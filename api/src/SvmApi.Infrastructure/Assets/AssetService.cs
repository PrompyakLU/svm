using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SvmApi.Application.Assets;
using SvmApi.Application.Audit;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Domain.Assets;
using SvmApi.Infrastructure.Auth.DataScope;
using SvmApi.Infrastructure.Integration;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Assets;

/// <summary>
/// <see cref="IAssetService"/> บน <see cref="SvmDbContext"/> (C2 · US-004/005/006 · FR-01..04).
/// ทุก list/by-id ผ่าน <see cref="DataScopeExtensions.ApplyDataScope(IQueryable{Asset}, ICurrentUser)"/> ก่อน count/paging.
/// ทุกการเขียนอยู่ใน <see cref="IUnitOfWork"/> เดียวกับ audit row (atomic; audit ไม่ได้ = ปฏิเสธ action — EX-98).
/// Criticality history: ไม่มีตารางเฉพาะใน data-model.md → เก็บเป็น audit event <c>asset.criticality.set</c>
/// (ผู้แก้ไข=userId, เวลา=timestamp, ก่อน/หลัง + เหตุผล) ซึ่ง immutable + hash chain; ค่าปัจจุบันอยู่ที่ master_asset.criticality.
/// </summary>
public sealed class AssetService(
    SvmDbContext db,
    IUnitOfWork unitOfWork,
    IAuditService audit,
    ICmdbAdapter cmdb,
    ICurrentUser user,
    TimeProvider clock,
    ILogger<AssetService> logger) : IAssetService
{
    public const string AuditSource = "AssetService";
    public const string SyncAuditSource = "CmdbSync";

    public const string ActionCreate = AssetAuditActions.Create;
    public const string ActionUpdate = AssetAuditActions.Update;
    public const string ActionCriticality = AssetAuditActions.Criticality;
    public const string ActionSync = AssetAuditActions.Sync;
    public const string ActionSyncReview = AssetAuditActions.SyncReview;

    private const int MaxIssues = 50;
    private const int MaxIssueMessageLength = 300;

    // จำนวนแถว audit สูงสุดที่อ่านเพื่อสร้างประวัติ criticality ของ asset หนึ่งตัว (กัน query ใหญ่เกิน)
    private const int MaxHistoryAuditRows = 500;

    // sync รันได้ทีละรอบต่อ process (กัน upsert ชนกันเอง); หลาย instance = ชนกันที่ unique(assetCode) -> นับเป็น failed รายตัว
    private static readonly SemaphoreSlim SyncGate = new(1, 1);

    private static readonly JsonSerializerOptions AuditJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ---------------------------------------------------------------- read

    public async Task<AssetResult<PagedResult<AssetDto>>> SearchAsync(AssetQuery query, CancellationToken cancellationToken)
    {
        var rule = AssetRules.ValidateQuery(query);
        if (!rule.IsValid)
        {
            return AssetResult<PagedResult<AssetDto>>.Invalid(rule.Errors);
        }

        var p = rule.Value!;

        // scope ก่อนทุก filter/count/paging (FR-63, BR-30)
        IQueryable<Asset> q = db.Assets.AsNoTracking().ApplyDataScope(user);

        if (p.Search is { } search)
        {
            q = q.Where(a => a.AssetCode.Contains(search)
                             || a.Name.Contains(search)
                             || (a.Hostname != null && a.Hostname.Contains(search))
                             || (a.IpAddress != null && a.IpAddress.Contains(search)));
        }

        if (p.AssetType is { } type) { q = q.Where(a => a.AssetType == type); }
        if (p.Environment is { } env) { q = q.Where(a => a.Environment == env); }
        if (p.BusinessUnit is { } bu) { q = q.Where(a => a.BusinessUnit == bu); }
        if (p.Criticality is { } criticality) { q = q.Where(a => a.Criticality == criticality); }
        if (p.InternetExposure is { } exposure) { q = q.Where(a => a.InternetExposure == exposure); }
        if (p.Status is { } status) { q = q.Where(a => a.Status == status); }

        if (p.CriticalityMissing is { } missing)
        {
            q = missing
                ? q.Where(a => a.Criticality == null && a.Environment == AssetEnvironment.Prod && a.Status == AssetStatus.Active)
                : q.Where(a => !(a.Criticality == null && a.Environment == AssetEnvironment.Prod && a.Status == AssetStatus.Active));
        }

        var total = await q.LongCountAsync(cancellationToken);

        var skip = (long)(p.Page - 1) * p.PageSize;
        if (skip > int.MaxValue)
        {
            return AssetResult<PagedResult<AssetDto>>.Ok(new PagedResult<AssetDto>([], p.Page, p.PageSize, total));
        }

        var rows = await q
            .Include(a => a.Ownerships)
            .OrderBy(a => a.AssetCode)
            .Skip((int)skip)
            .Take(p.PageSize)
            .ToListAsync(cancellationToken);

        var names = await LoadUserNamesAsync(rows.SelectMany(r => r.Ownerships), cancellationToken);
        var items = rows.Select(a => ToDto(a, a.Ownerships, names)).ToList();

        return AssetResult<PagedResult<AssetDto>>.Ok(new PagedResult<AssetDto>(items, p.Page, p.PageSize, total));
    }

    public async Task<AssetResult<AssetDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var (asset, failure) = await LoadScopedAsync(id, tracking: false, cancellationToken);
        if (asset is null)
        {
            return AssetResult<AssetDto>.Fail(failure, AssetErrorCodes.NotFound);
        }

        var names = await LoadUserNamesAsync(asset.Ownerships, cancellationToken);
        return AssetResult<AssetDto>.Ok(ToDto(asset, asset.Ownerships, names));
    }

    // ---------------------------------------------------------------- write

    public async Task<AssetResult<AssetDto>> CreateAsync(CreateAssetRequest request, CancellationToken cancellationToken)
    {
        var rule = AssetRules.ValidateCreate(request);
        if (!rule.IsValid)
        {
            return AssetResult<AssetDto>.Invalid(rule.Errors);
        }

        var input = rule.Value!;

        if (!await OwnersExistAsync(input.Owners, cancellationToken))
        {
            return OwnerNotFound();
        }

        var now = Now();
        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            AssetCode = input.AssetCode,
            Status = AssetStatus.Active,
            CreatedAt = now,
            CreatedBy = user.UserId,
        };
        ApplyFields(asset, input.Fields);

        var owners = input.Owners.Select(o => NewOwnership(asset.Id, o)).ToList();

        // FR-03: ไม่ได้ระบุ RemediationTeam -> ใช้ assignment rule เป็น fallback (ไม่บังคับ; ไม่มี rule ก็สร้างได้)
        var ruleId = await AddRuleTeamIfMissingAsync(asset, owners, cancellationToken);

        // ผู้ใช้สร้าง asset นอก scope ของตนเองไม่ได้ (จะเห็น/จัดการต่อไม่ได้) — fail closed
        if (!IsInScope(asset.AssetCode, asset.BusinessUnit, owners))
        {
            return AssetResult<AssetDto>.Fail(AssetOutcome.ScopeViolation, AssetErrorCodes.OutOfScope);
        }

        // ตรวจรหัสซ้ำหลัง scope check เพื่อไม่ให้ผู้ใช้ scope แคบใช้ 409 สอดแนมว่ามี assetCode ใดอยู่นอก scope
        if (await db.Assets.AsNoTracking().AnyAsync(a => a.AssetCode == input.AssetCode, cancellationToken))
        {
            return CodeExists();
        }

        try
        {
            await unitOfWork.ExecuteAsync(async ct =>
            {
                db.Assets.Add(asset);
                db.AssetOwnerships.AddRange(owners);
                await audit.AppendAsync(
                    new AuditEntry(user.UserId, ActionCreate, "Asset", asset.Id.ToString(), null,
                        Snapshot(asset, owners, ruleId is null ? null : new { assignmentRuleId = ruleId }),
                        "Success", AuditSource),
                    ct);
            }, cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await db.Assets.AsNoTracking().AnyAsync(a => a.AssetCode == input.AssetCode, cancellationToken))
            {
                return CodeExists();
            }

            throw;
        }

        var names = await LoadUserNamesAsync(owners, cancellationToken);
        return AssetResult<AssetDto>.Ok(ToDto(asset, owners, names));
    }

    public async Task<AssetResult<AssetDto>> UpdateAsync(Guid id, UpdateAssetRequest request, CancellationToken cancellationToken)
    {
        var (asset, failure) = await LoadScopedAsync(id, tracking: true, cancellationToken);
        if (asset is null)
        {
            return AssetResult<AssetDto>.Fail(failure, AssetErrorCodes.NotFound);
        }

        var rule = AssetRules.ValidateUpdate(request);
        if (!rule.IsValid)
        {
            return AssetResult<AssetDto>.Invalid(rule.Errors);
        }

        var input = rule.Value!;

        if (input.Owners is not null && !await OwnersExistAsync(input.Owners, cancellationToken))
        {
            return OwnerNotFound();
        }

        var replacement = input.Owners?.Select(o => NewOwnership(asset.Id, o)).ToList();
        var finalOwners = (IReadOnlyCollection<AssetOwnership>)(replacement ?? asset.Ownerships.ToList());

        // ผลลัพธ์หลังแก้ต้องยังอยู่ใน scope ของผู้แก้ (กันย้าย asset ออกนอก scope ตัวเอง)
        if (!IsInScope(asset.AssetCode, input.Fields.BusinessUnit, finalOwners))
        {
            return AssetResult<AssetDto>.Fail(AssetOutcome.ScopeViolation, AssetErrorCodes.OutOfScope);
        }

        var before = Snapshot(asset, asset.Ownerships.ToList());

        ApplyFields(asset, input.Fields);
        if (input.Status is { } status)
        {
            asset.Status = status;
        }

        asset.UpdatedAt = Now();
        asset.UpdatedBy = user.UserId;

        if (replacement is not null)
        {
            db.AssetOwnerships.RemoveRange(asset.Ownerships.ToList());
            db.AssetOwnerships.AddRange(replacement);
        }

        var after = Snapshot(asset, finalOwners);

        await unitOfWork.ExecuteAsync(
            ct => audit.AppendAsync(
                new AuditEntry(user.UserId, ActionUpdate, "Asset", asset.Id.ToString(), before, after, "Success", AuditSource),
                ct),
            cancellationToken);

        var names = await LoadUserNamesAsync(finalOwners, cancellationToken);
        return AssetResult<AssetDto>.Ok(ToDto(asset, finalOwners, names));
    }

    public async Task<AssetResult<AssetDto>> SetCriticalityAsync(
        Guid id, CriticalityRequest request, CancellationToken cancellationToken)
    {
        var (asset, failure) = await LoadScopedAsync(id, tracking: true, cancellationToken);
        if (asset is null)
        {
            return AssetResult<AssetDto>.Fail(failure, AssetErrorCodes.NotFound);
        }

        var rule = AssetRules.ValidateCriticality(request, asset.Criticality);
        if (!rule.IsValid)
        {
            return AssetResult<AssetDto>.Invalid(rule.Errors);
        }

        var change = rule.Value!;
        var before = JsonSerializer.Serialize(new { criticality = change.Previous?.ToString() }, AuditJson);
        var after = JsonSerializer.Serialize(
            new { criticality = change.New.ToString(), reason = change.Reason, changed = change.Changed }, AuditJson);

        asset.Criticality = change.New;
        asset.UpdatedAt = Now();
        asset.UpdatedBy = user.UserId;

        await unitOfWork.ExecuteAsync(
            ct => audit.AppendAsync(
                new AuditEntry(user.UserId, ActionCriticality, "Asset", asset.Id.ToString(), before, after, "Success", AuditSource),
                ct),
            cancellationToken);

        var owners = asset.Ownerships.ToList();
        var names = await LoadUserNamesAsync(owners, cancellationToken);
        return AssetResult<AssetDto>.Ok(ToDto(asset, owners, names));
    }

    public async Task<AssetResult<IReadOnlyList<CriticalityHistoryEntryDto>>> GetCriticalityHistoryAsync(
        Guid id, CancellationToken cancellationToken)
    {
        // scope ก่อนเสมอ (EX-07): นอก scope = OutOfScope -> controller ตอบ 404 + denied-access audit
        var (asset, failure) = await LoadScopedAsync(id, tracking: false, cancellationToken);
        if (asset is null)
        {
            return AssetResult<IReadOnlyList<CriticalityHistoryEntryDto>>.Fail(failure, AssetErrorCodes.NotFound);
        }

        // อ่าน audit trail โดยตรง (read-only; ไม่แตะ chain) เฉพาะ action ที่อาจเปลี่ยน criticality ของ asset นี้
        var objectId = asset.Id.ToString();
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(x => x.ObjectType == "Asset" && x.ObjectId == objectId
                        && (x.Action == ActionCriticality || x.Action == ActionCreate || x.Action == ActionUpdate))
            .OrderByDescending(x => x.Timestamp)
            .ThenByDescending(x => x.Id)
            .Take(MaxHistoryAuditRows)
            .Select(x => new AssetAuditEvent(x.Id, x.Action, x.UserId, x.BeforeValue, x.AfterValue, x.Source, x.Timestamp))
            .ToListAsync(cancellationToken);

        var userIds = rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        return AssetResult<IReadOnlyList<CriticalityHistoryEntryDto>>.Ok(AssetCriticalityHistory.Build(rows, names));
    }

    // ---------------------------------------------------------------- CMDB sync

    public async Task<AssetResult<AssetSyncResult>> SyncAsync(CancellationToken cancellationToken)
    {
        // sync เขียน/ทำเครื่องหมาย asset ทั้งองค์กร (ไม่สามารถกรองตาม scope ได้อย่างมีความหมาย)
        // จึงต้องมี scope Organization:* เท่านั้น — fail closed สำหรับผู้ถือ asset.sync ที่ scope แคบ
        if (!IsOrganizationWide())
        {
            return AssetResult<AssetSyncResult>.Fail(AssetOutcome.ScopeViolation, AssetErrorCodes.OutOfScope);
        }

        if (!await SyncGate.WaitAsync(0, cancellationToken))
        {
            return AssetResult<AssetSyncResult>.Fail(
                AssetOutcome.Conflict, AssetErrorCodes.SyncInProgress, "An asset sync is already running.");
        }

        try
        {
            return AssetResult<AssetSyncResult>.Ok(await RunSyncAsync(cancellationToken));
        }
        finally
        {
            SyncGate.Release();
        }
    }

    private async Task<AssetSyncResult> RunSyncAsync(CancellationToken cancellationToken)
    {
        var batchId = Guid.NewGuid();

        IReadOnlyList<CmdbAssetRecord> feed;
        try
        {
            // integration.md: timeout 3s + retry 3× exponential ก่อนถือว่า CMDB ไม่พร้อม
            feed = await CmdbResilience.ExecuteAsync(
                cmdb.GetAssetsAsync, CmdbRetrySettings.Default, clock, logger, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // EX-30: บันทึก integration error + คงข้อมูลเดิม (ไม่เขียนอะไรเลย) — log เฉพาะชนิด exception
            logger.LogWarning("asset.sync.integration_error batchId={BatchId} reason={Reason}", batchId, ex.GetType().Name);
            return await RecordIntegrationErrorAsync(batchId, "CMDB_UNAVAILABLE", cancellationToken);
        }

        if (feed is null || feed.Count == 0)
        {
            // feed ว่างเปล่าถือว่าผิดปกติ: ถ้าเชื่อจะทำให้ asset ทุกตัวถูกระบุว่า "หาย" (EX-31)
            logger.LogWarning("asset.sync.integration_error batchId={BatchId} reason=EmptyFeed", batchId);
            return await RecordIntegrationErrorAsync(batchId, "CMDB_EMPTY_FEED", cancellationToken);
        }

        int created = 0, updated = 0, unchanged = 0, failed = 0, unmapped = 0;
        var issues = new List<AssetSyncIssue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in feed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var externalId = AssetRules.Clean(record?.ExternalId) ?? AssetSyncPlanner.UnknownExternalId;

            if (record is null)
            {
                unmapped++;
                AddIssue(issues, externalId, "Unmapped", "Empty record.");
                continue;
            }

            if (externalId != AssetSyncPlanner.UnknownExternalId && !seen.Add(externalId))
            {
                unmapped++;
                AddIssue(issues, externalId, "Unmapped", "Duplicate externalId in the same feed.");
                continue;
            }

            try
            {
                var decision = await unitOfWork.ExecuteAsync(ct => ApplyRecordAsync(record, batchId, ct), cancellationToken);

                switch (decision.Action)
                {
                    case SyncAction.Create: created++; break;
                    case SyncAction.Update: updated++; break;
                    case SyncAction.Unchanged: unchanged++; break;
                    default:
                        unmapped++;
                        AddIssue(issues, decision.ExternalId, "Unmapped", string.Join("; ", decision.Problems));
                        logger.LogWarning(
                            "asset.sync.unmapped batchId={BatchId} externalId={ExternalId}", batchId, decision.ExternalId);
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // record เดียวล้มเหลวต้องไม่ล้ม batch (components.md C2 error handling)
                db.ChangeTracker.Clear();
                failed++;
                AddIssue(issues, externalId, "Failed", "The record could not be saved.");
                logger.LogWarning(
                    "asset.sync.record_failed batchId={BatchId} externalId={ExternalId} reason={Reason}",
                    batchId, externalId, ex.GetType().Name);
            }
        }

        // EX-31/BR-38: asset ที่ไม่อยู่ใน feed -> ทำเครื่องหมายทบทวน (audit event) ไม่ลบ ไม่เปลี่ยนสถานะ
        var reviewCount = await MarkMissingForReviewAsync(batchId, feed, cancellationToken);

        var summary = JsonSerializer.Serialize(
            new
            {
                batchId,
                status = AssetSyncStatus.Completed,
                created,
                updated,
                unchanged,
                failed,
                unmapped,
                review = reviewCount,
            },
            AuditJson);

        await audit.RecordAsync(
            new AuditEntry(user.UserId, ActionSync, "AssetSync", batchId.ToString(), null, summary, "Success", SyncAuditSource),
            cancellationToken);

        logger.LogInformation(
            "asset.sync.completed batchId={BatchId} created={Created} updated={Updated} unchanged={Unchanged} failed={Failed} unmapped={Unmapped} review={Review}",
            batchId, created, updated, unchanged, failed, unmapped, reviewCount);

        return new AssetSyncResult(
            batchId, AssetSyncStatus.Completed, created + updated + unchanged, failed, unmapped,
            created, updated, unchanged, reviewCount, issues);
    }

    /// <summary>upsert หนึ่ง record + audit ใน transaction เดียว (รันซ้ำได้ — เริ่มจาก ChangeTracker ว่าง).</summary>
    private async Task<SyncDecision> ApplyRecordAsync(CmdbAssetRecord record, Guid batchId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();

        var code = AssetRules.Clean(record.ExternalId);
        var existing = code is null
            ? null
            : await db.Assets.Include(a => a.Ownerships).FirstOrDefaultAsync(a => a.AssetCode == code, ct);

        var decision = AssetSyncPlanner.Decide(existing, record);
        var mapped = decision.Mapped;

        if (decision.Action == SyncAction.Create && mapped is not null)
        {
            var asset = AssetSyncPlanner.NewAsset(mapped);
            asset.Id = Guid.NewGuid();
            asset.CreatedAt = Now();
            asset.CreatedBy = user.UserId;

            var owners = await BuildSourceOwnershipsAsync(asset.Id, mapped, batchId, ct);

            db.Assets.Add(asset);
            db.AssetOwnerships.AddRange(owners);

            await audit.AppendAsync(
                new AuditEntry(user.UserId, ActionCreate, "Asset", asset.Id.ToString(), null,
                    Snapshot(asset, owners, new { source = "cmdb", batchId }), "Success", SyncAuditSource),
                ct);
        }
        else if (decision.Action == SyncAction.Update && existing is not null && mapped is not null)
        {
            var before = Snapshot(existing, existing.Ownerships.ToList());

            AssetSyncPlanner.ApplyUpdate(existing, mapped);
            existing.UpdatedAt = Now();
            existing.UpdatedBy = user.UserId;

            await audit.AppendAsync(
                new AuditEntry(user.UserId, ActionUpdate, "Asset", existing.Id.ToString(), before,
                    Snapshot(existing, existing.Ownerships.ToList(),
                        new { source = "cmdb", batchId, changedFields = decision.ChangedFields }),
                    "Success", SyncAuditSource),
                ct);
        }

        return decision;
    }

    /// <summary>เชื่อม owner จาก source (FR-03): user ต้องมีอยู่และ active มิฉะนั้นข้าม (EX-25) — asset ยังถูกสร้าง.</summary>
    private async Task<List<AssetOwnership>> BuildSourceOwnershipsAsync(
        Guid assetId, MappedCmdbAsset mapped, Guid batchId, CancellationToken ct)
    {
        var owners = new List<AssetOwnership>();
        Guid? ownerUserId = null;

        if (mapped.OwnerUsername is { } username)
        {
            ownerUserId = await db.Users.AsNoTracking()
                .Where(u => u.Username == username && u.IsActive)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(ct);

            if (ownerUserId is null)
            {
                logger.LogWarning(
                    "asset.sync.owner_not_found batchId={BatchId} assetCode={AssetCode}", batchId, mapped.AssetCode);
            }
        }

        var teamOnOwner = mapped.OwnerType == OwnerType.RemediationTeam && ownerUserId is not null;

        if (ownerUserId is not null)
        {
            owners.Add(NewOwnership(assetId, new ValidOwner(mapped.OwnerType, ownerUserId, teamOnOwner ? mapped.TeamName : null)));
        }

        if (mapped.TeamName is not null && !teamOnOwner)
        {
            owners.Add(NewOwnership(assetId, new ValidOwner(OwnerType.RemediationTeam, null, mapped.TeamName)));
        }

        // FR-03: source ไม่ระบุทีม -> fallback ด้วย assignment rule
        var probe = new Asset { BusinessUnit = mapped.Fields.BusinessUnit, AssetType = mapped.Fields.AssetType };
        await AddRuleTeamIfMissingAsync(probe, owners, ct, assetId);

        return owners;
    }

    /// <summary>
    /// FR-03: ถ้า <paramref name="owners"/> ยังไม่มี RemediationTeam ให้ match <c>config_assignmentRule</c>
    /// (<see cref="AssetOwnerRules.Match"/>) แล้วเพิ่ม ownership RemediationTeam. user ใน rule ต้องมีอยู่และ active
    /// มิฉะนั้นผูกเฉพาะชื่อทีม (EX-25). คืน rule id ที่ใช้ (null = ไม่ได้ใช้ rule).
    /// </summary>
    private async Task<Guid?> AddRuleTeamIfMissingAsync(
        Asset asset, List<AssetOwnership> owners, CancellationToken ct, Guid? assetId = null)
    {
        if (AssetOwnerRules.HasRemediationTeam(owners.Select(o => o.OwnerType)))
        {
            return null;
        }

        var rules = await db.AssignmentRules.AsNoTracking().Where(r => r.IsActive).ToListAsync(ct);
        var match = AssetOwnerRules.Match(rules, asset.BusinessUnit, asset.AssetType);
        if (match is null)
        {
            return null;
        }

        var userId = match.UserId;
        if (userId is { } uid && !await db.Users.AsNoTracking().AnyAsync(u => u.Id == uid && u.IsActive, ct))
        {
            userId = null;
        }

        owners.Add(NewOwnership(assetId ?? asset.Id, new ValidOwner(OwnerType.RemediationTeam, userId, match.TeamName)));
        return match.RuleId;
    }

    private async Task<int> MarkMissingForReviewAsync(
        Guid batchId, IReadOnlyList<CmdbAssetRecord> feed, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();

        var active = await db.Assets.AsNoTracking()
            .Where(a => a.Status == AssetStatus.Active)
            .Select(a => new { a.Id, a.AssetCode })
            .ToListAsync(cancellationToken);

        var missing = AssetSyncPlanner.FindMissing(active.Select(a => a.AssetCode), feed.Select(r => r?.ExternalId));
        if (missing.Count == 0)
        {
            return 0;
        }

        var idByCode = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in active)
        {
            idByCode.TryAdd(a.AssetCode, a.Id);
        }

        var marked = 0;
        var detail = JsonSerializer.Serialize(new { batchId, reason = "NOT_IN_CMDB_FEED" }, AuditJson);

        foreach (var code in missing)
        {
            if (!idByCode.TryGetValue(code, out var assetId))
            {
                continue;
            }

            try
            {
                await audit.RecordAsync(
                    new AuditEntry(user.UserId, ActionSyncReview, "Asset", assetId.ToString(), null, detail,
                        "ReviewRequired", SyncAuditSource),
                    cancellationToken);
                marked++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    "asset.sync.review_mark_failed batchId={BatchId} reason={Reason}", batchId, ex.GetType().Name);
            }
        }

        return marked;
    }

    private async Task<AssetSyncResult> RecordIntegrationErrorAsync(Guid batchId, string reason, CancellationToken ct)
    {
        var detail = JsonSerializer.Serialize(
            new { batchId, status = AssetSyncStatus.IntegrationError, reason }, AuditJson);

        await audit.RecordAsync(
            new AuditEntry(user.UserId, ActionSync, "AssetSync", batchId.ToString(), null, detail, "Failed", SyncAuditSource),
            ct);

        return new AssetSyncResult(batchId, AssetSyncStatus.IntegrationError, 0, 0, 0, 0, 0, 0, 0, []);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<(Asset? Asset, AssetOutcome Failure)> LoadScopedAsync(Guid id, bool tracking, CancellationToken ct)
    {
        IQueryable<Asset> q = db.Assets.Include(a => a.Ownerships);
        if (!tracking)
        {
            q = q.AsNoTracking();
        }

        var asset = await q.ApplyDataScope(user).Where(a => a.Id == id).FirstOrDefaultAsync(ct);
        if (asset is not null)
        {
            return (asset, AssetOutcome.Success);
        }

        // แยก "ไม่มีจริง" ออกจาก "มีแต่นอก scope" (EX-07): กรณีหลังผู้เรียกต้องบันทึก denied-access แต่ตอบเหมือน NotFound
        var exists = await db.Assets.AsNoTracking().AnyAsync(a => a.Id == id, ct);
        return (null, exists ? AssetOutcome.OutOfScope : AssetOutcome.NotFound);
    }

    private async Task<bool> OwnersExistAsync(IReadOnlyList<ValidOwner> owners, CancellationToken ct)
    {
        var ids = owners.Where(o => o.OwnerUserId is not null).Select(o => o.OwnerUserId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return true;
        }

        var found = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(ct);

        return found.Count == ids.Count;
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadUserNamesAsync(
        IEnumerable<AssetOwnership> ownerships, CancellationToken ct)
    {
        var ids = ownerships.Where(o => o.OwnerUserId is not null).Select(o => o.OwnerUserId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    /// <summary>ตรวจ scope ของสถานะที่จะเกิดขึ้น โดยใช้ filter กลางเดียวกับ query (ประเมินในหน่วยความจำ).</summary>
    private bool IsInScope(string assetCode, string? businessUnit, IEnumerable<AssetOwnership> owners)
    {
        var candidate = new Asset { AssetCode = assetCode, BusinessUnit = businessUnit, Ownerships = owners.ToList() };
        return new[] { candidate }.AsQueryable().ApplyDataScope(user).Any();
    }

    /// <summary>
    /// true เฉพาะผู้ใช้ที่มี <c>Organization:*</c>: asset สมมติ (assetCode ว่าง, ไม่มี BU/owner) ไม่ตรง grant
    /// BusinessUnit/Asset/Responsibility ใด ๆ จึงผ่าน filter กลางได้ก็ต่อเมื่อ scope ครอบคลุมทั้งหมด.
    /// </summary>
    private bool IsOrganizationWide()
    {
        var probe = new Asset { AssetCode = string.Empty, BusinessUnit = null, Ownerships = [] };
        return new[] { probe }.AsQueryable().ApplyDataScope(user).Any();
    }

    private static void ApplyFields(Asset asset, AssetFields f)
    {
        asset.Name = f.Name;
        asset.AssetType = f.AssetType;
        asset.IpAddress = f.IpAddress;
        asset.Hostname = f.Hostname;
        asset.Url = f.Url;
        asset.Environment = f.Environment;
        asset.BusinessUnit = f.BusinessUnit;
        asset.InternetExposure = f.InternetExposure;
        asset.CloudResourceId = f.CloudResourceId;
        asset.RepositoryId = f.RepositoryId;
    }

    private static AssetOwnership NewOwnership(Guid assetId, ValidOwner o) => new()
    {
        Id = Guid.NewGuid(),
        AssetId = assetId,
        OwnerType = o.OwnerType,
        OwnerUserId = o.OwnerUserId,
        TeamName = o.TeamName,
    };

    private static AssetDto ToDto(
        Asset a, IEnumerable<AssetOwnership> owners, IReadOnlyDictionary<Guid, string> names)
    {
        var ownerDtos = owners
            .OrderBy(o => o.OwnerType)
            .ThenBy(o => o.TeamName, StringComparer.Ordinal)
            .Select(o => new AssetOwnerDto(
                o.Id,
                o.OwnerType.ToString(),
                o.OwnerUserId,
                o.OwnerUserId is { } uid && names.TryGetValue(uid, out var name) ? name : null,
                o.TeamName))
            .ToList();

        return new AssetDto(
            a.Id,
            a.AssetCode,
            a.Name,
            a.AssetType.ToString(),
            a.IpAddress,
            a.Hostname,
            a.Url,
            a.Environment.ToString(),
            a.BusinessUnit,
            a.Criticality?.ToString(),
            a.InternetExposure,
            a.Status.ToString(),
            a.CloudResourceId,
            a.RepositoryId,
            ownerDtos,
            AssetRules.GetWarnings(a.Environment, a.Status, a.Criticality, ownerDtos.Count),
            DateTime.SpecifyKind(a.CreatedAt, DateTimeKind.Utc),
            a.UpdatedAt is { } updated ? DateTime.SpecifyKind(updated, DateTimeKind.Utc) : null);
    }

    /// <summary>JSON สำหรับ audit before/after — เฉพาะ field ของ asset (ไม่มี secret); ผ่าน AuditRedactor อีกชั้นตอน append.</summary>
    private static string Snapshot(Asset a, IEnumerable<AssetOwnership> owners, object? meta = null)
        => JsonSerializer.Serialize(
            new
            {
                assetCode = a.AssetCode,
                name = a.Name,
                assetType = a.AssetType.ToString(),
                ipAddress = a.IpAddress,
                hostname = a.Hostname,
                url = a.Url,
                environment = a.Environment.ToString(),
                businessUnit = a.BusinessUnit,
                criticality = a.Criticality?.ToString(),
                internetExposure = a.InternetExposure,
                status = a.Status.ToString(),
                cloudResourceId = a.CloudResourceId,
                repositoryId = a.RepositoryId,
                owners = owners
                    .OrderBy(o => o.OwnerType.ToString(), StringComparer.Ordinal)
                    .ThenBy(o => o.TeamName, StringComparer.Ordinal)
                    .Select(o => new { ownerType = o.OwnerType.ToString(), ownerUserId = o.OwnerUserId, teamName = o.TeamName })
                    .ToList(),
                meta,
            },
            AuditJson);

    private static void AddIssue(List<AssetSyncIssue> issues, string externalId, string kind, string message)
    {
        if (issues.Count >= MaxIssues)
        {
            return;
        }

        var text = message.Length > MaxIssueMessageLength ? message[..MaxIssueMessageLength] : message;
        issues.Add(new AssetSyncIssue(externalId, kind, text));
    }

    private static AssetResult<AssetDto> OwnerNotFound()
        => AssetResult<AssetDto>.Fail(
            AssetOutcome.BusinessRule, AssetErrorCodes.OwnerNotFound, "One or more owners do not exist or are inactive.");

    private static AssetResult<AssetDto> CodeExists()
        => AssetResult<AssetDto>.Fail(
            AssetOutcome.Conflict, AssetErrorCodes.CodeExists, "An asset with this assetCode already exists.");

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
