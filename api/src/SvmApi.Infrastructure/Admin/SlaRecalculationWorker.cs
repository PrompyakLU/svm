using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SvmApi.Application.Admin;
using SvmApi.Application.Sla;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Admin;

public sealed class SlaRecalculationWorker(
    SlaRecalculationQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<SlaRecalculationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in queue.ReadAllAsync(stoppingToken))
            {
                try { await ProcessAsync(request, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { logger.LogError("admin.sla.recalculation.failed reason={Reason}", ex.GetType().Name); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task ProcessAsync(SlaRecalculationRequest request, CancellationToken cancellationToken)
    {
        List<Guid> ids;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SvmDbContext>();
            var stopped = SlaCalculator.StoppedStatuses.ToArray();
            var levels = request.RiskLevels.ToArray();
            ids = await db.VulnerabilityInstances.AsNoTracking()
                .Where(x => x.RiskLevel.HasValue && levels.Contains(x.RiskLevel.Value) && !stopped.Contains(x.Status))
                .OrderBy(x => x.Id).Select(x => x.Id).Take(ISlaRecalculationScheduler.MaxInstancesPerRequest + 1)
                .ToListAsync(cancellationToken);
        }
        if (ids.Count > ISlaRecalculationScheduler.MaxInstancesPerRequest)
        {
            ids.RemoveAt(ids.Count - 1);
            logger.LogWarning("admin.sla.recalculation.capped limit={Limit}", ISlaRecalculationScheduler.MaxInstancesPerRequest);
        }
        var succeeded = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A fresh scope per instance isolates failed EF changes and audit retries.
            await using var scope = scopeFactory.CreateAsyncScope();
            var sla = scope.ServiceProvider.GetRequiredService<ISlaService>();
            if (await sla.TryCalculateAsync(id, SlaTrigger.PolicyChanged, cancellationToken)) succeeded++;
            else logger.LogWarning("admin.sla.recalculation.instance_failed instanceId={InstanceId}", id);
        }
        logger.LogInformation("admin.sla.recalculation.completed attempted={Attempted} succeeded={Succeeded}", ids.Count, succeeded);
    }
}
