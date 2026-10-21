using SvmApi.Application.Risk;
using SvmApi.Domain.Assets;
using SvmApi.Domain.Risk;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Tests.Risk;

/// <summary>
/// RiskScoringEngine (pure · C6 · US-015/016/017 · EX-50/51/54 · task 4.4/7.1):
/// invariant 0–100, ผลรวม weight, ขอบเขต threshold, weight ผิดถูกปฏิเสธ, manual override ต้องมีเหตุผล.
/// </summary>
public class RiskScoringEngineTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string ValidRowVersion = Convert.ToBase64String(new byte[8]);

    /// <summary>สำเนาค่า seed (SeedData.Config) — ผลรวม 1.0000.</summary>
    private static readonly RiskWeightConfig[] SeedWeights =
    [
        new(RiskFactor.Cvss, 0.2500m),
        new(RiskFactor.AssetCriticality, 0.1500m),
        new(RiskFactor.BusinessImpact, 0.1000m),
        new(RiskFactor.InternetExposure, 0.1000m),
        new(RiskFactor.ExploitAvailability, 0.1000m),
        new(RiskFactor.Kev, 0.1000m),
        new(RiskFactor.ThreatIntel, 0.0500m),
        new(RiskFactor.VulnAge, 0.0500m),
        new(RiskFactor.ExistingControl, 0.1000m),
    ];

    private static readonly RiskThresholdConfig[] SeedThresholds =
    [
        new(RiskLevel.Informational, 0m, 20m),
        new(RiskLevel.Low, 20m, 40m),
        new(RiskLevel.Medium, 40m, 60m),
        new(RiskLevel.High, 60m, 80m),
        new(RiskLevel.Critical, 80m, 100m),
    ];

    private static RiskInputs Inputs(
        decimal? cvss = 7.5m,
        string? severity = "High",
        AssetCriticality? criticality = AssetCriticality.High,
        AssetEnvironment env = AssetEnvironment.Prod,
        bool internet = false,
        bool kev = false,
        int ageDays = 0)
        => new(cvss, severity, criticality, env, internet, kev, Now.AddDays(-ageDays));

    private static Dictionary<RiskFactor, (decimal Value, string Source)> AllFactors(decimal value)
        => Enum.GetValues<RiskFactor>().ToDictionary(f => f, _ => (value, "test"));

    // ------------------------------------------------------------ invariant 0–100

    [Fact]
    public void Score_IsAlwaysWithin0And100_ForRandomInputsAndValidWeights()
    {
        var rng = new Random(4242);
        var criticalities = new AssetCriticality?[] { AssetCriticality.Critical, AssetCriticality.High, AssetCriticality.Medium, AssetCriticality.Low };
        var severities = new[] { "Critical", "High", "Medium", "Low", "Informational" };

        for (var i = 0; i < 2000; i++)
        {
            var weights = RandomWeights(rng);
            var inputs = new RiskInputs(
                rng.Next(3) == 0 ? null : Math.Round((decimal)(rng.NextDouble() * 10), 1),
                severities[rng.Next(severities.Length)],
                criticalities[rng.Next(criticalities.Length)],
                (AssetEnvironment)rng.Next(3),
                rng.Next(2) == 0,
                rng.Next(2) == 0,
                Now.AddDays(-rng.Next(0, 5000)));

            var calc = RiskScoringEngine.Calculate(inputs, weights, SeedThresholds, Now);

            Assert.True(calc.Succeeded, string.Join(",", calc.Issues));
            Assert.InRange(calc.Score, 0m, 100m);
            Assert.Equal(calc.Score, decimal.Round(calc.Score, 2));
            Assert.Equal(RiskScoringEngine.Classify(calc.Score, SeedThresholds), calc.Level);
        }
    }

    [Theory]
    [InlineData(-5.0)]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    [InlineData(1000.0)]
    public void Score_NormalizedValuesOutsideRange_AreClampedSoScoreStaysInRange(double value)
    {
        var calc = RiskScoringEngine.Calculate(AllFactors((decimal)value), SeedWeights, SeedThresholds);

        Assert.True(calc.Succeeded);
        Assert.InRange(calc.Score, 0m, 100m);
        Assert.Equal(value < 0 ? 0m : 100m, calc.Score);
    }

    [Fact]
    public void Score_AllFactorsMax_Is100Critical_AndAllZero_Is0Informational()
    {
        var max = RiskScoringEngine.Calculate(AllFactors(1m), SeedWeights, SeedThresholds);
        var min = RiskScoringEngine.Calculate(AllFactors(0m), SeedWeights, SeedThresholds);

        Assert.Equal(100m, max.Score);
        Assert.Equal(RiskLevel.Critical, max.Level);
        Assert.Equal(0m, min.Score);
        Assert.Equal(RiskLevel.Informational, min.Level);
    }

    [Fact]
    public void Calculate_KnownExample_ProducesExpectedScoreAndBreakdown()
    {
        // cvss 9.8 → 24.5 · crit Critical 15 · Prod 10 · internet 10 · exploit(KEV) 10 · KEV 10 · TI 0 · age 0 · control 10 = 89.5
        var calc = RiskScoringEngine.Calculate(
            Inputs(cvss: 9.8m, criticality: AssetCriticality.Critical, internet: true, kev: true), SeedWeights, SeedThresholds, Now);

        Assert.True(calc.Succeeded);
        Assert.Equal(89.5m, calc.Score);
        Assert.Equal(RiskLevel.Critical, calc.Level);
        Assert.NotNull(calc.Breakdown);
        Assert.Equal(9, calc.Breakdown!.Factors.Count);
        Assert.Equal(calc.Breakdown.RawScore, calc.Breakdown.Factors.Sum(f => f.Contribution));
        Assert.Equal("cvss", calc.Breakdown.Factors.Single(f => f.Factor == nameof(RiskFactor.Cvss)).Source);

        var json = RiskScoringEngine.SerializeBreakdown(calc.Breakdown);
        Assert.Contains("\"factors\"", json);
        Assert.Contains("\"model\":\"weighted-sum-v1\"", json);
    }

    [Fact]
    public void Calculate_UsesRuntimeWeights_NotHardcoded()
    {
        RiskWeightConfig[] cvssOnly = [new(RiskFactor.Cvss, 1m)];

        var calc = RiskScoringEngine.Calculate(Inputs(cvss: 6.4m), cvssOnly, SeedThresholds, Now);

        Assert.True(calc.Succeeded);
        Assert.Equal(64m, calc.Score);
        Assert.Equal(RiskLevel.High, calc.Level);
        Assert.Single(calc.Breakdown!.Factors);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(90, 0.5)]
    [InlineData(180, 1.0)]
    [InlineData(4000, 1.0)]
    public void Normalize_VulnAge_IsLinearAndCapped(int days, double expected)
    {
        var factors = RiskScoringEngine.Normalize(Inputs(ageDays: days), Now);

        Assert.Equal((decimal)expected, factors[RiskFactor.VulnAge].Value);
    }

    [Fact]
    public void Normalize_FutureFirstDetected_GivesZeroAge()
    {
        var factors = RiskScoringEngine.Normalize(Inputs() with { FirstDetectedAt = Now.AddDays(10) }, Now);

        Assert.Equal(0m, factors[RiskFactor.VulnAge].Value);
    }

    // ------------------------------------------------------------ EX-50 incomplete input

    [Fact]
    public void Calculate_MissingAssetCriticality_IsIncomplete()
    {
        var calc = RiskScoringEngine.Calculate(Inputs(criticality: null), SeedWeights, SeedThresholds, Now);

        Assert.False(calc.Succeeded);
        Assert.Equal(RiskErrorCodes.RiskInputIncomplete, calc.ErrorCode);
        Assert.Contains("MISSING_FACTOR: AssetCriticality", calc.Issues);
        Assert.Null(calc.Breakdown);
    }

    [Fact]
    public void Calculate_MissingCvssAndSeverity_IsIncomplete()
    {
        var calc = RiskScoringEngine.Calculate(Inputs(cvss: null, severity: null), SeedWeights, SeedThresholds, Now);

        Assert.False(calc.Succeeded);
        Assert.Equal(RiskErrorCodes.RiskInputIncomplete, calc.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(11.0)]
    [InlineData(-1.0)]
    public void Calculate_NoValidCvss_FallsBackToSeverity(double? cvss)
    {
        var calc = RiskScoringEngine.Calculate(
            Inputs(cvss: cvss is null ? null : (decimal)cvss, severity: "Medium"), SeedWeights, SeedThresholds, Now);

        Assert.True(calc.Succeeded);
        var factor = calc.Breakdown!.Factors.Single(f => f.Factor == nameof(RiskFactor.Cvss));
        Assert.Equal("severity", factor.Source);
        Assert.Equal(0.5m, factor.Normalized);
    }

    [Fact]
    public void Calculate_MissingFactorWithZeroWeight_IsNotRequired()
    {
        RiskWeightConfig[] weights = [new(RiskFactor.Cvss, 1m), new(RiskFactor.AssetCriticality, 0m)];

        var calc = RiskScoringEngine.Calculate(Inputs(criticality: null), weights, SeedThresholds, Now);

        Assert.True(calc.Succeeded);
    }

    // ------------------------------------------------------------ EX-54 weight validation

    [Fact]
    public void ValidateWeights_SeedWeights_SumToOneAndAreValid()
    {
        Assert.Equal(1.0000m, SeedWeights.Sum(w => w.Weight));
        Assert.True(RiskScoringEngine.ValidateWeights(SeedWeights).IsValid);
    }

    [Theory]
    [InlineData(0.9500)]
    [InlineData(1.0500)]
    [InlineData(0.9998)]
    public void ValidateWeights_SumNotOne_IsRejected(double cvssWeight)
    {
        var weights = SeedWeights.Select(w => w.Factor == RiskFactor.Cvss ? w with { Weight = 0.2500m + ((decimal)cvssWeight - 1m) } : w).ToArray();

        var result = RiskScoringEngine.ValidateWeights(weights);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.StartsWith("WEIGHT_SUM", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateWeights_NegativeWeight_IsRejectedEvenIfSumIsOne()
    {
        RiskWeightConfig[] weights = [new(RiskFactor.Cvss, 1.1m), new(RiskFactor.Kev, -0.1m)];

        var result = RiskScoringEngine.ValidateWeights(weights);

        Assert.False(result.IsValid);
        Assert.Contains("NEGATIVE_WEIGHT: Kev", result.Issues);
        Assert.Contains("WEIGHT_ABOVE_ONE: Cvss", result.Issues);
    }

    [Fact]
    public void ValidateWeights_DuplicateFactor_IsRejected()
    {
        RiskWeightConfig[] weights = [new(RiskFactor.Cvss, 0.5m), new(RiskFactor.Cvss, 0.5m)];

        Assert.Contains("DUPLICATE_FACTOR: Cvss", RiskScoringEngine.ValidateWeights(weights).Issues);
    }

    [Fact]
    public void ValidateWeights_EmptyOrNull_IsRejected()
    {
        Assert.False(RiskScoringEngine.ValidateWeights([]).IsValid);
        Assert.False(RiskScoringEngine.ValidateWeights(null).IsValid);
    }

    [Fact]
    public void ValidateWeights_InactiveRowsAreExcludedFromSum()
    {
        // factor ซ้ำ (active + inactive) ยังถือว่าซ้ำ; แถว inactive ที่ไม่ซ้ำไม่ถูกนับในผลรวม
        var weights = SeedWeights.Append(new RiskWeightConfig(RiskFactor.ThreatIntel, 0.5m, IsActive: false)).ToArray();
        RiskWeightConfig[] distinct = [new(RiskFactor.Cvss, 1m), new(RiskFactor.Kev, 0.4m, IsActive: false)];

        Assert.False(RiskScoringEngine.ValidateWeights(weights).IsValid);
        Assert.True(RiskScoringEngine.ValidateWeights(distinct).IsValid);
    }

    [Fact]
    public void Calculate_InvalidWeights_IsRejectedWithoutScore()
    {
        RiskWeightConfig[] weights = [new(RiskFactor.Cvss, 0.5m)];

        var calc = RiskScoringEngine.Calculate(Inputs(), weights, SeedThresholds, Now);

        Assert.False(calc.Succeeded);
        Assert.Equal(RiskErrorCodes.InvalidRiskWeight, calc.ErrorCode);
        Assert.Null(calc.Breakdown);
    }

    // ------------------------------------------------------------ classification (BR-01)

    [Theory]
    [InlineData(0.0, RiskLevel.Informational)]
    [InlineData(19.99, RiskLevel.Informational)]
    [InlineData(20.0, RiskLevel.Low)]
    [InlineData(39.99, RiskLevel.Low)]
    [InlineData(40.0, RiskLevel.Medium)]
    [InlineData(59.99, RiskLevel.Medium)]
    [InlineData(60.0, RiskLevel.High)]
    [InlineData(79.99, RiskLevel.High)]
    [InlineData(80.0, RiskLevel.Critical)]
    [InlineData(100.0, RiskLevel.Critical)]
    public void Classify_Boundaries_UseMinInclusiveMaxExclusive_TopInclusive(double score, RiskLevel expected)
        => Assert.Equal(expected, RiskScoringEngine.Classify((decimal)score, SeedThresholds));

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void Classify_OutOfRange_ReturnsNull(double score)
        => Assert.Null(RiskScoringEngine.Classify((decimal)score, SeedThresholds));

    [Fact]
    public void ValidateThresholds_Seed_IsValid()
        => Assert.True(RiskScoringEngine.ValidateThresholds(SeedThresholds).IsValid);

    [Fact]
    public void ValidateThresholds_GapOverlapDuplicateOrPartialCoverage_IsRejected()
    {
        var gap = SeedThresholds.Select(t => t.Level == RiskLevel.Low ? t with { MaxScore = 39m } : t).ToArray();
        var overlap = SeedThresholds.Select(t => t.Level == RiskLevel.Low ? t with { MaxScore = 45m } : t).ToArray();
        var duplicate = SeedThresholds.Append(new RiskThresholdConfig(RiskLevel.Low, 100m, 101m)).ToArray();
        var partial = SeedThresholds.Where(t => t.Level != RiskLevel.Critical).ToArray();

        Assert.False(RiskScoringEngine.ValidateThresholds(gap).IsValid);
        Assert.False(RiskScoringEngine.ValidateThresholds(overlap).IsValid);
        Assert.False(RiskScoringEngine.ValidateThresholds(duplicate).IsValid);
        Assert.False(RiskScoringEngine.ValidateThresholds(partial).IsValid);
        Assert.False(RiskScoringEngine.ValidateThresholds([]).IsValid);
    }

    [Fact]
    public void Calculate_InvalidThresholds_IsRejected()
    {
        var partial = SeedThresholds.Where(t => t.Level != RiskLevel.Critical).ToArray();

        var calc = RiskScoringEngine.Calculate(Inputs(), SeedWeights, partial, Now);

        Assert.False(calc.Succeeded);
        Assert.Equal(RiskErrorCodes.InvalidRiskThreshold, calc.ErrorCode);
    }

    [Theory]
    [InlineData(-3.0, 0.0)]
    [InlineData(100.004, 100.0)]
    [InlineData(55.555, 55.56)]
    [InlineData(250.0, 100.0)]
    public void ClampScore_KeepsInvariant(double value, double expected)
        => Assert.Equal((decimal)expected, RiskScoringEngine.ClampScore((decimal)value));

    // ------------------------------------------------------------ manual override (FR-20)

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateOverride_RequiresReason(string? reason)
    {
        var (value, errors) = RiskScoringEngine.ValidateOverride(new OverrideRiskRequest(70m, "High", reason, ValidRowVersion));

        Assert.Null(value);
        Assert.Equal(new[] { RiskErrorCodes.ReasonRequired }, errors["reason"]);
    }

    [Fact]
    public void ValidateOverride_NullRequest_RequiresReasonValueAndRowVersion()
    {
        var (value, errors) = RiskScoringEngine.ValidateOverride(null);

        Assert.Null(value);
        Assert.Contains("reason", errors.Keys);
        Assert.Contains("score", errors.Keys);
        Assert.Equal(new[] { RiskErrorCodes.RowVersionRequired }, errors["rowVersion"]);
    }

    [Fact]
    public void ValidateOverride_ValidRequest_IsAcceptedAndTrimmed()
    {
        var (value, errors) = RiskScoringEngine.ValidateOverride(
            new OverrideRiskRequest(null, "high", "  compensating control in place  ", ValidRowVersion));

        Assert.Empty(errors);
        Assert.NotNull(value);
        Assert.Equal(RiskLevel.High, value!.Level);
        Assert.Null(value.Score);
        Assert.Equal("compensating control in place", value.Reason);
        Assert.Equal(8, value.RowVersion.Length);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    [InlineData(50.123)]
    public void ValidateOverride_ScoreOutsideRangeOrPrecision_IsRejected(double score)
    {
        var (_, errors) = RiskScoringEngine.ValidateOverride(new OverrideRiskRequest((decimal)score, null, "reason", ValidRowVersion));

        Assert.Equal(new[] { RiskErrorCodes.InvalidScore }, errors["score"]);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("Severe")]
    [InlineData("High;")]
    public void ValidateOverride_UnknownRiskLevel_IsRejected(string level)
    {
        var (_, errors) = RiskScoringEngine.ValidateOverride(new OverrideRiskRequest(null, level, "reason", ValidRowVersion));

        Assert.Equal(new[] { RiskErrorCodes.InvalidRiskLevel }, errors["riskLevel"]);
    }

    [Fact]
    public void ValidateOverride_NeitherScoreNorLevel_IsRejected()
    {
        var (_, errors) = RiskScoringEngine.ValidateOverride(new OverrideRiskRequest(null, null, "reason", ValidRowVersion));

        Assert.Equal(new[] { RiskErrorCodes.OverrideValueRequired }, errors["score"]);
    }

    [Fact]
    public void ValidateOverride_ReasonTooLongOrControlChars_IsRejected()
    {
        var (_, tooLong) = RiskScoringEngine.ValidateOverride(
            new OverrideRiskRequest(50m, null, new string('x', RiskScoringEngine.MaxReasonLength + 1), ValidRowVersion));
        var (_, control) = RiskScoringEngine.ValidateOverride(new OverrideRiskRequest(50m, null, "bad\u0000reason", ValidRowVersion));

        Assert.Equal(new[] { RiskErrorCodes.ReasonTooLong }, tooLong["reason"]);
        Assert.Equal(new[] { RiskErrorCodes.InvalidFormat }, control["reason"]);
    }

    [Theory]
    [InlineData("not-base64!")]
    [InlineData("AAAA")]
    public void ParseRowVersion_InvalidValue_IsRejected(string value)
    {
        Assert.Null(RiskScoringEngine.ParseRowVersion(value, required: false, out var code));
        Assert.Equal(RiskErrorCodes.InvalidRowVersion, code);
    }

    [Fact]
    public void ParseRowVersion_OptionalAndMissing_IsAllowed()
    {
        Assert.Null(RiskScoringEngine.ParseRowVersion(null, required: false, out var code));
        Assert.Null(code);
    }

    // ------------------------------------------------------------ helpers

    /// <summary>weight สุ่ม 9 ตัว (ทศนิยม 4 ตำแหน่ง) ผลรวม = 1.0000 พอดี.</summary>
    private static RiskWeightConfig[] RandomWeights(Random rng)
    {
        var factors = Enum.GetValues<RiskFactor>();
        var cuts = Enumerable.Range(0, factors.Length - 1).Select(_ => rng.Next(0, 10_001)).Order().ToList();
        cuts.Insert(0, 0);
        cuts.Add(10_000);

        return factors.Select((f, i) => new RiskWeightConfig(f, (cuts[i + 1] - cuts[i]) / 10_000m)).ToArray();
    }
}
