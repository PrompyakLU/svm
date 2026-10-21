using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SvmApi.Application.Audit;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Application.Notifications;
using SvmApi.Application.Remediation;
using SvmApi.Application.Risk;
using SvmApi.Application.Workflow;
using SvmApi.Domain.Remediation;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Infrastructure.Auth.DataScope;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Remediation;

/// <summary>
/// <see cref="IAssignmentService"/> (C8 · US-020 · FR-24/25/26 · BR-06/07 · EX-59..62).
/// <list type="bullet">
/// <item>instance กรองด้วย data scope (fail-closed); นอก scope = <see cref="RemediationOutcome.OutOfScope"/> (404 + denied-access).</item>
/// <item>assignee ต้องมีอยู่ ใช้งานได้ (active/ไม่หมดอายุ) และมี scope ครอบคลุม asset ของ instance (EX-60).</item>
/// <item>ไม่ระบุ assignee = เลือกจาก <c>config_assignmentRule</c> (Priority น้อยก่อน); ไม่พบกฎ/assignee ของกฎใช้ไม่ได้ = Queued + แจ้งผู้ทำรายการ (EX-59/60).</item>
/// <item>เขียน instance.AssignedToUserId + remediation task + audit ใน transaction เดียว; แจ้งผู้รับหลัง commit (best-effort).</item>
/// <item>การมอบหมายไม่เปลี่ยนสถานะ workflow — ใช้ <c>POST /instances/{id}/transition</c> (Confirmed→Assigned).</item>
/// </list>
/// </summary>
public sealed class AssignmentService(
    SvmDbContext db,
    IUnitOfWork unitOfWork,
    IAuditService audit,
    INotificationService notifications,
    ICurrentUser user,
    TimeProvider clock,
    ILogger<AssignmentService> logger) : IAssignmentService
{
    public const string AuditSource = "AssignmentService";
    public const string ActionAssign = "instance.assign";
    public const string StatusAssigned = "Assigned";
    public const string StatusQueued = "Queued";

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ------------------------------------------------------------------ single

    public async Task<RemediationResult<AssignResultDto>> AssignAsync(
        Guid instanceId, AssignRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rowVersion = RiskScoringEngine.ParseRowVersion(request.RowVersion, required: false, out var rvError);
        if (rvError is not null)
        {
            return RemediationResult<AssignResultDto>.Invalid(
                new Dictionary<string, string[]> { ["rowVersion"] = [RemediationErrorCodes.InvalidRowVersion] });
        }

        var targetError = AssignmentPolicy.ValidateExplicitTarget(request.AssignToUserId, request.AssignToTeam);
        if (targetError is not null)
        {
            return RemediationResult<AssignResultDto>.Invalid(
                new Dictionary<string, string[]> { ["assignTo"] = [targetError] }, targetError);
        }

        return await AssignCoreAsync(
            instanceId, request.AssignToUserId, request.AssignToTeam, request.Reason, rowVersion, cancellationToken);
    }

    // ------------------------------------------------------------------ bulk (EX-61)

    public async Task<RemediationResult<BulkAssignResultDto>> BulkAssignAsync(
        BulkAssignRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ids = request.InstanceIds?.Where(i => i != Guid.Empty).Distinct().ToList() ?? [];
        if (ids.Count == 0)
        {
            return RemediationResult<BulkAssignResultDto>.Invalid(
                new Dictionary<string, string[]> { ["instanceIds"] = [RemediationErrorCodes.BulkEmpty] }, RemediationErrorCodes.BulkEmpty);
        }

        if (ids.Count > AssignmentPolicy.MaxBulkSize)
        {
            return RemediationResult<BulkAssignResultDto>.Invalid(
                new Dictionary<string, string[]> { ["instanceIds"] = [RemediationErrorCodes.BulkTooLarge] }, RemediationErrorCodes.BulkTooLarge);
        }

        var targetError = AssignmentPolicy.ValidateExplicitTarget(request.AssignToUserId, request.AssignToTeam);
        if (targetError is not null)
        {
            return RemediationResult<BulkAssignResultDto>.Invalid(
                new Dictionary<string, string[]> { ["assignTo"] = [targetError] }, targetError);
        }

        var assigned = new List<Guid>();
        var skipped = new List<BulkSkippedDto>();

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            db.ChangeTracker.Clear(); // แต่ละรายการเป็นหน่วยงานอิสระ

            var result = await AssignCoreAsync(
                id, request.AssignToUserId, request.AssignToTeam, request.Reason, expectedRowVersion: null, cancellationToken);

            switch (result.Outcome)
            {
                case RemediationOutcome.Success when result.Value!.Status == StatusAssigned:
                    assigned.Add(id);
                    break;
                case RemediationOutcome.Success:
                    skipped.Add(new BulkSkippedDto(id, result.Value!.Code ?? RemediationErrorCodes.NoAssignmentRule));
                    break;
                case RemediationOutcome.NotFound:
                case RemediationOutcome.OutOfScope:
                    // ไม่เปิดเผยว่า id มีอยู่หรือไม่ (EX-07/61)
                    skipped.Add(new BulkSkippedDto(id, RemediationErrorCodes.OutOfScope));
                    break;
                default:
                    skipped.Add(new BulkSkippedDto(id, result.Code ?? RemediationErrorCodes.ValidationError));
                    break;
            }
        }

        logger.LogInformation(
            "assignment.bulk.completed requested={Requested} assigned={Assigned} skipped={Skipped}",
            ids.Count, assigned.Count, skipped.Count);

        return RemediationResult<BulkAssignResultDto>.Ok(new BulkAssignResultDto(assigned, skipped));
    }

    // ------------------------------------------------------------------ core

    private async Task<RemediationResult<AssignResultDto>> AssignCoreAsync(
        Guid instanceId,
        Guid? requestedUserId,
        string? requestedTeam,
        string? reasonText,
        byte[]? expectedRowVersion,
        CancellationToken ct)
    {
        var actorId = user.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

        var instance = await db.VulnerabilityInstances.ApplyDataScope(user)
            .Include(i => i.Asset).ThenInclude(a => a.Ownerships)
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is null)
        {
            var exists = await db.VulnerabilityInstances.AsNoTracking().AnyAsync(i => i.Id == instanceId, ct);
            return RemediationResult<AssignResultDto>.Fail(
                exists ? RemediationOutcome.OutOfScope : RemediationOutcome.NotFound, RemediationErrorCodes.InstanceNotFound);
        }

        if (expectedRowVersion is not null && WorkflowGuard.IsStale(expectedRowVersion, instance.RowVersion))
        {
            return Conflict();
        }

        if (!AssignmentPolicy.CanAssignIn(instance.Status))
        {
            return RemediationResult<AssignResultDto>.Fail(
                RemediationOutcome.Unprocessable, RemediationErrorCodes.InvalidStatusForAssignment,
                "The instance is in a status that cannot be assigned.");
        }

        var task = await db.RemediationTasks
            .Where(t => t.InstanceId == instance.Id && t.Status != RemediationTaskStatus.Completed)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var hadPrevious = instance.AssignedToUserId is not null || task?.OwnerUserId is not null || task?.OwnerTeam is not null;

        var automatic = AssignmentPolicy.IsAutomatic(requestedUserId, requestedTeam);
        if (automatic && hadPrevious && task is not null)
        {
            // มอบหมายอัตโนมัติไม่ทับผู้รับผิดชอบที่มีอยู่แล้ว (รวมที่ officer กำหนดเอง)
            return RemediationResult<AssignResultDto>.Ok(
                ToResult(instance, task.Id, StatusAssigned, task.OwnerUserId, task.OwnerTeam, true, null));
        }

        Guid? targetUserId = requestedUserId is { } u && u != Guid.Empty ? u : null;
        var targetTeam = AssignmentPolicy.NormalizeTeam(requestedTeam);

        if (automatic)
        {
            var rules = await db.AssignmentRules.AsNoTracking()
                .Where(r => r.IsActive)
                .Select(r => new AssignmentRuleFacts(r.MatchType, r.MatchValue, r.AssignToUserId, r.AssignToTeam, r.Priority, r.IsActive))
                .ToListAsync(ct);

            var rule = AssignmentRuleMatcher.Match(
                rules, new AssetMatchFacts(instance.Asset.BusinessUnit, instance.Asset.AssetType.ToString(), instance.Component));
            if (rule is null)
            {
                return await QueueAsync(instance, actorId, RemediationErrorCodes.NoAssignmentRule, ct);
            }

            targetUserId = rule.AssignToUserId is { } ru && ru != Guid.Empty ? ru : null;
            targetTeam = targetUserId is null ? AssignmentPolicy.NormalizeTeam(rule.AssignToTeam) : null;
        }

        if (targetUserId is { } assigneeId)
        {
            var assigneeError = await ValidateAssigneeAsync(assigneeId, instance, ct);
            if (assigneeError is not null)
            {
                // กฎอัตโนมัติชี้ไปที่ผู้ใช้ที่ใช้ไม่ได้ → เข้าคิว + แจ้ง (EX-60); ระบุเอง → ปฏิเสธ 422
                return automatic
                    ? await QueueAsync(instance, actorId, assigneeError, ct)
                    : RemediationResult<AssignResultDto>.Fail(RemediationOutcome.Unprocessable, assigneeError);
            }
        }

        var changed = targetUserId != instance.AssignedToUserId
                      || !string.Equals(targetTeam, task?.OwnerTeam, StringComparison.OrdinalIgnoreCase)
                      || targetUserId != task?.OwnerUserId;

        if (hadPrevious && !changed && task is not null)
        {
            // idempotent: ผู้รับเดิมเหมือนเดิม → ไม่เขียนอะไร
            return RemediationResult<AssignResultDto>.Ok(ToResult(instance, task.Id, StatusAssigned, targetUserId, targetTeam, automatic, null));
        }

        var reason = AssignmentPolicy.NormalizeReason(reasonText);
        if (reason is { Length: > AssignmentPolicy.MaxReasonLength })
        {
            return RemediationResult<AssignResultDto>.Invalid(
                new Dictionary<string, string[]> { ["reason"] = [RemediationErrorCodes.TooLong] }, RemediationErrorCodes.TooLong);
        }

        if (AssignmentPolicy.ReasonRequired(automatic, hadPrevious, changed) && reason is null)
        {
            return RemediationResult<AssignResultDto>.Invalid(
                new Dictionary<string, string[]> { ["reason"] = [RemediationErrorCodes.ReasonRequired] }, RemediationErrorCodes.ReasonRequired);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var before = new { assignedTo = instance.AssignedToUserId, team = task?.OwnerTeam, taskId = task?.Id };

        if (task is null)
        {
            var slaDue = await db.SlaTimers.AsNoTracking()
                .Where(t => t.InstanceId == instance.Id)
                .Select(t => (DateTime?)t.ResolutionDue)
                .FirstOrDefaultAsync(ct);

            task = new RemediationTask
            {
                Id = Guid.NewGuid(),
                InstanceId = instance.Id,
                DueDate = AssignmentPolicy.DefaultDueDate(now, instance.RiskLevel, slaDue),
                RiskLevel = instance.RiskLevel ?? RiskLevel.Medium,
                Recommendation = BuildRecommendation(instance),
                Status = RemediationTaskStatus.Open,
                ProgressPercent = 0,
                CreatedAt = now,
                CreatedBy = actorId,
            };
            db.RemediationTasks.Add(task);
        }
        else
        {
            task.UpdatedAt = now;
            task.UpdatedBy = actorId;
        }

        task.OwnerUserId = targetUserId;
        task.OwnerTeam = targetTeam;
        instance.AssignedToUserId = targetUserId;
        instance.UpdatedAt = now;
        instance.UpdatedBy = actorId;

        var taskId = task.Id;
        try
        {
            await unitOfWork.ExecuteAsync(async token =>
            {
                await audit.AppendAsync(
                    new AuditEntry(
                        actorId, ActionAssign, "VulnerabilityInstance", instance.Id.ToString(),
                        JsonSerializer.Serialize(before, Json),
                        JsonSerializer.Serialize(
                            new { assignedTo = targetUserId, team = targetTeam, taskId, auto = automatic, reason }, Json),
                        "Success", AuditSource),
                    token);
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Conflict();
        }

        logger.LogInformation(
            "assignment.assigned instanceId={InstanceId} assignee={Assignee} auto={Auto}", instance.Id, targetUserId, automatic);

        if (targetUserId is { } notifyId && notifyId != actorId)
        {
            await TryNotifyAsync(
                new NotificationRequest(
                    notifyId, "ASSIGNMENT", "ได้รับมอบหมายช่องโหว่ใหม่",
                    $"ช่องโหว่ \"{TextSanitizer.Truncate(instance.Title, 200)}\" ถูกมอบหมายให้คุณ", instance.Id,
                    Urgent: instance.RiskLevel == RiskLevel.Critical),
                ct);
        }

        return RemediationResult<AssignResultDto>.Ok(ToResult(instance, taskId, StatusAssigned, targetUserId, targetTeam, automatic, null));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>คืน error code หรือ null เมื่อ assignee ใช้ได้ (มีอยู่ + active + มี scope ครอบคลุม asset).</summary>
    private async Task<string?> ValidateAssigneeAsync(Guid assigneeId, VulnerabilityInstance instance, CancellationToken ct)
    {
        var assignee = await db.Users.AsNoTracking()
            .Where(u => u.Id == assigneeId)
            .Select(u => new
            {
                u.IsActive,
                u.ExpiresAt,
                Scopes = u.UserScopes.Select(s => new { s.ScopeType, s.ScopeValue }).ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (assignee is null)
        {
            return RemediationErrorCodes.AssigneeNotFound;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (!assignee.IsActive || (assignee.ExpiresAt is { } exp && exp <= now))
        {
            return RemediationErrorCodes.AssigneeInactive;
        }

        var isOwner = instance.Asset.Ownerships.Any(o => o.OwnerUserId == assigneeId);
        var grants = assignee.Scopes.Select(s => new ScopeGrantFacts(s.ScopeType.ToString(), s.ScopeValue));
        return AssigneeScopeMatcher.HasScope(grants, instance.Asset.BusinessUnit, instance.Asset.AssetCode, isOwner)
            ? null
            : RemediationErrorCodes.AssigneeOutOfScope;
    }

    /// <summary>ไม่พบกฎ/assignee ใช้ไม่ได้ → คงสถานะ unassigned (คิว), audit + แจ้งผู้ทำรายการ (EX-59/60).</summary>
    private async Task<RemediationResult<AssignResultDto>> QueueAsync(
        VulnerabilityInstance instance, Guid actorId, string code, CancellationToken ct)
    {
        await audit.RecordAsync(
            new AuditEntry(
                actorId, ActionAssign, "VulnerabilityInstance", instance.Id.ToString(),
                null, JsonSerializer.Serialize(new { queued = true, code }, Json), "Queued", AuditSource),
            ct);

        logger.LogWarning("assignment.queued instanceId={InstanceId} code={Code}", instance.Id, code);

        await TryNotifyAsync(
            new NotificationRequest(
                actorId, "ASSIGNMENT_QUEUED", "มอบหมายอัตโนมัติไม่สำเร็จ",
                $"ช่องโหว่ \"{TextSanitizer.Truncate(instance.Title, 200)}\" ยังไม่มีผู้รับผิดชอบ ({code}) — โปรดมอบหมายด้วยตนเอง",
                instance.Id),
            ct);

        return RemediationResult<AssignResultDto>.Ok(ToResult(instance, null, StatusQueued, null, null, true, code));
    }

    private async Task TryNotifyAsync(NotificationRequest request, CancellationToken ct)
    {
        try
        {
            await notifications.NotifyAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // การแจ้งเตือนเป็น best-effort: ไม่ย้อนการมอบหมายที่ commit แล้ว
            logger.LogWarning(
                "assignment.notify.failed instanceId={InstanceId} type={Type} reason={Reason}",
                request.InstanceId, request.Type, ex.GetType().Name);
        }
    }

    private static string BuildRecommendation(VulnerabilityInstance instance)
    {
        var title = TextSanitizer.Truncate(TextSanitizer.Clean(instance.Title, allowLineBreaks: false), 300);
        return string.IsNullOrWhiteSpace(instance.CveId) ? $"Remediate: {title}" : $"Remediate: {title} ({instance.CveId})";
    }

    private static AssignResultDto ToResult(
        VulnerabilityInstance instance, Guid? taskId, string status, Guid? userId, string? team, bool auto, string? code)
        => new(instance.Id, status, userId, team, taskId, auto, code, Convert.ToBase64String(instance.RowVersion));

    private static RemediationResult<AssignResultDto> Conflict()
        => RemediationResult<AssignResultDto>.Fail(
            RemediationOutcome.Conflict, RemediationErrorCodes.ConcurrencyConflict,
            "The record was changed by another user. Reload and retry.");
}
