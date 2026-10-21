using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SvmApi.Application.Audit;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Application.Risk;
using SvmApi.Application.Verification;
using SvmApi.Application.Workflow;
using SvmApi.Domain.Verification;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Domain.Workflow;
using SvmApi.Infrastructure.Auth.DataScope;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Workflow;

/// <summary>
/// <see cref="IWorkflowService"/> (C7 · US-018/019 · FR-22/23 · BR-27/13): โหลดแถว <c>config_statusTransition</c> ตอน runtime,
/// ตัดสินด้วย <see cref="WorkflowGuard"/> (pure) แล้วเขียน instance + <c>trans_statusHistory</c> + audit ใน transaction เดียว.
/// <list type="bullet">
/// <item>instance กรองด้วย data scope (fail-closed); มีอยู่แต่นอก scope = <see cref="WorkflowOutcome.OutOfScope"/> (ตอบ 404).</item>
/// <item>permission ของแต่ละ transition ตรวจที่นี่กับ <see cref="ICurrentUser"/> (ไม่ใช่แค่ policy ของ controller).</item>
/// <item>rowVersion ไม่ตรง หรือชนกันตอนบันทึก = <see cref="WorkflowOutcome.Conflict"/> (409, EX-57).</item>
/// <item>audit ไม่เก็บข้อความเหตุผล (อาจมี PII) — เก็บเฉพาะ id + สถานะเดิม/ใหม่; เหตุผลอยู่ใน history.</item>
/// </list>
/// </summary>
public sealed class WorkflowService(
    SvmDbContext db,
    IUnitOfWork unitOfWork,
    IAuditService audit,
    IEvidenceChecker evidence,
    IApprovalChecker approvals,
    ICurrentUser user,
    TimeProvider clock,
    ILogger<WorkflowService> logger) : IWorkflowService
{
    public const string AuditSource = "WorkflowService";
    public const string ActionTransition = "instance.transition";
    public const string ActionReopen = "instance.reopen";
    public const int MaxReasonLength = 1000;
    private const int MaxHistoryRows = 500;

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ------------------------------------------------------------------ can-transition

    public async Task<WorkflowResult<CanTransitionDto>> CanTransitionAsync(
        Guid instanceId, VulnerabilityStatus toStatus, CancellationToken cancellationToken)
    {
        var (instance, failure) = await LoadScopedAsync(instanceId, track: false, cancellationToken);
        if (instance is null)
        {
            return WorkflowResult<CanTransitionDto>.Fail(failure!.Value, WorkflowErrorCodes.InstanceNotFound);
        }

        var decision = await DecideAsync(instance, toStatus, cancellationToken);
        return WorkflowResult<CanTransitionDto>.Ok(new CanTransitionDto(decision.Allowed, decision.Code, decision.Message));
    }

    // ------------------------------------------------------------------ transition

    public async Task<WorkflowResult<TransitionResultDto>> TransitionAsync(
        Guid instanceId, TransitionRequest request, CancellationToken cancellationToken)
    {
        var userId = user.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

        var (toStatus, reason, expected, errors) = Validate(request);
        if (toStatus is null)
        {
            return WorkflowResult<TransitionResultDto>.Invalid(errors);
        }

        var (instance, failure) = await LoadScopedAsync(instanceId, track: true, cancellationToken);
        if (instance is null)
        {
            return WorkflowResult<TransitionResultDto>.Fail(failure!.Value, WorkflowErrorCodes.InstanceNotFound);
        }

        // EX-57: ตรวจ rowVersion ก่อน guard อื่น (ผู้ใช้ถือข้อมูลเก่า)
        if (WorkflowGuard.IsStale(expected, instance.RowVersion))
        {
            return ConcurrencyConflict();
        }

        var decision = await DecideAsync(instance, toStatus.Value, cancellationToken);
        if (!decision.Allowed)
        {
            logger.LogWarning(
                "workflow.transition.denied instanceId={InstanceId} from={From} to={To} code={Code}",
                instance.Id, instance.Status, toStatus.Value, decision.Code);
            return WorkflowResult<TransitionResultDto>.Fail(decision.Outcome, decision.Code!, decision.Message);
        }

        return await ApplyAsync(instance, toStatus.Value, reason, userId, ActionTransition, cancellationToken);
    }

    // ------------------------------------------------------------------ reopen (BR-13)

    public async Task<WorkflowResult<TransitionResultDto>> ReopenAsync(
        Guid instanceId, string reason, CancellationToken cancellationToken)
    {
        var userId = user.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return WorkflowResult<TransitionResultDto>.Invalid(
                new Dictionary<string, string[]> { ["reason"] = [WorkflowErrorCodes.ReasonRequired] },
                WorkflowErrorCodes.ReasonRequired);
        }

        if (trimmed.Length > MaxReasonLength)
        {
            trimmed = trimmed[..MaxReasonLength];
        }

        var (instance, failure) = await LoadScopedAsync(instanceId, track: true, cancellationToken);
        if (instance is null)
        {
            return WorkflowResult<TransitionResultDto>.Fail(failure!.Value, WorkflowErrorCodes.InstanceNotFound);
        }

        var rule = await LoadRuleAsync(instance.Status, VulnerabilityStatus.Reopened, cancellationToken);
        var decision = WorkflowGuard.CanReopen(instance.Status, rule);
        if (!decision.Allowed)
        {
            return WorkflowResult<TransitionResultDto>.Fail(decision.Outcome, decision.Code!, decision.Message);
        }

        return await ApplyAsync(instance, VulnerabilityStatus.Reopened, trimmed, userId, ActionReopen, cancellationToken);
    }

    // ------------------------------------------------------------------ history

    public async Task<WorkflowResult<InstanceHistoryDto>> GetHistoryAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var (instance, failure) = await LoadScopedAsync(instanceId, track: false, cancellationToken);
        if (instance is null)
        {
            return WorkflowResult<InstanceHistoryDto>.Fail(failure!.Value, WorkflowErrorCodes.InstanceNotFound);
        }

        var statusRows = await db.StatusHistories.AsNoTracking()
            .Where(h => h.InstanceId == instanceId)
            .OrderByDescending(h => h.ChangedAt)
            .Take(MaxHistoryRows)
            .Select(h => new { h.Id, h.FromStatus, h.ToStatus, h.Reason, h.ChangedBy, h.ChangedAt })
            .ToListAsync(cancellationToken);

        var riskRows = await db.RiskScoreHistories.AsNoTracking()
            .Where(h => h.InstanceId == instanceId)
            .OrderByDescending(h => h.CreatedAt)
            .Take(MaxHistoryRows)
            .Select(h => new { h.Id, h.Score, h.RiskLevel, h.CalculationType, h.PreviousScore, h.Reason, h.CreatedBy, h.CreatedAt })
            .ToListAsync(cancellationToken);

        var dto = new InstanceHistoryDto(
            instance.Id,
            instance.Status.ToString(),
            statusRows
                .Select(h => new StatusHistoryDto(h.Id, h.FromStatus.ToString(), h.ToStatus.ToString(), h.Reason, h.ChangedBy, h.ChangedAt))
                .ToList(),
            riskRows
                .Select(h => new RiskHistoryItemDto(
                    h.Id, h.Score, h.RiskLevel.ToString(), h.CalculationType.ToString(), h.PreviousScore, h.Reason, h.CreatedBy, h.CreatedAt))
                .ToList());

        return WorkflowResult<InstanceHistoryDto>.Ok(dto);
    }

    // ------------------------------------------------------------------ helpers

    private static (VulnerabilityStatus? To, string? Reason, byte[]? RowVersion, IReadOnlyDictionary<string, string[]> Errors) Validate(
        TransitionRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        VulnerabilityStatus? to = null;

        var rawTo = request?.ToStatus?.Trim();
        if (string.IsNullOrEmpty(rawTo))
        {
            errors["toStatus"] = [WorkflowErrorCodes.ToStatusRequired];
        }
        else if (!int.TryParse(rawTo, out _)
                 && Enum.TryParse<VulnerabilityStatus>(rawTo, ignoreCase: true, out var parsed)
                 && Enum.IsDefined(parsed))
        {
            to = parsed;
        }
        else
        {
            errors["toStatus"] = [WorkflowErrorCodes.InvalidStatus];
        }

        var reason = request?.Reason?.Trim();
        if (reason is { Length: > MaxReasonLength })
        {
            errors["reason"] = [WorkflowErrorCodes.ReasonTooLong];
        }
        else if (to is { } target && WorkflowGuard.ReasonRequired(target) && string.IsNullOrEmpty(reason))
        {
            errors["reason"] = [WorkflowErrorCodes.ReasonRequired];
        }

        var rowVersion = RiskScoringEngine.ParseRowVersion(request?.RowVersion, required: true, out var rvError);
        if (rvError is not null)
        {
            errors["rowVersion"] = [rvError];
        }

        if (errors.Count > 0)
        {
            return (null, null, null, errors);
        }

        return (to, string.IsNullOrEmpty(reason) ? null : reason, rowVersion, errors);
    }

    /// <summary>
    /// ตัดสิน transition: รอบแรกตรวจ rule + สิทธิ์ (ยังไม่ query evidence/approval),
    /// ผ่านแล้วจึงอ่าน evidence/approval เฉพาะที่ rule บังคับ แล้วตัดสินรอบสุดท้าย.
    /// </summary>
    private async Task<TransitionDecision> DecideAsync(VulnerabilityInstance instance, VulnerabilityStatus to, CancellationToken ct)
    {
        var rule = await LoadRuleAsync(instance.Status, to, ct);
        var permissions = user.Permissions;

        var pre = WorkflowGuard.CanTransition(instance.Status, to, rule, permissions, hasEvidence: true, hasApproval: true);
        if (!pre.Allowed)
        {
            return pre;
        }

        var hasEvidence = !rule!.RequiresEvidence || await evidence.HasRequiredEvidenceAsync(instance.Id, ct);
        var hasApproval = !rule.RequiresApproval || await approvals.HasApprovalAsync(instance.Id, to, ct);
        var decision = WorkflowGuard.CanTransition(instance.Status, to, rule, permissions, hasEvidence, hasApproval);
        if (!decision.Allowed)
        {
            return decision;
        }

        return await CheckIndependentVerificationAsync(instance, to, ct) ?? decision;
    }

    /// <summary>
    /// BR-16 / EX-80: Critical จะเป็น Resolved/Closed ได้ต้องมี independent verification (Verified) ในรอบปัจจุบัน
    /// (หลัง Reopened ล่าสุด). คืน null = ผ่าน/ไม่เกี่ยวข้อง. ตัดสินด้วย <see cref="VerificationRules"/> (pure).
    /// </summary>
    private async Task<TransitionDecision?> CheckIndependentVerificationAsync(
        VulnerabilityInstance instance, VulnerabilityStatus to, CancellationToken ct)
    {
        if (!VerificationRules.RequiresIndependentVerifier(instance.RiskLevel)
            || to is not (VulnerabilityStatus.Resolved or VulnerabilityStatus.Closed))
        {
            return null;
        }

        var since = await db.StatusHistories.AsNoTracking()
            .Where(h => h.InstanceId == instance.Id && h.ToStatus == VulnerabilityStatus.Reopened)
            .MaxAsync(h => (DateTime?)h.ChangedAt, ct);

        var records = await db.VerificationRecords.AsNoTracking()
            .Where(r => r.InstanceId == instance.Id && r.Result == VerificationResult.Verified && r.IsIndependent)
            .Select(r => new { r.Result, r.IsIndependent, r.VerifiedAt })
            .ToListAsync(ct);

        var snapshots = records.Select(r => new VerificationSnapshot(r.Result, r.IsIndependent, r.VerifiedAt)).ToList();
        if (VerificationRules.IsClosureAllowed(instance.RiskLevel, to, snapshots, since))
        {
            return null;
        }

        return TransitionDecision.Deny(
            WorkflowOutcome.IndependentVerificationRequired, WorkflowErrorCodes.IndependentVerificationRequired,
            "Critical findings require independent verification before they can be resolved or closed.");
    }

    private async Task<TransitionRule?> LoadRuleAsync(VulnerabilityStatus from, VulnerabilityStatus to, CancellationToken ct)
        => await db.StatusTransitions.AsNoTracking()
            .Where(t => t.FromStatus == from && t.ToStatus == to)
            .Select(t => new TransitionRule(t.FromStatus, t.ToStatus, t.RequiredPermission, t.RequiresEvidence, t.RequiresApproval, t.IsActive))
            .FirstOrDefaultAsync(ct);

    private async Task<(VulnerabilityInstance? Instance, WorkflowOutcome? Failure)> LoadScopedAsync(
        Guid instanceId, bool track, CancellationToken ct)
    {
        var query = db.VulnerabilityInstances.ApplyDataScope(user);
        if (!track)
        {
            query = query.AsNoTracking();
        }

        var instance = await query.FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is not null)
        {
            return (instance, null);
        }

        var exists = await db.VulnerabilityInstances.AsNoTracking().AnyAsync(i => i.Id == instanceId, ct);
        return (null, exists ? WorkflowOutcome.OutOfScope : WorkflowOutcome.NotFound);
    }

    /// <summary>เปลี่ยนสถานะ + history + audit ใน transaction เดียว; ชนกันตอนบันทึก = 409.</summary>
    private async Task<WorkflowResult<TransitionResultDto>> ApplyAsync(
        VulnerabilityInstance instance,
        VulnerabilityStatus to,
        string? reason,
        Guid userId,
        string action,
        CancellationToken cancellationToken)
    {
        var from = instance.Status;
        var now = clock.GetUtcNow().UtcDateTime;

        instance.Status = to;
        instance.UpdatedAt = now;
        instance.UpdatedBy = userId;

        var history = new StatusHistory
        {
            Id = Guid.NewGuid(),
            InstanceId = instance.Id,
            FromStatus = from,
            ToStatus = to,
            Reason = reason,
            ChangedBy = userId,
            ChangedAt = now,
        };
        db.StatusHistories.Add(history);

        try
        {
            await unitOfWork.ExecuteAsync(async ct =>
            {
                await audit.AppendAsync(
                    new AuditEntry(
                        userId, action, "VulnerabilityInstance", instance.Id.ToString(),
                        JsonSerializer.Serialize(new { status = from.ToString() }, Json),
                        JsonSerializer.Serialize(
                            new { status = to.ToString(), historyId = history.Id, hasReason = reason is not null }, Json),
                        "Success", AuditSource),
                    ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return ConcurrencyConflict();
        }

        logger.LogInformation(
            "workflow.transitioned instanceId={InstanceId} from={From} to={To} action={Action}",
            instance.Id, from, to, action);

        return WorkflowResult<TransitionResultDto>.Ok(new TransitionResultDto(
            instance.Id, from.ToString(), to.ToString(), history.Id, now, Convert.ToBase64String(instance.RowVersion)));
    }

    private static WorkflowResult<TransitionResultDto> ConcurrencyConflict()
        => WorkflowResult<TransitionResultDto>.Fail(
            WorkflowOutcome.Conflict, WorkflowErrorCodes.ConcurrencyConflict,
            "The record was changed by another user. Reload and retry.");
}
