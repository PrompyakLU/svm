using SvmApi.Application.Remediation;
using SvmApi.Domain.Remediation;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Tests.Remediation;

/// <summary>
/// AssignmentRuleMatcher / AssigneeScopeMatcher / AssignmentPolicy (pure · C8 · US-020 · FR-24..26 · BR-06/07 · EX-59/60 · task 5.2).
/// </summary>
public class AssignmentLogicTests
{
    private static readonly Guid UserA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static AssignmentRuleFacts Rule(
        AssignmentMatchType type, string value, string? team = "Team", Guid? user = null, int priority = 10, bool active = true)
        => new(type, value, user, team, priority, active);

    private static AssetMatchFacts Asset(string? bu = "IT Infrastructure", string? type = "Server", string? tech = null)
        => new(bu, type, tech);

    // ------------------------------------------------------------ rule matching

    [Fact]
    public void Matches_business_unit_case_insensitively()
    {
        var rules = new[] { Rule(AssignmentMatchType.BusinessUnit, "it infrastructure", "Infra Ops") };

        var match = AssignmentRuleMatcher.Match(rules, Asset());

        Assert.NotNull(match);
        Assert.Equal("Infra Ops", match!.AssignToTeam);
    }

    [Fact]
    public void Lower_priority_number_wins()
    {
        var rules = new[]
        {
            Rule(AssignmentMatchType.BusinessUnit, "IT Infrastructure", "BU team", priority: 20),
            Rule(AssignmentMatchType.AssetType, "Server", "Server team", priority: 10),
        };

        Assert.Equal("Server team", AssignmentRuleMatcher.Match(rules, Asset())!.AssignToTeam);
    }

    [Fact]
    public void Same_priority_prefers_more_specific_match_type()
    {
        var rules = new[]
        {
            Rule(AssignmentMatchType.BusinessUnit, "IT Infrastructure", "BU team"),
            Rule(AssignmentMatchType.AssetType, "Server", "Type team"),
            Rule(AssignmentMatchType.Technology, "openssl", "Tech team"),
        };

        var match = AssignmentRuleMatcher.Match(rules, Asset(tech: "OpenSSL 3.0.1"));

        Assert.Equal("Tech team", match!.AssignToTeam);
    }

    [Fact]
    public void Result_is_deterministic_regardless_of_input_order()
    {
        var a = Rule(AssignmentMatchType.AssetType, "Server", "A");
        var b = Rule(AssignmentMatchType.AssetType, "server", "B");

        var first = AssignmentRuleMatcher.Match([a, b], Asset());
        var second = AssignmentRuleMatcher.Match([b, a], Asset());

        Assert.Equal(first!.AssignToTeam, second!.AssignToTeam);
    }

    [Fact]
    public void No_rule_matches_returns_null()
    {
        var rules = new[] { Rule(AssignmentMatchType.BusinessUnit, "HR"), Rule(AssignmentMatchType.AssetType, "Database") };

        Assert.Null(AssignmentRuleMatcher.Match(rules, Asset()));
    }

    [Fact]
    public void Inactive_rules_are_ignored()
    {
        var rules = new[] { Rule(AssignmentMatchType.AssetType, "Server", active: false) };

        Assert.Null(AssignmentRuleMatcher.Match(rules, Asset()));
    }

    [Fact]
    public void Rule_without_any_assignee_is_skipped_and_next_rule_used()
    {
        var rules = new[]
        {
            Rule(AssignmentMatchType.AssetType, "Server", team: null, user: null, priority: 1),
            Rule(AssignmentMatchType.BusinessUnit, "IT Infrastructure", "Fallback", priority: 50),
        };

        Assert.Equal("Fallback", AssignmentRuleMatcher.Match(rules, Asset())!.AssignToTeam);
    }

    [Fact]
    public void Rule_targeting_a_user_is_supported()
    {
        var rules = new[] { Rule(AssignmentMatchType.AssetType, "Server", team: null, user: UserA) };

        Assert.Equal(UserA, AssignmentRuleMatcher.Match(rules, Asset())!.AssignToUserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_match_value_never_matches(string? value)
    {
        var rules = new[] { Rule(AssignmentMatchType.BusinessUnit, value!) };

        Assert.Null(AssignmentRuleMatcher.Match(rules, Asset(bu: "")));
    }

    [Fact]
    public void Technology_needs_component_and_min_length()
    {
        Assert.Null(AssignmentRuleMatcher.Match([Rule(AssignmentMatchType.Technology, "openssl")], Asset(tech: null)));
        Assert.Null(AssignmentRuleMatcher.Match([Rule(AssignmentMatchType.Technology, "o")], Asset(tech: "openssl")));
        Assert.NotNull(AssignmentRuleMatcher.Match([Rule(AssignmentMatchType.Technology, "OpenSSL")], Asset(tech: "openssl-3.0")));
    }

    // ------------------------------------------------------------ assignee scope

    [Fact]
    public void Organization_wildcard_grants_scope()
        => Assert.True(AssigneeScopeMatcher.HasScope([new("Organization", "*")], "IT", "A-1", false));

    [Fact]
    public void Organization_with_other_value_does_not_grant_scope()
        => Assert.False(AssigneeScopeMatcher.HasScope([new("Organization", "ACME")], "IT", "A-1", false));

    [Fact]
    public void Business_unit_scope_matches_asset_business_unit_only()
    {
        Assert.True(AssigneeScopeMatcher.HasScope([new("BusinessUnit", "it")], "IT", "A-1", false));
        Assert.False(AssigneeScopeMatcher.HasScope([new("BusinessUnit", "HR")], "IT", "A-1", false));
        Assert.False(AssigneeScopeMatcher.HasScope([new("BusinessUnit", "IT")], null, "A-1", false));
    }

    [Fact]
    public void Asset_scope_matches_asset_code()
    {
        Assert.True(AssigneeScopeMatcher.HasScope([new("Asset", "a-1")], "IT", "A-1", false));
        Assert.False(AssigneeScopeMatcher.HasScope([new("Asset", "A-2")], "IT", "A-1", false));
    }

    [Fact]
    public void Responsibility_scope_requires_asset_ownership()
    {
        Assert.True(AssigneeScopeMatcher.HasScope([new("Responsibility", "self")], "IT", "A-1", isAssetOwner: true));
        Assert.False(AssigneeScopeMatcher.HasScope([new("Responsibility", "self")], "IT", "A-1", isAssetOwner: false));
    }

    [Fact]
    public void No_grants_means_no_scope_fail_closed()
        => Assert.False(AssigneeScopeMatcher.HasScope([], "IT", "A-1", true));

    [Fact]
    public void Blank_grant_value_is_ignored()
        => Assert.False(AssigneeScopeMatcher.HasScope([new("BusinessUnit", " ")], "", "A-1", false));

    // ------------------------------------------------------------ policy

    [Theory]
    [InlineData(VulnerabilityStatus.New, true)]
    [InlineData(VulnerabilityStatus.Confirmed, true)]
    [InlineData(VulnerabilityStatus.Assigned, true)]
    [InlineData(VulnerabilityStatus.InProgress, true)]
    [InlineData(VulnerabilityStatus.Reopened, true)]
    [InlineData(VulnerabilityStatus.PendingVerification, false)]
    [InlineData(VulnerabilityStatus.Resolved, false)]
    [InlineData(VulnerabilityStatus.Closed, false)]
    [InlineData(VulnerabilityStatus.FalsePositive, false)]
    [InlineData(VulnerabilityStatus.RiskAccepted, false)]
    [InlineData(VulnerabilityStatus.ExceptionApproved, false)]
    [InlineData(VulnerabilityStatus.Archived, false)]
    public void Assignable_statuses(VulnerabilityStatus status, bool expected)
        => Assert.Equal(expected, AssignmentPolicy.CanAssignIn(status));

    [Fact]
    public void Explicit_target_rejects_user_and_team_together()
        => Assert.Equal(RemediationErrorCodes.AssigneeConflict, AssignmentPolicy.ValidateExplicitTarget(UserA, "Team"));

    [Fact]
    public void Explicit_target_accepts_user_or_team_alone()
    {
        Assert.Null(AssignmentPolicy.ValidateExplicitTarget(UserA, null));
        Assert.Null(AssignmentPolicy.ValidateExplicitTarget(null, "Team"));
    }

    [Fact]
    public void Explicit_target_rejects_too_long_team()
        => Assert.Equal(
            RemediationErrorCodes.TeamTooLong,
            AssignmentPolicy.ValidateExplicitTarget(null, new string('t', AssignmentPolicy.MaxTeamLength + 1)));

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, "  ", true)]
    [InlineData("empty", null, true)]
    [InlineData(null, "Team", false)]
    public void Automatic_when_no_target_given(string? user, string? team, bool expected)
    {
        Guid? id = user == "empty" ? Guid.Empty : null;
        Assert.Equal(expected, AssignmentPolicy.IsAutomatic(id, team));
    }

    [Fact]
    public void Automatic_is_false_when_user_given()
        => Assert.False(AssignmentPolicy.IsAutomatic(UserA, null));

    [Theory]
    [InlineData(false, true, true, true)]   // manual reassign → reason required
    [InlineData(false, false, true, false)] // first assignment → not required
    [InlineData(false, true, false, false)] // nothing changed → not required
    [InlineData(true, true, true, false)]   // automatic → not required
    public void Reason_required_only_when_manually_changing_existing_owner(bool automatic, bool hadPrevious, bool changed, bool expected)
        => Assert.Equal(expected, AssignmentPolicy.ReasonRequired(automatic, hadPrevious, changed));

    [Fact]
    public void Reason_is_cleaned_and_blank_becomes_null()
    {
        Assert.Null(AssignmentPolicy.NormalizeReason("  \u0000 \u200B "));
        Assert.Equal("moveteam", AssignmentPolicy.NormalizeReason("  move\u0007team "));
    }

    [Fact]
    public void Default_due_date_prefers_sla_resolution_due()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var sla = now.AddDays(3);

        Assert.Equal(sla, AssignmentPolicy.DefaultDueDate(now, RiskLevel.Low, sla));
    }

    [Theory]
    [InlineData(RiskLevel.Critical, 7)]
    [InlineData(RiskLevel.High, 30)]
    [InlineData(RiskLevel.Medium, 90)]
    [InlineData(RiskLevel.Low, 180)]
    [InlineData(null, 180)]
    public void Default_due_date_falls_back_by_risk_level(RiskLevel? level, int days)
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(now.AddDays(days), AssignmentPolicy.DefaultDueDate(now, level, null));
    }
}
