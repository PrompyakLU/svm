using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SvmApi.Application.Audit;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Application.Sla;
using SvmApi.Domain.Assets;
using SvmApi.Domain.Sla;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Infrastructure.Auth.DataScope;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Sla;

/// <summary>
/// <see cref="ISlaService"/> (C9 · US-024/025 · FR-35..37 · BR-05/08/09/10): โหลด policy + business calendar จาก DB ตอน runtime,
/// ตัดสินด้วย <see cref="SlaCalculator"/> (pure) แล้วเขียน <c>trans_slaTimer</c> + audit ใน transaction เดียว.
/// <list type="bullet">
/// <item><see cref="GetAsync"/> กรองด้วย data scope (fail-closed); มีอยู่แต่นอก scope = <see cref="SlaOutcome.OutOfScope"/> (ตอบ 404).</item>
/// <item><see cref="CalculateAsync"/> เป็นงานระดับระบบ (ไม่กรอง scope) — ผู้เรียกตรวจสิทธิ์มาก่อน; timer เริ่มนับที่ <c>firstDetectedAt</c> (BR-09).</item>
/// <item>BR-05: asset Active Prod ไม่มี criticality = <see cref="SlaErrorCodes.CriticalityMissing"/> และไม่สร้าง timer.</item>
/// <item>audit ไม่เก็บข้อความอิสระ — เก็บ id, due date, รหัสเหตุผล (<see cref="SlaTrigger"/>).</item>
/// </list>
/// ข้อจำกัด MVP: ไม่มี rowversion บน timer (สร้างซ้ำถูกกันด้วย unique index <c>instanceId</c> → 409); job ทำงานอินสแตนซ์เดียว.
/// </summary>
public sealed class SlaService(
    SvmDbContext db,
    IUnitOfWork unitOfWork,
    IAuditService audit,
    ICurrentUser user,
    TimeProvider clock,
    IOptions<SlaOptions> options,
    ILogger<SlaService> logger) : ISlaService
{
    public const string AuditSource = "SlaService";
    public const string ActionCreated = "sla.timer.created";
    public const string ActionRecalculated = "sla.timer.recalculated";

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed record InstanceFacts(
        Guid Id,
        VulnerabilityStatus Status,
        RiskLevel? RiskLevel,
        bool IsKev,
        DateTime FirstDetectedAt,
        AssetEnvironment Environment,
        AssetStatus AssetStatus,
        AssetCriticality? Criticality,
        bool InternetExposure);

    // ------------------------------------------------------------------ read

    public async Task<SlaResult<SlaTimerDto>> GetAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var facts = await db.VulnerabilityInstances.ApplyDataScope(user).AsNoTracking()
            .Where(i => i.Id == instanceId)
            .Select(i => new InstanceFacts(
                i.Id, i.Status, i.RiskLevel, i.IsKev, i.FirstDetectedAt,
                i.Asset.Environment, i.Asset.Status, i.Asset.Criticality, i.Asset.InternetExposure))
            .FirstOrDefaultAsync(cancellationToken);

        if (facts is null)
        {
            var exists = await db.VulnerabilityInstances.AsNoTracking().AnyAsync(i => i.Id == instanceId, cancellationToken);
            return SlaResult<SlaTimerDto>.Fail(
                exists ? SlaOutcome.OutOfScope : SlaOutcome.NotFound, SlaErrorCodes.InstanceNotFound);
        }

        var timer = await db.SlaTimers.AsNoTracking().FirstOrDefaultAsync(t => t.InstanceId == instanceId, cancellationToken);
        if (timer is null)
        {
            var blocked = SlaCalculator.CheckEligibility(
                facts.Status, facts.RiskLevel, facts.Environment, facts.AssetStatus, facts.Criticality);
            return SlaResult<SlaTimerDto>.Ok(SlaCalculator.NotStarted(instanceId, blocked));
        }

        return SlaResult<SlaTimerDto>.Ok(ToDto(facts, timer));
    }

    // ------------------------------------------------------------------ create / recalc

    public async Task<SlaResult<SlaTimerDto>> CalculateAsync(Guid instanceId, SlaTrigger trigger, CancellationToken cancellationToken)
    {
        var facts = await db.VulnerabilityInstances.AsNoTracking()
            .Where(i => i.Id == instanceId)
            .Select(i => new InstanceFacts(
                i.Id, i.Status, i.RiskLevel, i.IsKev, i.FirstDetectedAt,
                i.Asset.Environment, i.Asset.Status, i.Asset.Criticality, i.Asset.InternetExposure))
            .FirstOrDefaultAsync(cancellationToken);
        if (facts is null)
        {
            return SlaResult<SlaTimerDto>.Fail(SlaOutcome.NotFound, SlaErrorCodes.InstanceNotFound);
        }

        // BR-05 / EX-65: ไม่สร้าง timer เมื่อข้อมูลยังไม่พอ
        var blocked = SlaCalculator.CheckEligibility(
            facts.Status, facts.RiskLevel, facts.Environment, facts.AssetStatus, facts.Criticality);
        if (blocked is not null)
        {
            logger.LogWarning("sla.calculate.blocked instanceId={InstanceId} code={Code}", instanceId, blocked);
            return SlaResult<SlaTimerDto>.Fail(SlaOutcome.Unprocessable, blocked);
        }

        var riskLevel = facts.RiskLevel!.Value;
        var subject = new SlaSubject(riskLevel, facts.Criticality, facts.InternetExposure, facts.IsKev);
        var startedAt = SlaCalculator.AsUtc(facts.FirstDetectedAt);

        var policies = await db.SlaPolicies.AsNoTracking()
            .Where(p => p.IsActive && p.RiskLevel == riskLevel)
            .Select(p => new SlaPolicyCandidate(
                p.Id, p.RiskLevel, p.AssetCriticality, p.InternetExposure, p.KevOnly,
                p.AcknowledgeHours, p.StartHours, p.ResolveHours, p.IsActive))
            .ToListAsync(cancellationToken);

        var policy = SlaCalculator.SelectPolicy(policies, subject);
        if (policy is null)
        {
            // EX-66
            logger.LogWarning("sla.calculate.no_policy instanceId={InstanceId} riskLevel={RiskLevel}", instanceId, riskLevel);
            return SlaResult<SlaTimerDto>.Fail(SlaOutcome.Unprocessable, SlaErrorCodes.PolicyNotFound);
        }

        var calendarFrom = DateOnly.FromDateTime(startedAt).AddDays(-1);
        var calendarRows = await db.BusinessCalendars.AsNoTracking()
            .Where(c => c.Date >= calendarFrom)
            .Select(c => new { c.Date, c.IsWorkingDay })
            .ToListAsync(cancellationToken);
        var calendar = new SlaCalendar(calendarRows.Select(c => new KeyValuePair<DateOnly, bool>(c.Date, c.IsWorkingDay)));

        var due = SlaCalculator.ComputeDueDates(startedAt, policy, calendar);
        if (!due.Succeeded)
        {
            // EX-67 (หรือ policy ผิดรูป)
            logger.LogWarning(
                "sla.calculate.failed instanceId={InstanceId} policyId={PolicyId} code={Code}", instanceId, policy.Id, due.ErrorCode);
            return SlaResult<SlaTimerDto>.Fail(SlaOutcome.Unprocessable, due.ErrorCode!);
        }

        var dates = due.Dates!;
        var now = clock.GetUtcNow().UtcDateTime;
        var o = options.Value;

        var timer = await db.SlaTimers.FirstOrDefaultAsync(t => t.InstanceId == instanceId, cancellationToken);
        string action;
        object before;

        if (timer is null)
        {
            action = ActionCreated;
            before = new { exists = false };
            timer = new SlaTimer
            {
                Id = Guid.NewGuid(),
                InstanceId = instanceId,
                AcknowledgeDue = dates.AcknowledgeDue,
                RemediationStartDue = dates.RemediationStartDue,
                ResolutionDue = dates.ResolutionDue,
                StartedAt = startedAt,
                PolicyId = policy.Id,
                BreachStatus = SlaCalculator.EvaluateBreach(startedAt, dates.ResolutionDue, now, o.NearDueFraction),
                EscalationLevel = 0,
            };
            db.SlaTimers.Add(timer);
        }
        else
        {
            var previousRisk = await db.SlaPolicies.AsNoTracking()
                .Where(p => p.Id == timer.PolicyId)
                .Select(p => (RiskLevel?)p.RiskLevel)
                .FirstOrDefaultAsync(cancellationToken);
            var riskIncreased = previousRisk is { } previous && SlaCalculator.IsMoreSevere(riskLevel, previous);

            var state = ToState(timer);
            var result = SlaCalculator.Recalculate(state, dates, policy.Id, riskIncreased, trigger, now);
            if (!result.Changed)
            {
                return SlaResult<SlaTimerDto>.Ok(ToDto(facts, timer));
            }

            action = ActionRecalculated;
            before = new
            {
                policyId = timer.PolicyId,
                acknowledgeDue = timer.AcknowledgeDue,
                remediationStartDue = timer.RemediationStartDue,
                resolutionDue = timer.ResolutionDue,
            };

            timer.AcknowledgeDue = result.Dates.AcknowledgeDue;
            timer.RemediationStartDue = result.Dates.RemediationStartDue;
            timer.ResolutionDue = result.Dates.ResolutionDue;
            timer.PolicyId = result.PolicyId;
            timer.PreviousDue = result.PreviousDueJson;
            timer.BreachStatus = SlaCalculator.EvaluateBreach(timer.StartedAt, timer.ResolutionDue, now, o.NearDueFraction);

            // due date ขยาย/สถานะดีขึ้น → ปล่อยให้ escalate ระดับเดิมได้อีกครั้งเมื่อกลับมาเกินกำหนด
            var level = SlaEscalationMatrix.Determine(
                timer.BreachStatus, SlaCalculator.DaysOverdue(timer.ResolutionDue, now), o.EffectiveOverdueEscalationDays);
            timer.EscalationLevel = Math.Min(timer.EscalationLevel, (int)level);
        }

        try
        {
            await unitOfWork.ExecuteAsync(async ct =>
            {
                await audit.AppendAsync(
                    new AuditEntry(
                        user.UserId, action, "VulnerabilityInstance", instanceId.ToString(),
                        JsonSerializer.Serialize(before, Json),
                        JsonSerializer.Serialize(
                            new
                            {
                                timerId = timer.Id,
                                policyId = timer.PolicyId,
                                riskLevel = riskLevel.ToString(),
                                acknowledgeDue = timer.AcknowledgeDue,
                                remediationStartDue = timer.RemediationStartDue,
                                resolutionDue = timer.ResolutionDue,
                                breachStatus = timer.BreachStatus.ToString(),
                                trigger = trigger.ToCode(),
                            },
                            Json),
                        "Success", AuditSource),
                    ct);
            }, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // สร้าง timer พร้อมกัน (unique instanceId) หรือชนกันตอนบันทึก
            db.ChangeTracker.Clear();
            logger.LogWarning("sla.timer.conflict instanceId={InstanceId} action={Action}", instanceId, action);
            return SlaResult<SlaTimerDto>.Fail(
                SlaOutcome.Conflict, SlaErrorCodes.ConcurrencyConflict, "The SLA timer was changed concurrently. Retry.");
        }

        logger.LogInformation(
            "{Event} instanceId={InstanceId} policyId={PolicyId} riskLevel={RiskLevel} breachStatus={BreachStatus} trigger={Trigger}",
            action, instanceId, timer.PolicyId, riskLevel, timer.BreachStatus, trigger.ToCode());

        return SlaResult<SlaTimerDto>.Ok(ToDto(facts, timer));
    }

    public async Task<bool> TryCalculateAsync(Guid instanceId, SlaTrigger trigger, CancellationToken cancellationToken)
    {
        try
        {
            var result = await CalculateAsync(instanceId, trigger, cancellationToken);
            return result.Succeeded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            logger.LogWarning(
                "sla.calculate.error instanceId={InstanceId} trigger={Trigger} reason={Reason}",
                instanceId, trigger.ToCode(), ex.GetType().Name);
            return false;
        }
    }

    // ------------------------------------------------------------------ mapping

    private SlaTimerDto ToDto(InstanceFacts facts, SlaTimer timer)
        => SlaCalculator.ToDto(
            facts.Id,
            SlaCalculator.IsTimerRunning(facts.Status),
            ToState(timer),
            clock.GetUtcNow().UtcDateTime,
            options.Value.NearDueFraction);

    private static SlaTimerState ToState(SlaTimer timer)
        => new(
            SlaCalculator.AsUtc(timer.StartedAt),
            new SlaDueDates(
                SlaCalculator.AsUtc(timer.AcknowledgeDue),
                SlaCalculator.AsUtc(timer.RemediationStartDue),
                SlaCalculator.AsUtc(timer.ResolutionDue)),
            timer.PolicyId,
            timer.BreachStatus,
            timer.PreviousDue,
            timer.EscalationLevel);
}
