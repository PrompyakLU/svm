using SvmApi.Application.Assets;
using SvmApi.Domain.Assets;
using SvmApi.Domain.Remediation;

namespace SvmApi.Tests.Assets;

/// <summary>ทดสอบ fallback owner จาก assignment rule สำหรับ asset ใหม่ (US-006 · FR-03) — pure.</summary>
public class AssetOwnerRulesTests
{
    private static AssignmentRule Rule(
        AssignmentMatchType type, string value, string? team, int priority, bool active = true, Guid? userId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            MatchType = type,
            MatchValue = value,
            AssignToTeam = team,
            AssignToUserId = userId,
            Priority = priority,
            IsActive = active,
        };

    [Fact]
    public void Match_LowestPriorityWins()
    {
        var byType = Rule(AssignmentMatchType.AssetType, "Database", "DBA Team", 10);
        var byBu = Rule(AssignmentMatchType.BusinessUnit, "Corporate Finance", "Finance Team", 20);

        var m = AssetOwnerRules.Match([byBu, byType], "Corporate Finance", AssetType.Database);

        Assert.NotNull(m);
        Assert.Equal(byType.Id, m!.RuleId);
        Assert.Equal("DBA Team", m.TeamName);
    }

    [Fact]
    public void Match_BusinessUnit_CaseInsensitiveAndTrimmed()
    {
        var rule = Rule(AssignmentMatchType.BusinessUnit, " digital channels ", "Dev Team", 20);

        var m = AssetOwnerRules.Match([rule], "Digital Channels", AssetType.Application);

        Assert.Equal("Dev Team", m!.TeamName);
    }

    [Fact]
    public void Match_SkipsInactive_Teamless_AndTechnologyRules()
    {
        var rules = new[]
        {
            Rule(AssignmentMatchType.AssetType, "Server", "Inactive Team", 1, active: false),
            Rule(AssignmentMatchType.AssetType, "Server", null, 2, userId: Guid.NewGuid()),
            Rule(AssignmentMatchType.Technology, "Server", "Tech Team", 3),
            Rule(AssignmentMatchType.AssetType, "server", "Infra Team", 4),
        };

        var m = AssetOwnerRules.Match(rules, null, AssetType.Server);

        Assert.Equal("Infra Team", m!.TeamName);
    }

    [Fact]
    public void Match_NoBusinessUnit_DoesNotMatchBuRule()
        => Assert.Null(AssetOwnerRules.Match(
            [Rule(AssignmentMatchType.BusinessUnit, "IT Infrastructure", "Infra Team", 1)], "  ", AssetType.Server));

    [Fact]
    public void Match_NoRules_Null()
        => Assert.Null(AssetOwnerRules.Match([], "IT Infrastructure", AssetType.Server));

    [Fact]
    public void Match_CarriesRuleUser_EmptyGuidBecomesNull()
    {
        var uid = Guid.NewGuid();
        Assert.Equal(uid, AssetOwnerRules.Match(
            [Rule(AssignmentMatchType.AssetType, "Container", "Platform", 1, userId: uid)], null, AssetType.Container)!.UserId);
        Assert.Null(AssetOwnerRules.Match(
            [Rule(AssignmentMatchType.AssetType, "Container", "Platform", 1, userId: Guid.Empty)], null, AssetType.Container)!.UserId);
    }

    [Fact]
    public void HasRemediationTeam_DetectsTeamOwner()
    {
        Assert.True(AssetOwnerRules.HasRemediationTeam([OwnerType.AssetOwner, OwnerType.RemediationTeam]));
        Assert.False(AssetOwnerRules.HasRemediationTeam([OwnerType.AssetOwner, OwnerType.SystemOwner]));
        Assert.False(AssetOwnerRules.HasRemediationTeam([]));
    }
}
