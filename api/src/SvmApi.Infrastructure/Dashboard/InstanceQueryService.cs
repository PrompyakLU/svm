using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Application.Dashboard;
using SvmApi.Application.Sla;
using SvmApi.Application.Workflow;
using SvmApi.Domain.Risk;
using SvmApi.Domain.Sla;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Infrastructure.Auth.DataScope;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Dashboard;

/// <summary>
/// <see cref="IInstanceQueryService"/> (C11 · FR-56/58): รายการ + รายละเอียด instance แบบอ่านอย่างเดียว.
/// scope ก่อนทุก filter/count/paging (<c>ApplyDataScope</c> fail-closed); id นอก scope = <see cref="DashboardOutcome.OutOfScope"/>
/// (controller ตอบ 404 + denied-access audit). ไม่เขียน audit/ข้อมูลใด ๆ. ทุกคำสั่งเป็น <c>AsNoTracking</c> และมีเพดานจำนวนแถว.
/// </summary>
public sealed class InstanceQueryService(
    SvmDbContext db,
    ICurrentUser user,
    TimeProvider clock,
    IOptions<SlaOptions> slaOptions) : IInstanceQueryService
{
    // ------------------------------------------------------------------ list

    public async Task<DashboardResult<PagedResult<InstanceListItemDto>>> SearchAsync(
        InstanceListQuery query, CancellationToken cancellationToken)
    {
        var rule = InstanceQueryRules.Validate(query);
        if (!rule.IsValid)
        {
            return DashboardResult<PagedResult<InstanceListItemDto>>.Invalid(rule.Errors);
        }

        var p = rule.Value!;
        var now = clock.GetUtcNow().UtcDateTime;
        var fraction = slaOptions.Value.NearDueFraction;

        // scope ก่อนทุก filter/count/paging (FR-63, BR-30)
        var scoped = db.VulnerabilityInstances.AsNoTracking().ApplyDataScope(user);
        var filtered = scoped.ApplyFilters(p, user.UserId, now, fraction);

        var total = await filtered.LongCountAsync(cancellationToken);

        var skip = InstanceQueryRules.ComputeSkip(p.Page, p.PageSize);
        if (skip is null || total == 0)
        {
            return DashboardResult<PagedResult<InstanceListItemDto>>.Ok(new PagedResult<InstanceListItemDto>([], p.Page, p.PageSize, total));
        }

        var page = filtered.ApplySort(p.Sort).Skip(skip.Value).Take(p.PageSize);
        var items = await InstanceListProjection.LoadAsync(db, page, now, fraction, cancellationToken);

        return DashboardResult<PagedResult<InstanceListItemDto>>.Ok(new PagedResult<InstanceListItemDto>(items, p.Page, p.PageSize, total));
    }

    // ------------------------------------------------------------------ detail

    public async Task<DashboardResult<InstanceDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var fraction = slaOptions.Value.NearDueFraction;

        var row = await db.VulnerabilityInstances.AsNoTracking()
            .ApplyDataScope(user)
            .Where(i => i.Id == id)
            .Select(i => new DetailRow
            {
                Id = i.Id,
                Title = i.Title,
                RowVersion = i.RowVersion,
                AssetId = i.AssetId,
                AssetCode = i.Asset.AssetCode,
                AssetName = i.Asset.Name,
                AssetType = i.Asset.AssetType,
                IpAddress = i.Asset.IpAddress,
                Hostname = i.Asset.Hostname,
                Environment = i.Asset.Environment,
                BusinessUnit = i.Asset.BusinessUnit,
                Criticality = i.Asset.Criticality,
                InternetExposure = i.Asset.InternetExposure,
                AssetStatus = i.Asset.Status,
                CveId = i.CveId,
                CweId = i.CweId,
                Location = i.Location,
                Port = i.Port,
                Component = i.Component,
                Status = i.Status,
                RiskScore = i.RiskScore,
                RiskLevel = i.RiskLevel,
                IsKev = i.IsKev,
                AssignedToUserId = i.AssignedToUserId,
                FirstDetectedAt = i.FirstDetectedAt,
                LastDetectedAt = i.LastDetectedAt,
                UpdatedAt = i.UpdatedAt,
                SourceSystem = i.Findings.OrderByDescending(f => f.DetectedAt).Select(f => f.SourceSystem).FirstOrDefault(),
                SlaStartedAt = (DateTime?)i.SlaTimer!.StartedAt,
                SlaDue = (DateTime?)i.SlaTimer!.ResolutionDue,
                SlaStored = (SlaBreachStatus?)i.SlaTimer!.BreachStatus,
                PolicyLevel = (RiskLevel?)i.SlaTimer!.Policy.RiskLevel,
                PolicyResolveHours = (int?)i.SlaTimer!.Policy.ResolveHours,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            // แยก "ไม่มีจริง" ออกจาก "มีแต่นอก scope" (EX-07)
            var exists = await db.VulnerabilityInstances.AsNoTracking().AnyAsync(i => i.Id == id, cancellationToken);
            return DashboardResult<InstanceDetailDto>.Fail(
                exists ? DashboardOutcome.OutOfScope : DashboardOutcome.NotFound, DashboardErrorCodes.InstanceNotFound);
        }

        // ---- ข้อมูลย่อย (แต่ละ query มีเพดานแถว; เรียงใหม่สุดก่อน)
        var statusRows = await db.StatusHistories.AsNoTracking()
            .Where(h => h.InstanceId == id)
            .OrderByDescending(h => h.ChangedAt)
            .Take(InstanceQueryRules.MaxHistoryRows)
            .Select(h => new { h.Id, h.FromStatus, h.ToStatus, h.Reason, h.ChangedAt, h.ChangedBy })
            .ToListAsync(cancellationToken);

        var riskRows = await db.RiskScoreHistories.AsNoTracking()
            .Where(h => h.InstanceId == id)
            .OrderByDescending(h => h.CreatedAt)
            .Take(InstanceQueryRules.MaxHistoryRows)
            .Select(h => new { h.Id, h.Score, h.RiskLevel, h.CalculationType, h.Reason, h.PreviousScore, h.CreatedAt, h.CreatedBy })
            .ToListAsync(cancellationToken);

        // breakdown ล่าสุดที่เป็นการคำนวณอัตโนมัติ (Auto/Recalc); การปรับด้วยมือไม่มี factor
        var breakdownJson = await db.RiskScoreHistories.AsNoTracking()
            .Where(h => h.InstanceId == id && h.CalculationType != RiskCalculationType.Manual)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => h.FactorBreakdown)
            .FirstOrDefaultAsync(cancellationToken);

        var evidenceRows = await db.Evidences.AsNoTracking()
            .Where(e => e.InstanceId == id)
            .OrderByDescending(e => e.UploadedAt)
            .Take(InstanceQueryRules.MaxEvidenceRows)
            .Select(e => new { e.Id, e.TaskId, e.FileName, e.ContentType, e.Version, e.FileSize, e.ScanStatus, e.UploadedAt, e.UploadedBy })
            .ToListAsync(cancellationToken);

        var commentRows = await db.Comments.AsNoTracking()
            .Where(c => c.InstanceId == id)
            .OrderByDescending(c => c.CreatedAt)
            .Take(InstanceQueryRules.MaxCommentRows)
            .Select(c => new { c.Id, c.Body, c.CreatedAt, c.CreatedBy })
            .ToListAsync(cancellationToken);

        var verificationRows = await db.VerificationRecords.AsNoTracking()
            .Where(v => v.InstanceId == id)
            .OrderByDescending(v => v.VerifiedAt)
            .Take(InstanceQueryRules.MaxVerificationRows)
            .Select(v => new { v.Id, v.Method, v.Result, v.IsIndependent, v.VerifiedBy, v.VerifiedAt, v.Notes })
            .ToListAsync(cancellationToken);

        var task = await db.RemediationTasks.AsNoTracking()
            .Where(t => t.InstanceId == id)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id, t.Recommendation, t.OwnerUserId, t.OwnerTeam, t.DueDate, t.ProgressPercent,
                t.ActionPlan, t.RootCause, t.TargetDate, t.Obstacle, t.Status,
            })
            .FirstOrDefaultAsync(cancellationToken);

        var transitions = await db.StatusTransitions.AsNoTracking()
            .Where(t => t.FromStatus == row.Status && t.IsActive)
            .OrderBy(t => t.ToStatus)
            .Select(t => new { t.ToStatus, t.RequiresEvidence, t.RequiresApproval, t.RequiredPermission })
            .ToListAsync(cancellationToken);

        // ---- ชื่อผู้ใช้ (query เดียว)
        var userIds = new HashSet<Guid>();
        AddId(userIds, row.AssignedToUserId);
        foreach (var s in statusRows) { userIds.Add(s.ChangedBy); }
        foreach (var r in riskRows) { AddId(userIds, r.CreatedBy); }
        foreach (var e in evidenceRows) { userIds.Add(e.UploadedBy); }
        foreach (var c in commentRows) { userIds.Add(c.CreatedBy); }
        foreach (var v in verificationRows) { userIds.Add(v.VerifiedBy); }
        if (task is not null) { AddId(userIds, task.OwnerUserId); }

        var names = await InstanceListProjection.LoadNamesAsync(db, userIds, cancellationToken);

        // ---- ประกอบ DTO
        var canTransition = user.HasPermission(WorkflowGuard.DefaultPermission);

        var dto = new InstanceDetailDto(
            row.Id,
            InstanceQueryRules.DisplayCode(row.Id),
            row.Title,
            Convert.ToBase64String(row.RowVersion),
            new InstanceAssetDto(
                row.AssetId, row.AssetCode, row.AssetName, row.AssetType.ToString(), row.IpAddress, row.Hostname,
                row.Environment.ToString(), row.BusinessUnit, row.Criticality?.ToString(), row.InternetExposure,
                row.AssetStatus.ToString()),
            row.CveId,
            row.CweId,
            row.Location,
            row.Port,
            row.Component,
            row.Status.ToString(),
            row.RiskScore,
            row.RiskLevel?.ToString(),
            row.IsKev,
            row.AssignedToUserId,
            NameOf(names, row.AssignedToUserId),
            SlaCalculator.AsUtc(row.FirstDetectedAt),
            SlaCalculator.AsUtc(row.LastDetectedAt),
            InstanceQueryRules.AgeDays(row.FirstDetectedAt, now),
            row.SourceSystem,
            row.UpdatedAt is { } updated ? SlaCalculator.AsUtc(updated) : null,
            InstanceListProjection.ToSla(
                row.Status, row.SlaStartedAt, row.SlaDue, row.SlaStored, row.PolicyLevel, row.PolicyResolveHours, now, fraction),
            DashboardMath.ParseRiskFactors(breakdownJson),
            riskRows.Select(r => new RiskHistoryEntryDto(
                r.Id, r.Score, r.RiskLevel.ToString(), r.CalculationType.ToString(), r.Reason, r.PreviousScore,
                SlaCalculator.AsUtc(r.CreatedAt), r.CreatedBy, NameOf(names, r.CreatedBy) ?? SystemActor)).ToList(),
            statusRows.Select(s => new StatusHistoryEntryDto(
                s.Id, s.FromStatus.ToString(), s.ToStatus.ToString(), s.Reason, SlaCalculator.AsUtc(s.ChangedAt),
                s.ChangedBy, NameOf(names, s.ChangedBy) ?? SystemActor)).ToList(),
            evidenceRows.Select(e => new EvidenceEntryDto(
                e.Id, e.TaskId, e.FileName, e.ContentType, e.Version, e.FileSize, e.ScanStatus.ToString(),
                SlaCalculator.AsUtc(e.UploadedAt), e.UploadedBy, NameOf(names, e.UploadedBy) ?? SystemActor)).ToList(),
            commentRows.Select(c => new CommentEntryDto(
                c.Id, c.Body, SlaCalculator.AsUtc(c.CreatedAt), c.CreatedBy, NameOf(names, c.CreatedBy) ?? SystemActor)).ToList(),
            task is null
                ? null
                : new RemediationSummaryDto(
                    task.Id, task.Recommendation, task.OwnerUserId, NameOf(names, task.OwnerUserId), task.OwnerTeam,
                    SlaCalculator.AsUtc(task.DueDate), task.ProgressPercent, task.ActionPlan, task.RootCause,
                    task.TargetDate is { } target ? SlaCalculator.AsUtc(target) : null, task.Obstacle, task.Status.ToString()),
            verificationRows.Select(v => new VerificationEntryDto(
                v.Id, v.Method.ToString(), v.Result.ToString(), v.IsIndependent, v.VerifiedBy,
                NameOf(names, v.VerifiedBy) ?? SystemActor, SlaCalculator.AsUtc(v.VerifiedAt), v.Notes)).ToList(),
            transitions.Select(t => new AvailableTransitionDto(
                t.ToStatus.ToString(),
                t.RequiresEvidence,
                t.RequiresApproval,
                t.RequiredPermission,
                canTransition && user.HasPermission(string.IsNullOrWhiteSpace(t.RequiredPermission)
                    ? WorkflowGuard.DefaultPermission
                    : t.RequiredPermission.Trim()))).ToList());

        return DashboardResult<InstanceDetailDto>.Ok(dto);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>ชื่อที่แสดงเมื่อผู้กระทำเป็นระบบ/ไม่พบผู้ใช้ (เช่น คำนวณ risk อัตโนมัติ).</summary>
    private const string SystemActor = "system";

    private static void AddId(HashSet<Guid> set, Guid? id)
    {
        if (id is { } value)
        {
            set.Add(value);
        }
    }

    private static string? NameOf(IReadOnlyDictionary<Guid, string> names, Guid? id)
        => id is { } value && names.TryGetValue(value, out var name) ? name : null;

    private sealed class DetailRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = default!;
        public byte[] RowVersion { get; set; } = default!;
        public Guid AssetId { get; set; }
        public string AssetCode { get; set; } = default!;
        public string AssetName { get; set; } = default!;
        public Domain.Assets.AssetType AssetType { get; set; }
        public string? IpAddress { get; set; }
        public string? Hostname { get; set; }
        public Domain.Assets.AssetEnvironment Environment { get; set; }
        public string? BusinessUnit { get; set; }
        public Domain.Assets.AssetCriticality? Criticality { get; set; }
        public bool InternetExposure { get; set; }
        public Domain.Assets.AssetStatus AssetStatus { get; set; }
        public string? CveId { get; set; }
        public string? CweId { get; set; }
        public string? Location { get; set; }
        public int? Port { get; set; }
        public string? Component { get; set; }
        public VulnerabilityStatus Status { get; set; }
        public decimal? RiskScore { get; set; }
        public RiskLevel? RiskLevel { get; set; }
        public bool IsKev { get; set; }
        public Guid? AssignedToUserId { get; set; }
        public DateTime FirstDetectedAt { get; set; }
        public DateTime LastDetectedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? SourceSystem { get; set; }
        public DateTime? SlaStartedAt { get; set; }
        public DateTime? SlaDue { get; set; }
        public SlaBreachStatus? SlaStored { get; set; }
        public RiskLevel? PolicyLevel { get; set; }
        public int? PolicyResolveHours { get; set; }
    }
}

/// <summary>projection รายการ instance + ชื่อผู้ใช้ (ใช้ร่วมกับ dashboard เพื่อให้รูปแบบแถวเหมือนกัน).</summary>
internal static class InstanceListProjection
{
    public static async Task<IReadOnlyList<InstanceListItemDto>> LoadAsync(
        SvmDbContext db,
        IQueryable<VulnerabilityInstance> page,
        DateTime now,
        double nearDueFraction,
        CancellationToken cancellationToken)
    {
        var rows = await page
            .Select(i => new ListRow
            {
                Id = i.Id,
                Title = i.Title,
                AssetId = i.AssetId,
                AssetCode = i.Asset.AssetCode,
                AssetName = i.Asset.Name,
                BusinessUnit = i.Asset.BusinessUnit,
                InternetExposure = i.Asset.InternetExposure,
                Criticality = i.Asset.Criticality,
                CveId = i.CveId,
                CweId = i.CweId,
                Location = i.Location,
                Port = i.Port,
                Component = i.Component,
                Status = i.Status,
                RiskScore = i.RiskScore,
                RiskLevel = i.RiskLevel,
                IsKev = i.IsKev,
                AssignedToUserId = i.AssignedToUserId,
                FirstDetectedAt = i.FirstDetectedAt,
                LastDetectedAt = i.LastDetectedAt,
                SourceSystem = i.Findings.OrderByDescending(f => f.DetectedAt).Select(f => f.SourceSystem).FirstOrDefault(),
                SlaStartedAt = (DateTime?)i.SlaTimer!.StartedAt,
                SlaDue = (DateTime?)i.SlaTimer!.ResolutionDue,
                SlaStored = (SlaBreachStatus?)i.SlaTimer!.BreachStatus,
                PolicyLevel = (RiskLevel?)i.SlaTimer!.Policy.RiskLevel,
                PolicyResolveHours = (int?)i.SlaTimer!.Policy.ResolveHours,
            })
            .ToListAsync(cancellationToken);

        var assigneeIds = rows.Where(r => r.AssignedToUserId is not null).Select(r => r.AssignedToUserId!.Value).ToHashSet();
        var names = await LoadNamesAsync(db, assigneeIds, cancellationToken);

        return rows.Select(r => new InstanceListItemDto(
            r.Id,
            InstanceQueryRules.DisplayCode(r.Id),
            r.Title,
            r.AssetId,
            r.AssetCode,
            r.AssetName,
            r.BusinessUnit,
            r.InternetExposure,
            r.Criticality?.ToString(),
            r.CveId,
            r.CweId,
            r.Location,
            r.Port,
            r.Component,
            r.Status.ToString(),
            r.RiskScore,
            r.RiskLevel?.ToString(),
            r.IsKev,
            r.AssignedToUserId,
            r.AssignedToUserId is { } uid && names.TryGetValue(uid, out var name) ? name : null,
            SlaCalculator.AsUtc(r.FirstDetectedAt),
            SlaCalculator.AsUtc(r.LastDetectedAt),
            InstanceQueryRules.AgeDays(r.FirstDetectedAt, now),
            r.SourceSystem,
            ToSla(r.Status, r.SlaStartedAt, r.SlaDue, r.SlaStored, r.PolicyLevel, r.PolicyResolveHours, now, nearDueFraction)))
            .ToList();
    }

    public static async Task<IReadOnlyDictionary<Guid, string>> LoadNamesAsync(
        SvmDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var list = ids.ToList();
        return await db.Users.AsNoTracking()
            .Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
    }

    public static SlaSummaryDto? ToSla(
        VulnerabilityStatus status,
        DateTime? startedAt,
        DateTime? due,
        SlaBreachStatus? stored,
        RiskLevel? policyLevel,
        int? policyResolveHours,
        DateTime now,
        double nearDueFraction)
    {
        if (startedAt is not { } started || due is not { } resolutionDue)
        {
            return null;
        }

        var policyName = policyLevel is { } level && policyResolveHours is { } hours
            ? InstanceQueryRules.PolicyName(level, hours)
            : "SLA";

        return InstanceQueryRules.BuildSla(
            policyName, status, started, resolutionDue, stored ?? SlaBreachStatus.OnTrack, now, nearDueFraction);
    }

    private sealed class ListRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = default!;
        public Guid AssetId { get; set; }
        public string AssetCode { get; set; } = default!;
        public string AssetName { get; set; } = default!;
        public string? BusinessUnit { get; set; }
        public bool InternetExposure { get; set; }
        public Domain.Assets.AssetCriticality? Criticality { get; set; }
        public string? CveId { get; set; }
        public string? CweId { get; set; }
        public string? Location { get; set; }
        public int? Port { get; set; }
        public string? Component { get; set; }
        public VulnerabilityStatus Status { get; set; }
        public decimal? RiskScore { get; set; }
        public RiskLevel? RiskLevel { get; set; }
        public bool IsKev { get; set; }
        public Guid? AssignedToUserId { get; set; }
        public DateTime FirstDetectedAt { get; set; }
        public DateTime LastDetectedAt { get; set; }
        public string? SourceSystem { get; set; }
        public DateTime? SlaStartedAt { get; set; }
        public DateTime? SlaDue { get; set; }
        public SlaBreachStatus? SlaStored { get; set; }
        public RiskLevel? PolicyLevel { get; set; }
        public int? PolicyResolveHours { get; set; }
    }
}
