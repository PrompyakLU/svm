using Microsoft.Extensions.Logging;
using SvmApi.Application.Assets;

namespace SvmApi.Infrastructure.Integration;

/// <summary>ค่า resilience ของการเรียก CMDB (integration.md: timeout 3s, retry 3× exponential).</summary>
public sealed record CmdbRetrySettings(TimeSpan AttemptTimeout, int MaxRetries, TimeSpan BaseDelay)
{
    public static CmdbRetrySettings Default { get; } =
        new(TimeSpan.FromSeconds(3), 3, TimeSpan.FromMilliseconds(200));
}

/// <summary>
/// ห่อการเรียก <see cref="ICmdbAdapter"/> ด้วย timeout ต่อครั้ง + retry แบบ exponential backoff (NFR-24, integration.md).
/// retry ทุก exception ยกเว้นการยกเลิกจากผู้เรียก; timeout ต่อครั้งถือเป็น <see cref="TimeoutException"/> (retry ได้).
/// ครบจำนวนแล้วยังล้มเหลว → throw <see cref="CmdbUnavailableException"/> (ข้อความทั่วไป) ให้ผู้เรียกบันทึก integration error (EX-30).
/// log เฉพาะเลขรอบ + ชนิด exception (ไม่ log message/secret).
/// </summary>
public static class CmdbResilience
{
    /// <summary>delay ก่อน retry ครั้งที่ <paramref name="retryNumber"/> (เริ่มที่ 1): base × 2^(n-1).</summary>
    public static TimeSpan GetRetryDelay(CmdbRetrySettings settings, int retryNumber)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryNumber, 1);

        var exponent = Math.Min(retryNumber - 1, 16);
        return TimeSpan.FromTicks(settings.BaseDelay.Ticks * (1L << exponent));
    }

    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CmdbRetrySettings settings,
        TimeProvider clock,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        var maxRetries = Math.Max(0, settings.MaxRetries);
        Exception? last = null;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attempt > 0)
            {
                await Task.Delay(GetRetryDelay(settings, attempt), clock, cancellationToken);
            }

            using var timeout = new CancellationTokenSource(settings.AttemptTimeout, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            try
            {
                return await operation(linked.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                last = new TimeoutException("The CMDB call timed out.");
            }
            catch (Exception ex)
            {
                last = ex;
            }

            logger.LogWarning(
                "cmdb.call.failed attempt={Attempt} maxAttempts={MaxAttempts} reason={Reason}",
                attempt + 1, maxRetries + 1, last.GetType().Name);
        }

        throw new CmdbUnavailableException("The CMDB could not be reached.", last!);
    }
}
