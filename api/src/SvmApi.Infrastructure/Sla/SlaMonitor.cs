using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SvmApi.Application.Audit;
using SvmApi.Application.Common;
using SvmApi.Application.Sla;
using SvmApi.Domain.Assets;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Infrastructure.Persistence;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Infrastructure.Sla;

/// <summary>
/// งานหนึ่งรอบของ <see cref="SlaMonitorJob"/> (US-025/026 · FR-36/39/40 · BR-10/11 · EX-68/70). ทำงานใน DI scope ของรอบนั้น:
/// <list type="number">
/// <item>สร้าง timer ให้ instance ที่มี risk level แต่ยังไม่มี timer (BR-09) — ข้ามเมื่อข้อมูลไม่พอ (BR-05/EX-65/66/67).</item>
/// <item>recalc timer ที่ risk level ของ instance ไม่ตรงกับ policy ที่ใช้อยู่ (US-025 — safety net ของ hook จาก risk recalculation).</item>
/// <item>ประเมิน breach/near-due แล้ว escalate ตาม <see cref="SlaEscalationMatrix"/>; ระดับ escalate ได้ครั้งเดียว (idempotent ด้วย <c>escalationLevel</c>).</item>
/// </list>
/// ความปลอดภัย: exception ของแต่ละ instance ถูกจับและข้ามไป (ไม่หยุดทั้งรอบ), ตรวจ cancellation ทุกรายการ,
/// ล้าง change tracker หลังแต่ละรายการ. การส่ง notification ทำ <b>หลัง</b> commit ระดับ escalation (at-most-once ต่อระดับ).
/// หนี้เทคนิค MVP: อินสแตนซ์เดียวเท่านั้น — ถ้าขยายหลายอินสแตนซ์ต้องเพิ่ม distributed lock (เช่น sp_getapplock) ครอบทั้งรอบ.
/// </summary>
public sealed class SlaMonitor(
    SvmDbContext db,
    ISlaService sla,
    IUnitOfWork unitOfWork,
    IAuditService audit,
    ISlaEscalationNotifier notifier,
    TimeProvider clock,
    IOptions<SlaOptions> options,
    ILogger<SlaMonitor> logger)
{
    public const string AuditSource = "SlaMonitor";
    public const string ActionBreachStatus = "sla.breach.status";
    public const string ActionEscalated = "sla.escalated";

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed record ActiveTimer(Guid InstanceId, RiskLevel? InstanceRisk, RiskLevel PolicyRisk);

    public async Task<SlaMonitorSummary> RunOnceAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        var cap = o.EffectiveMaxInstancesPerRun;
        var stopped = SlaCalculator.StoppedStatuses.ToArray();

        int created = 0, recalculated = 0, statusUpdated = 0, escalated = 0, skipped = 0, failed = 0;

        // ---- 1) สร้าง timer ที่ยังไม่มี
        var missing = await db.VulnerabilityInstances.AsNoTracking()
            .Where(i => i.RiskLevel != null
                        && !stopped.Contains(i.Status)
                        && !db.SlaTimers.Any(t => t.InstanceId == i.Id))
            .OrderBy(i => i.FirstDetectedAt)
            .Select(i => i.Id)
            .Take(cap)
            .ToListAsync(cancellationToken);

        foreach (var instanceId in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await sla.CalculateAsync(instanceId, SlaTrigger.Initial, cancellationToken);
                if (result.Succeeded)
                {
                    created++;
                }
                else
                {
                    skipped++;
                    logger.LogDebug("sla.monitor.skipped instanceId={InstanceId} code={Code}", instanceId, result.Code);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                FailedItem(instanceId, "create", ex);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        // ---- 2+3) timer ที่ยังนับอยู่: recalc ถ้า risk เปลี่ยน แล้วประเมิน/escalate
        var active = (await db.SlaTimers.AsNoTracking()
                .Where(t => !stopped.Contains(t.Instance.Status))
                .OrderBy(t => t.ResolutionDue)
                .Select(t => new ActiveTimer(t.InstanceId, t.Instance.RiskLevel, t.Policy.RiskLevel))
                .Take(cap)
                .ToListAsync(cancellationToken));

        foreach (var item in active)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (item.InstanceRisk is { } risk && risk != item.PolicyRisk)
                {
                    var recalc = await sla.CalculateAsync(item.InstanceId, SlaTrigger.RiskLevelChanged, cancellationToken);
                    if (recalc.Succeeded)
                    {
                        recalculated++;
                    }
                    else
                    {
                        skipped++;
                        logger.LogDebug("sla.monitor.skipped instanceId={InstanceId} code={Code}", item.InstanceId, recalc.Code);
                    }

                    db.ChangeTracker.Clear();
                }

                var outcome = await EvaluateAsync(item.InstanceId, cancellationToken);
                if (outcome.StatusUpdated)
                {
                    statusUpdated++;
                }

                if (outcome.Escalated)
                {
                    escalated++;
                }

                if (outcome.NotifyFailed)
                {
                    failed++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                FailedItem(item.InstanceId, "evaluate", ex);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        return new SlaMonitorSummary(created, recalculated, statusUpdated, escalated, skipped, failed);
    }

    private readonly record struct EvaluateOutcome(bool StatusUpdated, bool Escalated, bool NotifyFailed);

    /// <summary>ประเมิน timer เดียว: อัปเดต breach status, escalate ถ้าระดับสูงขึ้น, reset ระดับถ้าสถานะดีขึ้น.</summary>
    private async Task<EvaluateOutcome> EvaluateAsync(Guid instanceId, CancellationToken ct)
    {
        var o = options.Value;
        var timer = await db.SlaTimers.Include(t => t.Instance)
            .FirstOrDefaultAsync(t => t.InstanceId == instanceId, ct);
        if (timer is null || !SlaCalculator.IsTimerRunning(timer.Instance.Status))
        {
            return default;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var startedAt = SlaCalculator.AsUtc(timer.StartedAt);
        var resolutionDue = SlaCalculator.AsUtc(timer.ResolutionDue);

        var breach = SlaCalculator.EvaluateBreach(startedAt, resolutionDue, now, o.NearDueFraction);
        var daysOverdue = SlaCalculator.DaysOverdue(resolutionDue, now);
        var level = SlaEscalationMatrix.Determine(breach, daysOverdue, o.EffectiveOverdueEscalationDays);

        var statusChanged = breach != timer.BreachStatus;
        var escalate = SlaEscalationMatrix.ShouldEscalate(timer.EscalationLevel, level);
        var reset = (int)level < timer.EscalationLevel;
        if (!statusChanged && !escalate && !reset)
        {
            return default;
        }

        var instance = timer.Instance;
        var previousStatus = timer.BreachStatus;
        var previousLevel = timer.EscalationLevel;

        timer.BreachStatus = breach;
        IReadOnlyList<Guid> recipients = [];
        if (escalate)
        {
            recipients = await ResolveRecipientsAsync(instance, level, now, ct);
            timer.EscalationLevel = (int)level;
            timer.LastEscalatedAt = now;
        }
        else if (reset)
        {
            timer.EscalationLevel = (int)level;
        }

        var action = escalate ? ActionEscalated : ActionBreachStatus;
        await unitOfWork.ExecuteAsync(async token =>
        {
            await audit.AppendAsync(
                new AuditEntry(
                    null, action, "VulnerabilityInstance", instanceId.ToString(),
                    JsonSerializer.Serialize(new { breachStatus = previousStatus.ToString(), escalationLevel = previousLevel }, Json),
                    JsonSerializer.Serialize(
                        new
                        {
                            timerId = timer.Id,
                            breachStatus = breach.ToString(),
                            escalationLevel = timer.EscalationLevel,
                            daysOverdue,
                            recipientCount = recipients.Count,
                        },
                        Json),
                    "Success", AuditSource),
                token);
        }, ct);

        logger.LogInformation(
            "sla.monitor.updated instanceId={InstanceId} breachStatus={BreachStatus} escalationLevel={Level} escalated={Escalated}",
            instanceId, breach, timer.EscalationLevel, escalate);

        if (!escalate)
        {
            return new EvaluateOutcome(statusChanged, false, false);
        }

        // ส่งหลัง commit: ระดับนี้ถูก "จอง" แล้ว (ไม่ส่งซ้ำ). ความล้มเหลวของ notifier ต้องไม่ทำให้รอบหยุด (EX-70)
        var notifyFailed = false;
        try
        {
            await notifier.NotifyAsync(
                new SlaEscalation(
                    instanceId,
                    timer.Id,
                    level,
                    instance.RiskLevel?.ToString() ?? "Unknown",
                    SlaEscalationMatrix.IsUrgent(instance.RiskLevel, instance.IsKev),
                    daysOverdue,
                    resolutionDue,
                    recipients),
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            notifyFailed = true;
            logger.LogError(
                "sla.escalation.notify_failed instanceId={InstanceId} level={Level} reason={Reason}",
                instanceId, level, ex.GetType().Name);
        }

        return new EvaluateOutcome(statusChanged, true, notifyFailed);
    }

    /// <summary>ผู้รับตามตาราง: assignee + owner ของ asset (+ Vulnerability Management Officer เมื่อ Overdue); เฉพาะผู้ใช้ที่ active และยังไม่หมดอายุ.</summary>
    private async Task<IReadOnlyList<Guid>> ResolveRecipientsAsync(
        VulnerabilityInstance instance, SlaEscalationLevel level, DateTime now, CancellationToken ct)
    {
        var audience = SlaEscalationMatrix.AudienceFor(level);
        var candidates = new HashSet<Guid>();

        if (audience.HasFlag(SlaAudience.Assignee) && instance.AssignedToUserId is { } assignee)
        {
            candidates.Add(assignee);
        }

        if (audience.HasFlag(SlaAudience.AssetOwners))
        {
            var owners = await db.AssetOwnerships.AsNoTracking()
                .Where(o => o.AssetId == instance.AssetId && o.OwnerUserId != null && o.OwnerType != OwnerType.RemediationTeam)
                .Select(o => o.OwnerUserId)
                .ToListAsync(ct);
            foreach (var owner in owners)
            {
                if (owner is { } id)
                {
                    candidates.Add(id);
                }
            }
        }

        if (audience.HasFlag(SlaAudience.Supervisors))
        {
            var supervisors = await db.UserRoles.AsNoTracking()
                .Where(ur => ur.Role.Code == SeedRoleCodes.VulnMgmtOfficer)
                .Select(ur => ur.UserId)
                .ToListAsync(ct);
            candidates.UnionWith(supervisors);
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var ids = candidates.ToArray();
        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.IsActive && (u.ExpiresAt == null || u.ExpiresAt > now))
            .Select(u => u.Id)
            .ToListAsync(ct);
    }

    private void FailedItem(Guid instanceId, string step, Exception ex)
        => logger.LogWarning(
            "sla.monitor.item_failed instanceId={InstanceId} step={Step} reason={Reason}",
            instanceId, step, ex.GetType().Name);
}
