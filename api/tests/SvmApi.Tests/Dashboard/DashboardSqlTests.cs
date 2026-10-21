using Microsoft.EntityFrameworkCore;
using SvmApi.Application.Auth;
using SvmApi.Application.Dashboard;
using SvmApi.Infrastructure.Auth.DataScope;
using SvmApi.Infrastructure.Dashboard;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Tests.Dashboard;

/// <summary>Compile real SQL Server queries without connecting to a database.</summary>
public sealed class DashboardSqlTests
{
    [Theory]
    [InlineData("OnTrack")]
    [InlineData("NearDue")]
    [InlineData("Breached")]
    [InlineData("Met")]
    [InlineData("OnTrack,NearDue,Breached,Met")]
    public void ScopedAdvancedFiltersAndSlaCompileToSql(string slaStatus)
    {
        using var db = CreateContext();
        var parsed = InstanceQueryRules.Validate(new InstanceListQuery(
            Search: "CVE-2026", Status: "New,Reopened", RiskLevel: "Critical,High",
            SlaStatus: slaStatus, IsKev: "true", DetectedFrom: "2026-01-01", DetectedTo: "2026-10-19",
            AgeDaysMin: "1", AgeDaysMax: "90", Sort: "riskScore:desc,assetCode"));
        Assert.True(parsed.IsValid);
        var sql = db.VulnerabilityInstances.AsNoTracking().ApplyDataScope(new ScopedUser())
            .ApplyFilters(parsed.Value!, Guid.NewGuid(), new DateTime(2026, 10, 19), 0.2)
            .ApplySort(parsed.Value!.Sort).Skip(25).Take(25).ToQueryString();
        Assert.Contains("Finance", sql);
        Assert.Contains("WHERE", sql);
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("OFFSET", sql);
        Assert.Contains("FETCH NEXT", sql);
    }

    [Theory]
    [InlineData("riskScore")]
    [InlineData("firstDetectedAt")]
    [InlineData("lastDetectedAt")]
    [InlineData("status")]
    [InlineData("title")]
    [InlineData("slaDueDate")]
    [InlineData("assetCode")]
    [InlineData("businessUnit")]
    public void EachAllowedSortTranslatesWithStableTieBreaker(string field)
    {
        using var db = CreateContext();
        var parsed = InstanceQueryRules.Validate(new InstanceListQuery(Sort: field + ":desc"));
        Assert.True(parsed.IsValid);
        var sql = db.VulnerabilityInstances.ApplyDataScope(new ScopedUser())
            .ApplySort(parsed.Value!.Sort).Take(5).ToQueryString();
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("[id]", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static SvmDbContext CreateContext()
        => new(new DbContextOptionsBuilder<SvmDbContext>()
            .UseSqlServer("Server=localhost;Database=DashboardTranslationOnly;Integrated Security=true;TrustServerCertificate=true")
            .Options);

    private sealed class ScopedUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public Guid? SessionId => null;
        public string? Username => "translation-test";
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => ["instance.read"];
        public IReadOnlyCollection<AuthScope> Scopes => [new("BusinessUnit", "Finance")];
        public bool HasPermission(string permission) => PermissionMatcher.HasPermission(Permissions, permission);
    }
}
