using SvmApi.Application.Admin;
using SvmApi.Application.Sla;
using SvmApi.Application.Workflow;
using SvmApi.Domain.Remediation;
using SvmApi.Domain.Risk;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Tests.Admin;

public sealed class AdminRulesTests
{
    [Theory]
    [InlineData("High", true)]
    [InlineData(" high ", true)]
    [InlineData("1", false)]
    [InlineData("High, Low", false)]
    [InlineData("unknown", false)]
    public void EnumParsingAcceptsOnlyNamedValues(string value, bool valid)
        => Assert.Equal(valid, AdminParse.TryEnum<RiskLevel>(value, out _));

    [Fact]
    public void WeightsMustIncludeAllFactorsEvenWhenMissingFactorsWouldHaveZeroWeight()
    {
        var result = RiskConfigAdminRules.ParseWeights(new([new("Cvss", 1m, true)], null));
        Assert.False(result.Validation.IsValid);
        Assert.Contains(result.Validation.Issues, x => x.Code == "MISSING_FACTOR: Kev");
        Assert.Equal(AdminOutcome.Unprocessable, AdminResult<RiskWeightsDto>.Invalid(result.Validation, AdminErrorCodes.InvalidRiskWeight).Outcome);
    }

    [Fact]
    public void InvalidFactorIsAFormatFailure()
    {
        var result = RiskConfigAdminRules.ParseWeights(new([new("0", 1m, true)], null));
        Assert.True(result.Validation.HasFormatIssues);
        Assert.Equal(AdminOutcome.ValidationFailed, AdminResult<RiskWeightsDto>.Invalid(result.Validation, AdminErrorCodes.InvalidRiskWeight).Outcome);
    }

    [Fact]
    public void WeightPrecisionMustMatchStoredPrecision()
    {
        var items = ValidWeights();
        items[0] = items[0] with { Weight = 0.99999m };
        var parsed = RiskConfigAdminRules.ParseWeights(new(items, null));
        Assert.Contains(parsed.Validation.Issues, x => x.Code == "WEIGHT_PRECISION");
    }

    [Fact]
    public void ExtremeWeightValuesAreValidationFailuresInsteadOfArithmeticExceptions()
    {
        var result = RiskConfigAdminRules.ParseWeights(new([new("Cvss", decimal.MaxValue, true), new("Kev", decimal.MaxValue, true)], null));
        Assert.False(result.Validation.IsValid);
        Assert.Contains(result.Validation.Issues, x => x.Code == "WEIGHT_SUM_OVERFLOW");
    }

    [Fact]
    public void TrailingDecimalZerosDoNotCountAsExcessPrecision()
    {
        Assert.Equal(1, AdminParse.DecimalPlaces(0.100000m));
        Assert.True(RiskConfigAdminRules.ParseWeights(new(ValidWeights(), null)).Validation.IsValid);
    }

    [Fact]
    public void ThresholdsMustCoverEveryLevelAndHaveNoGap()
    {
        RiskThresholdItem[] items = [new("Informational", 0m, 20m), new("Low", 20m, 40m),
            new("Medium", 41m, 60m), new("High", 60m, 80m), new("Critical", 80m, 100m)];
        var result = RiskConfigAdminRules.ParseThresholds(new(items, null));
        Assert.Contains(result.Validation.Issues, x => x.Code.StartsWith("RANGE_GAP_OR_OVERLAP:", StringComparison.Ordinal));
    }

    [Fact]
    public void ProtectedReopenTransitionCannotBeDisabled()
    {
        TransitionRule[] before = [new(VulnerabilityStatus.Resolved, VulnerabilityStatus.Reopened, null, false, false)];
        TransitionRule[] after = [before[0] with { IsActive = false }];
        var result = StatusTransitionAdminRules.ValidateChange(before, after, new HashSet<string>());
        Assert.Contains(result.Issues, x => x.Code == "PROTECTED_TRANSITION_REQUIRED: Resolved->Reopened");
    }

    [Fact]
    public void TransitionChangeCannotIntroduceUnknownPermission()
    {
        TransitionRule[] before = [new(VulnerabilityStatus.New, VulnerabilityStatus.UnderReview, null, false, false)];
        TransitionRule[] after = [before[0] with { RequiredPermission = "invented.permission" }];
        Assert.Contains(StatusTransitionAdminRules.ValidateChange(before, after, new HashSet<string>()).Issues,
            x => x.Code == "UNKNOWN_PERMISSION: invented.permission");
    }

    [Fact]
    public void ExistingInvalidWorkflowCanBeRepairedIncrementally()
    {
        TransitionRule[] after = [new(VulnerabilityStatus.Resolved, VulnerabilityStatus.Reopened, null, false, false)];
        Assert.True(StatusTransitionAdminRules.ValidateChange([], after, new HashSet<string>()).IsValid);
    }

    [Fact]
    public void LastActiveSlaPolicyForALevelCannotBeDisabled()
    {
        var policy = Policy();
        var changed = policy with { IsActive = false };
        Assert.Contains(SlaPolicyAdminRules.ValidateChange([policy], [changed], changed).Issues,
            x => x.Code == "NO_ACTIVE_POLICY: High");
    }

    [Fact]
    public void SlaPoliciesCannotDuplicateAnActiveScope()
    {
        var policy = Policy();
        var duplicate = policy with { Id = Guid.NewGuid() };
        Assert.Contains(SlaPolicyAdminRules.ValidateChange([policy], [policy, duplicate], duplicate).Issues,
            x => x.Code.StartsWith("DUPLICATE_SCOPE:", StringComparison.Ordinal));
    }

    [Fact]
    public void SlaHoursMustFollowMilestoneOrder()
    {
        Assert.Contains(SlaPolicyAdminRules.ValidateRow(Policy() with { AcknowledgeHours = 30 }),
            x => x.Code.StartsWith("HOURS_ORDER:", StringComparison.Ordinal));
    }

    [Fact]
    public void MovingSlaPolicyAffectsBothPreviousAndNewLevels()
    {
        var policy = Policy();
        var changed = policy with { RiskLevel = RiskLevel.Critical };
        Assert.True(SlaPolicyAdminRules.AffectsTimers(policy, changed));
        Assert.Equal([RiskLevel.Critical, RiskLevel.High], SlaPolicyAdminRules.AffectedRiskLevels(policy, changed));
        Assert.False(SlaPolicyAdminRules.AffectsTimers(policy, policy));
    }

    [Fact]
    public void AssignmentDuplicatesIgnoreWhitespaceAndCase()
    {
        var first = new AssignmentRuleRow(Guid.NewGuid(), AssignmentMatchType.Technology, " IIS ", null, "ops", 1, true);
        var second = first with { Id = Guid.NewGuid(), MatchValue = "iis" };
        Assert.False(AssignmentRuleAdminRules.ValidateSet([first, second]).IsValid);
        Assert.True(AssignmentRuleAdminRules.ValidateSet([first, second with { IsActive = false }]).IsValid);
    }

    [Fact]
    public void AssignmentRequiresAUserOrTeam()
    {
        var parsed = AssignmentRuleAdminRules.Parse(new("Technology", "IIS", null, " ", 1, true, null), false);
        Assert.Contains(parsed.Validation.Issues, x => x.Code == "ASSIGNEE_REQUIRED");
    }

    [Theory]
    [InlineData("severity")]
    [InlineData("asset-type")]
    [InlineData("asset-criticality")]
    public void EnumMasterDataIsReadOnly(string type)
    {
        Assert.True(MasterDataAdminRules.IsReadOnly(type));
        Assert.NotEmpty(MasterDataAdminRules.EnumValues(type));
    }

    [Fact]
    public void CalendarQueryRangeIsBounded()
    {
        var start = new DateOnly(2026, 1, 1);
        Assert.False(MasterDataAdminRules.ResolveCalendarRange(start, start.AddDays(800), start).Validation.IsValid);
        Assert.False(MasterDataAdminRules.ResolveCalendarRange(start, start.AddDays(-1), start).Validation.IsValid);
    }

    [Fact]
    public void ConfigVersionDetectsChangedContentAndAllowsOmission()
    {
        var version = ConfigVersion.Of("High", 12, true);
        Assert.Equal(ConfigVersion.Length, version.Length);
        Assert.True(ConfigVersion.IsCurrent(version.ToUpperInvariant(), version));
        Assert.True(ConfigVersion.IsCurrent(null, version));
        Assert.False(ConfigVersion.IsCurrent(ConfigVersion.Of("High", 13, true), version));
    }

    [Fact]
    public void ConfigVersionSurvivesDatabaseDecimalScaleAndDateTimeKindChanges()
    {
        var utc = new DateTime(2026, 10, 19, 1, 2, 3, DateTimeKind.Utc).AddTicks(12345);
        var stored = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
        Assert.Equal(ConfigVersion.Of(0.25m, utc), ConfigVersion.Of(0.2500m, stored));
        Assert.NotEqual(ConfigVersion.Of(0.25m, utc), ConfigVersion.Of(0.25m, utc.AddTicks(1)));
    }

    [Fact]
    public void ConfigVersionDistinguishesNullAndEmbeddedSeparators()
    {
        Assert.NotEqual(ConfigVersion.Of("a|b", "c"), ConfigVersion.Of("a", "b|c"));
        Assert.NotEqual(ConfigVersion.Of(new object?[] { null }), ConfigVersion.Of("~"));
    }

    private static List<RiskWeightItem> ValidWeights()
        => Enum.GetValues<RiskFactor>().Select(x => new RiskWeightItem(x.ToString(), x == RiskFactor.Cvss ? 1m : 0m, true)).ToList();

    private static SlaPolicyCandidate Policy()
        => new(Guid.NewGuid(), RiskLevel.High, null, null, false, 1, 2, 24, true);
}
