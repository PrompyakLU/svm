using SvmApi.Application.Dashboard;

namespace SvmApi.Tests.Dashboard;

public sealed class DashboardMathTests
{
    [Fact]
    public void TrendUsesMondayUtcAndFillsEmptyWeeks()
    {
        var now = new DateTime(2026, 10, 18, 23, 59, 0, DateTimeKind.Utc);
        var start = DashboardMath.TrendWindowStart(now, 4);
        Assert.Equal(new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc), start);
        var points = DashboardMath.BuildWeeklyTrend(start, 4,
            new Dictionary<int, (long, long)> { [1] = (5, 2), [4] = (999, 999) },
            new Dictionary<int, long> { [3] = 3 });
        Assert.Equal(4, points.Count);
        Assert.Equal(5, points.Sum(p => p.New));
        Assert.Equal(2, points.Sum(p => p.NewCritical));
        Assert.Equal(3, points.Sum(p => p.Resolved));
        Assert.Null(DashboardMath.WeekIndex(start, 4, start.AddTicks(-1)));
        Assert.Null(DashboardMath.WeekIndex(start, 4, start.AddDays(28)));
        Assert.Equal(3, DashboardMath.WeekIndex(start, 4, now));
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(0, 0)]
    [InlineData(7, 0)]
    [InlineData(8, 1)]
    [InlineData(30, 1)]
    [InlineData(31, 2)]
    [InlineData(90, 2)]
    [InlineData(91, 3)]
    public void OverdueBucketsDoNotOverlap(int days, int? expected)
        => Assert.Equal(expected, DashboardMath.OverdueBucketIndex(days));

    [Fact]
    public void ComplianceIsUnknownWithoutTimersAndRoundsOneDecimal()
    {
        Assert.Null(DashboardMath.CompliancePercent(0, 0));
        Assert.Equal(66.7m, DashboardMath.CompliancePercent(2, 3));
        Assert.Equal(100m, DashboardMath.CompliancePercent(4, 3));
        Assert.Equal(0m, DashboardMath.CompliancePercent(-1, 3));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{broken")]
    [InlineData("{}")]
    public void MissingOrMalformedRiskBreakdownIsSafe(string? value)
        => Assert.Empty(DashboardMath.ParseRiskFactors(value));
}
