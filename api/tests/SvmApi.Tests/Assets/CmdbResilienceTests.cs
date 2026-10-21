using Microsoft.Extensions.Logging.Abstractions;
using SvmApi.Application.Assets;
using SvmApi.Infrastructure.Integration;

namespace SvmApi.Tests.Assets;

/// <summary>ทดสอบ timeout/retry ของการเรียก CMDB (US-005 · EX-30 · integration.md) — ไม่มี IO จริง.</summary>
public class CmdbResilienceTests
{
    private static readonly CmdbRetrySettings Fast = new(TimeSpan.FromSeconds(5), 3, TimeSpan.Zero);

    [Fact]
    public void Default_MatchesIntegrationSpec()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), CmdbRetrySettings.Default.AttemptTimeout);
        Assert.Equal(3, CmdbRetrySettings.Default.MaxRetries);
    }

    [Fact]
    public void GetRetryDelay_IsExponential()
    {
        var s = new CmdbRetrySettings(TimeSpan.FromSeconds(3), 3, TimeSpan.FromMilliseconds(200));

        Assert.Equal(TimeSpan.FromMilliseconds(200), CmdbResilience.GetRetryDelay(s, 1));
        Assert.Equal(TimeSpan.FromMilliseconds(400), CmdbResilience.GetRetryDelay(s, 2));
        Assert.Equal(TimeSpan.FromMilliseconds(800), CmdbResilience.GetRetryDelay(s, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => CmdbResilience.GetRetryDelay(s, 0));
    }

    [Fact]
    public async Task ExecuteAsync_SucceedsAfterTransientFailures()
    {
        var calls = 0;

        var result = await CmdbResilience.ExecuteAsync(
            _ =>
            {
                calls++;
                return calls < 3 ? throw new CmdbUnavailableException("down") : Task.FromResult(42);
            },
            Fast, TimeProvider.System, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ExecuteAsync_ExhaustsRetries_ThrowsUnavailable()
    {
        var calls = 0;

        var ex = await Assert.ThrowsAsync<CmdbUnavailableException>(() => CmdbResilience.ExecuteAsync<int>(
            _ =>
            {
                calls++;
                throw new InvalidOperationException("internal detail");
            },
            Fast, TimeProvider.System, NullLogger.Instance, CancellationToken.None));

        Assert.Equal(4, calls); // 1 ครั้งแรก + retry 3
        Assert.DoesNotContain("internal detail", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_AttemptTimeout_RetriedThenUnavailable()
    {
        var settings = new CmdbRetrySettings(TimeSpan.FromMilliseconds(30), 1, TimeSpan.Zero);
        var calls = 0;

        var ex = await Assert.ThrowsAsync<CmdbUnavailableException>(() => CmdbResilience.ExecuteAsync<int>(
            async ct =>
            {
                calls++;
                await Task.Delay(Timeout.Infinite, ct);
                return 0;
            },
            settings, TimeProvider.System, NullLogger.Instance, CancellationToken.None));

        Assert.Equal(2, calls);
        Assert.IsType<TimeoutException>(ex.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancellation_NotRetried()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CmdbResilience.ExecuteAsync<int>(
            ct =>
            {
                calls++;
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(0);
            },
            Fast, TimeProvider.System, NullLogger.Instance, cts.Token));

        Assert.Equal(1, calls);
    }
}
