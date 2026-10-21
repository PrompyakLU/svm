using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SvmApi.Application.Sla;

namespace SvmApi.Infrastructure.Sla;

/// <summary>
/// Background job สแกน SLA (C9 · US-025/026 · FR-39/40): รัน <see cref="SlaMonitor"/> ทุก <see cref="SlaOptions.Interval"/>
/// (ค่าเริ่มต้น 5 นาที; ปิดได้ด้วย <c>Sla:MonitorEnabled=false</c>). ใช้ <see cref="TimeProvider"/> ทั้งการหน่วงและตัวจับเวลา.
/// <list type="bullet">
/// <item>สร้าง DI scope ใหม่ต่อรอบ (DbContext/ICurrentUser เป็น scoped).</item>
/// <item>exception ของรอบใดรอบหนึ่งถูก log (ชนิดเท่านั้น — ไม่เอา message ซึ่งอาจมี connection detail) แล้วรอรอบถัดไป; ไม่ปล่อยให้ host ล้ม.</item>
/// <item>เคารพ cancellation (shutdown) ทั้งระหว่างรอและระหว่างรอบ.</item>
/// </list>
/// หนี้เทคนิค (tasks.md Notes): MVP รองรับอินสแตนซ์เดียว — หลายอินสแตนซ์ต้องมี distributed lock เพื่อไม่ให้ escalate ซ้ำ.
/// </summary>
public sealed class SlaMonitorJob(
    IServiceScopeFactory scopeFactory,
    IOptions<SlaOptions> options,
    TimeProvider clock,
    ILogger<SlaMonitorJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.MonitorEnabled)
        {
            logger.LogInformation("sla.monitor.disabled");
            return;
        }

        logger.LogInformation(
            "sla.monitor.started intervalMinutes={Interval} startupDelaySeconds={Delay}",
            o.Interval.TotalMinutes, o.StartupDelay.TotalSeconds);

        try
        {
            await Task.Delay(o.StartupDelay, clock, stoppingToken);

            using var ticker = new PeriodicTimer(o.Interval, clock);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await ticker.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown ปกติ
        }

        logger.LogInformation("sla.monitor.stopped");
    }

    /// <summary>รันหนึ่งรอบใน scope ใหม่; จับทุก exception ยกเว้นการยกเลิก.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var monitor = scope.ServiceProvider.GetRequiredService<SlaMonitor>();
            var summary = await monitor.RunOnceAsync(cancellationToken);

            logger.LogInformation(
                "sla.monitor.run created={Created} recalculated={Recalculated} statusUpdated={StatusUpdated} escalated={Escalated} skipped={Skipped} failed={Failed}",
                summary.TimersCreated, summary.TimersRecalculated, summary.BreachStatusUpdated,
                summary.Escalated, summary.Skipped, summary.Failed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError("sla.monitor.run_failed reason={Reason}", ex.GetType().Name);
        }
    }
}
