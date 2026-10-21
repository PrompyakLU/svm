using SvmApi.Application.Dashboard;
using SvmApi.Domain.Sla;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Tests.Dashboard;

public sealed class InstanceQueryRulesTests
{
    [Fact]
    public void InvalidFiltersAreRejectedTogether()
    {
        var result = InstanceQueryRules.Validate(new InstanceListQuery(
            Status: "Unknown", IsKev: "perhaps", AssetId: "no-guid", Sort: "drop table", Page: 0, PageSize: 101));
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 6);
    }

    [Fact]
    public void DefaultQueryIsValidAndBounded()
    {
        var result = InstanceQueryRules.Validate(new InstanceListQuery());
        Assert.True(result.IsValid);
        Assert.Equal(25, result.Value!.PageSize);
        Assert.Equal(0, InstanceQueryRules.ComputeSkip(1, 25));
    }

    [Fact]
    public void ReopenedRunsSlaAndClosedPreservesStoredBreach()
    {
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var reopened = InstanceQueryRules.BuildSla("Test", VulnerabilityStatus.Reopened,
            now.AddDays(-2), now.AddHours(-1), SlaBreachStatus.OnTrack, now, 0.2);
        Assert.True(reopened.Running);
        Assert.Equal("Breached", reopened.BreachStatus);
        var closed = InstanceQueryRules.BuildSla("Test", VulnerabilityStatus.Closed,
            now.AddDays(-2), now.AddHours(-1), SlaBreachStatus.OnTrack, now, 0.2);
        Assert.False(closed.Running);
        Assert.Equal("Met", closed.BreachStatus);
    }
}
