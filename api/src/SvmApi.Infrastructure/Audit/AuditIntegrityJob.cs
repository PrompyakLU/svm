using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SvmApi.Application.Audit;

namespace SvmApi.Infrastructure.Audit;

/// <summary>
/// ตรวจ hash chain ของ audit เป็นระยะ (EX-100, NFR-21). Config: <c>Audit:IntegrityCheckMinutes</c>
/// (default 60, 0 หรือค่าติดลบ = ปิด). ใช้ DI scope ต่อรอบ; error ไม่ทำให้ host ล้ม.
/// </summary>
public sealed class AuditIntegrityJob(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AuditIntegrityJob> logger) : BackgroundService
{
    /// <summary>คีย์ config: ระยะห่างระหว่างรอบตรวจ (นาที).</summary>
    public const string ConfigKey = "Audit:IntegrityCheckMinutes";

    public const int DefaultMinutes = 60;

    // 30 วัน: กัน PeriodicTimer period เกินช่วงที่รองรับ
    private const int MaxMinutes = 43_200;

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = ReadMinutes();
        if (minutes <= 0)
        {
            logger.LogInformation("audit.integrity_job.disabled");
            return;
        }

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Min(minutes, MaxMinutes)));
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // host กำลังหยุด
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            var result = await audit.VerifyChainAsync(cancellationToken);

            if (result.IsValid)
            {
                logger.LogInformation("audit.integrity_job.ok checked={CheckedCount}", result.CheckedCount);
            }
            else
            {
                logger.LogError(
                    "audit.integrity_job.chain_broken firstBrokenId={FirstBrokenId} checked={CheckedCount}",
                    result.FirstBrokenId, result.CheckedCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // ห้ามให้ host ล้ม; รอบถัดไปจะลองใหม่
            logger.LogError(ex, "audit.integrity_job.failed");
        }
    }

    private int ReadMinutes()
    {
        var raw = configuration[ConfigKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultMinutes;
        }

        if (int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var minutes))
        {
            return minutes;
        }

        logger.LogWarning("audit.integrity_job.invalid_config key={ConfigKey}; using default", ConfigKey);
        return DefaultMinutes;
    }
}
