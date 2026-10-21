using SvmApi.Application.Admin;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Infrastructure.Admin;

namespace SvmApi.Tests.Admin;

public sealed class SlaRecalculationQueueTests
{
    [Fact]
    public async Task QueuedLevelsAreCopiedAndDeduplicated()
    {
        var queue = new SlaRecalculationQueue();
        var levels = new List<RiskLevel> { RiskLevel.High, RiskLevel.High };
        Assert.True(queue.Enqueue(new(levels, "SLA_POLICY_CHANGED")));
        levels.Clear();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = queue.ReadAllAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal([RiskLevel.High], reader.Current.RiskLevels);
    }

    [Fact]
    public void QueueRejectsInvalidLevelsAndReportsCapacityInsteadOfDroppingSilently()
    {
        var queue = new SlaRecalculationQueue();
        Assert.False(queue.Enqueue(new([], "CONFIG_CHANGED")));
        Assert.False(queue.Enqueue(new([(RiskLevel)999], "CONFIG_CHANGED")));
        for (var i = 0; i < 256; i++) Assert.True(queue.Enqueue(new([RiskLevel.High], "CONFIG_CHANGED")));
        Assert.False(queue.Enqueue(new([RiskLevel.High], "CONFIG_CHANGED")));
    }
}
