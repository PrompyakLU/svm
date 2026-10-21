using SvmApi.Application.Sla;
using SvmApi.Domain.Assets;
using SvmApi.Domain.Sla;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Tests.Sla;

/// <summary>
/// SlaCalculator / SlaEscalationMatrix (pure · C9 · US-024/025/026 · BR-05/08/10 · EX-65..70 · task 5.3/7.4):
/// due date บน business calendar (ข้ามเสาร์-อาทิตย์/วันหยุด), เลือก policy เจาะจงที่สุด, breach/near-due,
/// recalc เก็บค่าเดิม + เหตุผล, กัน criticality หาย (BR-05), escalation แบบ idempotent.
/// </summary>
public class SlaCalculatorTests
{
    // 2026-10-05 = จันทร์
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static readonly DateOnly Tue = Mon.AddDays(1);
    private static readonly DateOnly Wed = Mon.AddDays(2);
    private static readonly DateOnly Fri = Mon.AddDays(4);
    private static readonly DateOnly Sat = Mon.AddDays(5);
    private static readonly DateOnly NextMon = Mon.AddDays(7);
    private static readonly DateOnly NextTue = Mon.AddDays(8);

    private static DateTime At(DateOnly date, int hour, int minute = 0)
        => date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc);

    /// <summary>ปฏิทินจันทร์–ศุกร์เป็นวันทำงาน (ยกเว้น <paramref name="holidays"/>) ตลอด <paramref name="days"/> วันนับจาก <paramref name="from"/>.</summary>
    private static SlaCalendar Calendar(DateOnly from, int days, params DateOnly[] holidays)
    {
        var rows = new List<KeyValuePair<DateOnly, bool>>();
        for (var i = 0; i < days; i++)
        {
            var date = from.AddDays(i);
            var working = date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(date);
            rows.Add(new KeyValuePair<DateOnly, bool>(date, working));
        }

        return new SlaCalendar(rows);
    }

    private static SlaPolicyCandidate Policy(
        RiskLevel level,
        int resolveHours,
        bool? internet = null,
        bool kevOnly = false,
        AssetCriticality? criticality = null,
        bool active = true,
        int ackHours = 4,
        int startHours = 24)
        => new(Guid.NewGuid(), level, criticality, internet, kevOnly, ackHours, startHours, resolveHours, active);

    private static SlaSubject Subject(
        RiskLevel level = RiskLevel.Critical,
        AssetCriticality? criticality = AssetCriticality.High,
        bool internet = false,
        bool kev = false)
        => new(level, criticality, internet, kev);

    [Fact]
    public void Test_calendar_anchor_dates_are_what_the_tests_assume()
    {
        Assert.Equal(DayOfWeek.Monday, Mon.DayOfWeek);
        Assert.Equal(DayOfWeek.Friday, Fri.DayOfWeek);
        Assert.Equal(DayOfWeek.Saturday, Sat.DayOfWeek);
    }

    // ------------------------------------------------------------ due date over business calendar (US-024)

    [Fact]
    public void AddBusinessHours_within_one_working_day()
    {
        var due = SlaCalculator.AddBusinessHours(At(Mon, 9), 4, Calendar(Mon, 30));

        Assert.Equal(At(Mon, 13), due);
    }

    [Fact]
    public void AddBusinessHours_crosses_midnight_between_working_days()
    {
        var due = SlaCalculator.AddBusinessHours(At(Mon, 22), 4, Calendar(Mon, 30));

        Assert.Equal(At(Tue, 2), due);
    }

    [Fact]
    public void AddBusinessHours_skips_weekend()
    {
        // ศุกร์ 18:00 + 24 ชม. = เหลือศุกร์ 6 ชม. → ข้ามเสาร์-อาทิตย์ → จันทร์ 18:00
        var due = SlaCalculator.AddBusinessHours(At(Fri, 18), 24, Calendar(Mon, 30));

        Assert.Equal(At(NextMon, 18), due);
    }

    [Fact]
    public void AddBusinessHours_started_on_weekend_begins_counting_on_next_working_day()
    {
        var due = SlaCalculator.AddBusinessHours(At(Sat, 10), 4, Calendar(Mon, 30));

        Assert.Equal(At(NextMon, 4), due);
    }

    [Fact]
    public void AddBusinessHours_168_hours_is_seven_working_days()
    {
        // จันทร์ 00:00 + 168 ชม. (Critical) = พุธสัปดาห์ถัดไป 00:00 (ข้ามเสาร์-อาทิตย์ 1 รอบ)
        var due = SlaCalculator.AddBusinessHours(At(Mon, 0), 168, Calendar(Mon, 60));

        Assert.Equal(At(NextMon.AddDays(2), 0), due);
    }

    [Fact]
    public void AddBusinessHours_skips_calendar_holiday()
    {
        // อังคารเป็นวันหยุด: จันทร์ 20:00 + 8 ชม. = จันทร์ 4 ชม. + ข้ามอังคาร + พุธ 4 ชม.
        var due = SlaCalculator.AddBusinessHours(At(Mon, 20), 8, Calendar(Mon, 30, Tue));

        Assert.Equal(At(Wed, 4), due);
    }

    [Fact]
    public void AddBusinessHours_returns_null_when_calendar_does_not_cover_needed_dates()
    {
        // ปฏิทินครอบคลุมแค่ 2 วัน แต่ต้องใช้ 72 ชม. (EX-67)
        Assert.Null(SlaCalculator.AddBusinessHours(At(Mon, 9), 72, Calendar(Mon, 2)));
    }

    [Fact]
    public void AddBusinessHours_returns_null_when_start_is_before_calendar_start()
    {
        Assert.Null(SlaCalculator.AddBusinessHours(At(Mon.AddDays(-1), 9), 4, Calendar(Mon, 30)));
    }

    [Fact]
    public void ComputeDueDates_computes_all_three_milestones_from_policy()
    {
        var policy = Policy(RiskLevel.Critical, resolveHours: 168, ackHours: 4, startHours: 24);

        var result = SlaCalculator.ComputeDueDates(At(Mon, 9), policy, Calendar(Mon, 60));

        Assert.True(result.Succeeded);
        Assert.Equal(At(Mon, 13), result.Dates!.AcknowledgeDue);
        Assert.Equal(At(Tue, 9), result.Dates.RemediationStartDue);
        Assert.Equal(At(NextMon.AddDays(2), 9), result.Dates.ResolutionDue);
    }

    [Fact]
    public void ComputeDueDates_fails_with_calendar_incomplete_when_calendar_is_empty_or_has_no_working_day()
    {
        var policy = Policy(RiskLevel.High, 336);

        var empty = SlaCalculator.ComputeDueDates(At(Mon, 9), policy, new SlaCalendar([]));
        var allOff = SlaCalculator.ComputeDueDates(
            At(Mon, 9), policy, new SlaCalendar([new KeyValuePair<DateOnly, bool>(Mon, false)]));

        Assert.Equal(SlaErrorCodes.CalendarIncomplete, empty.ErrorCode);
        Assert.Equal(SlaErrorCodes.CalendarIncomplete, allOff.ErrorCode);
    }

    [Fact]
    public void ComputeDueDates_fails_when_calendar_ends_before_resolution_due()
    {
        var policy = Policy(RiskLevel.Low, 2160);

        var result = SlaCalculator.ComputeDueDates(At(Mon, 9), policy, Calendar(Mon, 30));

        Assert.Equal(SlaErrorCodes.CalendarIncomplete, result.ErrorCode);
        Assert.Null(result.Dates);
    }

    [Theory]
    [InlineData(0, 24, 168)]
    [InlineData(4, 0, 168)]
    [InlineData(4, 24, -1)]
    [InlineData(4, 24, SlaCalculator.MaxPolicyHours + 1)]
    public void ComputeDueDates_rejects_invalid_policy_hours(int ack, int start, int resolve)
    {
        var policy = Policy(RiskLevel.Critical, resolve, ackHours: ack, startHours: start);

        var result = SlaCalculator.ComputeDueDates(At(Mon, 9), policy, Calendar(Mon, 60));

        Assert.Equal(SlaErrorCodes.PolicyInvalid, result.ErrorCode);
    }

    [Fact]
    public void ComputeDueDates_treats_unspecified_kind_from_database_as_utc()
    {
        var unspecified = DateTime.SpecifyKind(At(Mon, 9), DateTimeKind.Unspecified);

        var result = SlaCalculator.ComputeDueDates(unspecified, Policy(RiskLevel.Critical, 168), Calendar(Mon, 60));

        Assert.Equal(At(Mon, 13), result.Dates!.AcknowledgeDue);
    }

    // ------------------------------------------------------------ most specific policy (BR-08 / EX-66)

    [Fact]
    public void SelectPolicy_returns_base_policy_when_only_base_matches()
    {
        var baseRow = Policy(RiskLevel.Critical, 168);
        var internetRow = Policy(RiskLevel.Critical, 120, internet: true);

        var chosen = SlaCalculator.SelectPolicy([baseRow, internetRow], Subject(internet: false));

        Assert.Equal(baseRow.Id, chosen!.Id);
    }

    [Fact]
    public void SelectPolicy_prefers_internet_row_over_base_for_internet_facing_asset()
    {
        var baseRow = Policy(RiskLevel.Critical, 168);
        var internetRow = Policy(RiskLevel.Critical, 120, internet: true);

        var chosen = SlaCalculator.SelectPolicy([baseRow, internetRow], Subject(internet: true));

        Assert.Equal(internetRow.Id, chosen!.Id);
    }

    [Fact]
    public void SelectPolicy_prefers_combined_internet_and_kev_row_over_single_dimension_rows()
    {
        var baseRow = Policy(RiskLevel.Critical, 168);
        var internetRow = Policy(RiskLevel.Critical, 120, internet: true);
        var kevRow = Policy(RiskLevel.Critical, 72, kevOnly: true);
        var combined = Policy(RiskLevel.Critical, 48, internet: true, kevOnly: true);

        var chosen = SlaCalculator.SelectPolicy([baseRow, internetRow, kevRow, combined], Subject(internet: true, kev: true));

        Assert.Equal(combined.Id, chosen!.Id);
    }

    [Fact]
    public void SelectPolicy_ignores_kev_only_row_for_non_kev_instance_and_internet_row_for_internal_asset()
    {
        var baseRow = Policy(RiskLevel.Critical, 168);
        var kevRow = Policy(RiskLevel.Critical, 72, kevOnly: true);
        var internetRow = Policy(RiskLevel.Critical, 120, internet: true);

        var chosen = SlaCalculator.SelectPolicy([kevRow, internetRow, baseRow], Subject(internet: false, kev: false));

        Assert.Equal(baseRow.Id, chosen!.Id);
    }

    [Fact]
    public void SelectPolicy_breaks_equal_specificity_by_choosing_the_stricter_policy()
    {
        // internet+KEV แต่ไม่มีแถว combined: internet (504) vs KEV (336) เจาะจงเท่ากัน → เลือกอันที่เข้มกว่า
        var baseRow = Policy(RiskLevel.Medium, 720);
        var internetRow = Policy(RiskLevel.Medium, 504, internet: true);
        var kevRow = Policy(RiskLevel.Medium, 336, kevOnly: true);

        var forward = SlaCalculator.SelectPolicy([baseRow, internetRow, kevRow], Subject(RiskLevel.Medium, internet: true, kev: true));
        var reversed = SlaCalculator.SelectPolicy([kevRow, internetRow, baseRow], Subject(RiskLevel.Medium, internet: true, kev: true));

        Assert.Equal(kevRow.Id, forward!.Id);
        Assert.Equal(kevRow.Id, reversed!.Id);
    }

    [Fact]
    public void SelectPolicy_criticality_specific_row_wins_only_for_matching_criticality()
    {
        var baseRow = Policy(RiskLevel.High, 336);
        var criticalAsset = Policy(RiskLevel.High, 240, criticality: AssetCriticality.Critical);

        var matching = SlaCalculator.SelectPolicy([baseRow, criticalAsset], Subject(RiskLevel.High, AssetCriticality.Critical));
        var other = SlaCalculator.SelectPolicy([baseRow, criticalAsset], Subject(RiskLevel.High, AssetCriticality.Low));
        var unknown = SlaCalculator.SelectPolicy([baseRow, criticalAsset], Subject(RiskLevel.High, criticality: null));

        Assert.Equal(criticalAsset.Id, matching!.Id);
        Assert.Equal(baseRow.Id, other!.Id);
        Assert.Equal(baseRow.Id, unknown!.Id);
    }

    [Fact]
    public void SelectPolicy_returns_null_when_no_active_policy_for_risk_level()
    {
        var inactive = Policy(RiskLevel.Critical, 168, active: false);
        var otherLevel = Policy(RiskLevel.High, 336);

        Assert.Null(SlaCalculator.SelectPolicy([inactive, otherLevel], Subject(RiskLevel.Critical)));
        Assert.Null(SlaCalculator.SelectPolicy([], Subject(RiskLevel.Critical)));
    }

    [Theory]
    [InlineData(RiskLevel.Critical, false, false, 168)]
    [InlineData(RiskLevel.Critical, true, false, 120)]
    [InlineData(RiskLevel.Critical, false, true, 72)]
    [InlineData(RiskLevel.Critical, true, true, 48)]
    [InlineData(RiskLevel.High, true, true, 120)]
    [InlineData(RiskLevel.Medium, true, true, 336)] // internet 504 vs KEV 336 → เข้มกว่า
    [InlineData(RiskLevel.Low, true, true, 2160)] // Low มีแต่แถวฐาน
    [InlineData(RiskLevel.Informational, false, false, 4320)]
    public void SelectPolicy_over_seed_catalog_picks_expected_resolve_hours(RiskLevel level, bool internet, bool kev, int expectedResolve)
    {
        var catalog = SeedData.SlaPolicies
            .Select(p => new SlaPolicyCandidate(
                Guid.NewGuid(), p.RiskLevel, p.AssetCriticality, p.InternetExposure, p.KevOnly,
                p.AcknowledgeHours, p.StartHours, p.ResolveHours, IsActive: true))
            .ToList();

        var chosen = SlaCalculator.SelectPolicy(catalog, Subject(level, AssetCriticality.High, internet, kev));

        Assert.NotNull(chosen);
        Assert.Equal(expectedResolve, chosen!.ResolveHours);
    }

    [Fact]
    public void Seed_catalog_has_an_applicable_policy_for_every_risk_level()
    {
        var catalog = SeedData.SlaPolicies
            .Select(p => new SlaPolicyCandidate(
                Guid.NewGuid(), p.RiskLevel, p.AssetCriticality, p.InternetExposure, p.KevOnly,
                p.AcknowledgeHours, p.StartHours, p.ResolveHours, IsActive: true))
            .ToList();

        foreach (var level in Enum.GetValues<RiskLevel>())
        {
            Assert.NotNull(SlaCalculator.SelectPolicy(catalog, Subject(level, internet: false, kev: false)));
        }
    }

    // ------------------------------------------------------------ breach detection (FR-36 / EX-68)

    [Fact]
    public void EvaluateBreach_on_track_near_due_and_breached_boundaries()
    {
        var started = At(Mon, 0);
        var due = started.AddHours(240); // ช่วง 240 ชม. → near-due 20% = 48 ชม.

        Assert.Equal(SlaBreachStatus.OnTrack, SlaCalculator.EvaluateBreach(started, due, due.AddHours(-49), 0.2));
        Assert.Equal(SlaBreachStatus.NearDue, SlaCalculator.EvaluateBreach(started, due, due.AddHours(-48), 0.2));
        Assert.Equal(SlaBreachStatus.NearDue, SlaCalculator.EvaluateBreach(started, due, due.AddSeconds(-1), 0.2));
        Assert.Equal(SlaBreachStatus.NearDue, SlaCalculator.EvaluateBreach(started, due, due, 0.2)); // ยังไม่เกิน (now > due เท่านั้น)
        Assert.Equal(SlaBreachStatus.Breached, SlaCalculator.EvaluateBreach(started, due, due.AddSeconds(1), 0.2));
    }

    [Fact]
    public void EvaluateBreach_with_zero_length_window_is_never_near_due()
    {
        var at = At(Mon, 9);

        Assert.Equal(SlaBreachStatus.OnTrack, SlaCalculator.EvaluateBreach(at, at, at.AddMinutes(-1), 0.2));
        Assert.Equal(SlaBreachStatus.Breached, SlaCalculator.EvaluateBreach(at, at, at.AddMinutes(1), 0.2));
    }

    [Fact]
    public void DaysOverdue_counts_whole_days_and_is_zero_before_due()
    {
        var due = At(Mon, 9);

        Assert.Equal(0, SlaCalculator.DaysOverdue(due, due.AddHours(-5)));
        Assert.Equal(0, SlaCalculator.DaysOverdue(due, due));
        Assert.Equal(0, SlaCalculator.DaysOverdue(due, due.AddHours(23)));
        Assert.Equal(2, SlaCalculator.DaysOverdue(due, due.AddDays(2).AddHours(23)));
        Assert.Equal(3, SlaCalculator.DaysOverdue(due, due.AddDays(3)));
    }

    [Fact]
    public void RemainingSeconds_is_negative_when_overdue()
    {
        var due = At(Mon, 9);

        Assert.Equal(3600, SlaCalculator.RemainingSeconds(due, due.AddHours(-1)));
        Assert.True(SlaCalculator.RemainingSeconds(due, due.AddHours(1)) < 0);
    }

    // ------------------------------------------------------------ recalc keeps previous value + reason (FR-37 / BR-10)

    private static SlaTimerState Timer(Guid policyId, SlaDueDates dates, string? previousJson = null, int escalation = 0)
        => new(At(Mon, 9), dates, policyId, SlaBreachStatus.OnTrack, previousJson, escalation);

    private static readonly SlaDueDates MediumDates = new(At(Mon, 13), At(Tue, 9), At(NextTue, 9));

    [Fact]
    public void Recalculate_with_identical_result_changes_nothing_and_adds_no_history()
    {
        var policyId = Guid.NewGuid();
        var current = Timer(policyId, MediumDates);

        var result = SlaCalculator.Recalculate(current, MediumDates, policyId, riskIncreased: false, SlaTrigger.Manual, At(Wed, 10));

        Assert.False(result.Changed);
        Assert.Equal(MediumDates, result.Dates);
        Assert.Null(result.PreviousDueJson);
    }

    [Fact]
    public void Recalculate_keeps_previous_due_dates_and_reason_when_risk_increases()
    {
        var oldPolicy = Guid.NewGuid();
        var newPolicy = Guid.NewGuid();
        var computed = new SlaDueDates(At(Mon, 11), At(Mon, 21), At(Fri, 9));
        var current = Timer(oldPolicy, MediumDates);
        var now = At(Wed, 10);

        var result = SlaCalculator.Recalculate(current, computed, newPolicy, riskIncreased: true, SlaTrigger.RiskLevelChanged, now);

        Assert.True(result.Changed);
        Assert.Equal(computed, result.Dates);
        Assert.Equal(newPolicy, result.PolicyId);

        var history = SlaCalculator.ParsePreviousDue(result.PreviousDueJson);
        var entry = Assert.Single(history);
        Assert.Equal("RISK_LEVEL_CHANGED", entry.Reason);
        Assert.Equal(oldPolicy, entry.PolicyId);
        Assert.Equal(MediumDates.AcknowledgeDue, entry.AcknowledgeDue);
        Assert.Equal(MediumDates.RemediationStartDue, entry.RemediationStartDue);
        Assert.Equal(MediumDates.ResolutionDue, entry.ResolutionDue);
        Assert.Equal(now, entry.RecordedAt);
    }

    [Fact]
    public void Recalculate_with_risk_increase_never_loosens_any_due_date()
    {
        // acknowledge ใหม่ "ช้ากว่า" เดิม → ต้องคงค่าเดิม (BR-10: ใช้ค่าที่เข้มกว่า); start/resolve เร็วกว่า → ใช้ค่าใหม่
        var computed = new SlaDueDates(At(Mon, 15), At(Mon, 21), At(Fri, 9));

        var result = SlaCalculator.Recalculate(
            Timer(Guid.NewGuid(), MediumDates), computed, Guid.NewGuid(), riskIncreased: true, SlaTrigger.RiskLevelChanged, At(Wed, 10));

        Assert.Equal(MediumDates.AcknowledgeDue, result.Dates.AcknowledgeDue);
        Assert.Equal(computed.RemediationStartDue, result.Dates.RemediationStartDue);
        Assert.Equal(computed.ResolutionDue, result.Dates.ResolutionDue);
    }

    [Fact]
    public void Recalculate_when_risk_does_not_increase_applies_new_dates_and_still_records_previous()
    {
        var looser = new SlaDueDates(At(Tue, 9), At(Wed, 9), At(NextMon.AddDays(10), 9));

        var result = SlaCalculator.Recalculate(
            Timer(Guid.NewGuid(), MediumDates), looser, Guid.NewGuid(), riskIncreased: false, SlaTrigger.PolicyChanged, At(Wed, 10));

        Assert.True(result.Changed);
        Assert.Equal(looser, result.Dates);
        var entry = Assert.Single(SlaCalculator.ParsePreviousDue(result.PreviousDueJson));
        Assert.Equal("POLICY_CHANGED", entry.Reason);
        Assert.Equal(MediumDates.ResolutionDue, entry.ResolutionDue);
    }

    [Fact]
    public void Recalculate_with_policy_change_only_is_recorded_even_if_dates_equal()
    {
        var result = SlaCalculator.Recalculate(
            Timer(Guid.NewGuid(), MediumDates), MediumDates, Guid.NewGuid(), riskIncreased: false, SlaTrigger.PolicyChanged, At(Wed, 10));

        Assert.True(result.Changed);
        Assert.Single(SlaCalculator.ParsePreviousDue(result.PreviousDueJson));
    }

    [Fact]
    public void Recalculate_appends_history_in_order_and_caps_entries()
    {
        var state = Timer(Guid.NewGuid(), MediumDates);
        var total = SlaCalculator.MaxPreviousDueEntries + 5;

        for (var i = 0; i < total; i++)
        {
            var computed = new SlaDueDates(At(Mon, 13), At(Tue, 9), At(NextTue, 9).AddHours(i + 1));
            var result = SlaCalculator.Recalculate(state, computed, state.PolicyId, riskIncreased: false, SlaTrigger.Manual, At(Wed, 10));
            Assert.True(result.Changed);
            state = state with { Dates = result.Dates, PreviousDueJson = result.PreviousDueJson };
        }

        var history = SlaCalculator.ParsePreviousDue(state.PreviousDueJson);
        Assert.Equal(SlaCalculator.MaxPreviousDueEntries, history.Count);
        // ล่าสุดคือค่าก่อนการ recalc รอบสุดท้าย (i = total-2 → +total-1 ชม.)
        Assert.Equal(At(NextTue, 9).AddHours(total - 1), history[history.Count - 1].ResolutionDue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    public void ParsePreviousDue_returns_empty_for_missing_or_corrupt_json(string? json)
    {
        Assert.Empty(SlaCalculator.ParsePreviousDue(json));
    }

    [Fact]
    public void IsMoreSevere_follows_risk_level_order()
    {
        Assert.True(SlaCalculator.IsMoreSevere(RiskLevel.Critical, RiskLevel.High));
        Assert.True(SlaCalculator.IsMoreSevere(RiskLevel.Medium, RiskLevel.Informational));
        Assert.False(SlaCalculator.IsMoreSevere(RiskLevel.High, RiskLevel.High));
        Assert.False(SlaCalculator.IsMoreSevere(RiskLevel.Low, RiskLevel.Critical));
    }

    // ------------------------------------------------------------ eligibility: BR-05 criticality block / EX-65

    [Fact]
    public void CheckEligibility_blocks_active_prod_asset_without_criticality()
    {
        var code = SlaCalculator.CheckEligibility(
            VulnerabilityStatus.Confirmed, RiskLevel.High, AssetEnvironment.Prod, AssetStatus.Active, criticality: null);

        Assert.Equal(SlaErrorCodes.CriticalityMissing, code);
    }

    [Theory]
    [InlineData(AssetEnvironment.Staging, AssetStatus.Active)]
    [InlineData(AssetEnvironment.Dev, AssetStatus.Active)]
    [InlineData(AssetEnvironment.Prod, AssetStatus.Decommissioned)]
    public void CheckEligibility_does_not_block_outside_active_prod_without_criticality(AssetEnvironment env, AssetStatus status)
    {
        Assert.Null(SlaCalculator.CheckEligibility(VulnerabilityStatus.Confirmed, RiskLevel.High, env, status, criticality: null));
    }

    [Fact]
    public void CheckEligibility_allows_active_prod_asset_with_criticality()
    {
        Assert.Null(SlaCalculator.CheckEligibility(
            VulnerabilityStatus.New, RiskLevel.Critical, AssetEnvironment.Prod, AssetStatus.Active, AssetCriticality.Low));
    }

    [Fact]
    public void CheckEligibility_requires_risk_level()
    {
        var code = SlaCalculator.CheckEligibility(
            VulnerabilityStatus.New, riskLevel: null, AssetEnvironment.Staging, AssetStatus.Active, AssetCriticality.High);

        Assert.Equal(SlaErrorCodes.RiskLevelMissing, code);
    }

    [Theory]
    [InlineData(VulnerabilityStatus.Resolved)]
    [InlineData(VulnerabilityStatus.Closed)]
    [InlineData(VulnerabilityStatus.FalsePositive)]
    [InlineData(VulnerabilityStatus.RiskAccepted)]
    [InlineData(VulnerabilityStatus.ExceptionApproved)]
    [InlineData(VulnerabilityStatus.Archived)]
    public void CheckEligibility_stopped_statuses_are_not_active_even_when_other_data_is_missing(VulnerabilityStatus status)
    {
        var code = SlaCalculator.CheckEligibility(status, riskLevel: null, AssetEnvironment.Prod, AssetStatus.Active, criticality: null);

        Assert.Equal(SlaErrorCodes.NotActive, code);
        Assert.False(SlaCalculator.IsTimerRunning(status));
    }

    [Theory]
    [InlineData(VulnerabilityStatus.New)]
    [InlineData(VulnerabilityStatus.Confirmed)]
    [InlineData(VulnerabilityStatus.Assigned)]
    [InlineData(VulnerabilityStatus.InProgress)]
    [InlineData(VulnerabilityStatus.PendingVerification)]
    [InlineData(VulnerabilityStatus.Reopened)]
    public void IsTimerRunning_is_true_for_open_statuses(VulnerabilityStatus status)
    {
        Assert.True(SlaCalculator.IsTimerRunning(status));
    }

    // ------------------------------------------------------------ DTO

    [Fact]
    public void ToDto_running_timer_computes_live_breach_remaining_and_days_overdue()
    {
        var started = At(Mon, 0);
        var state = new SlaTimerState(
            started,
            new SlaDueDates(started.AddHours(4), started.AddHours(24), started.AddHours(240)),
            Guid.NewGuid(), SlaBreachStatus.OnTrack, null, 0);
        var now = started.AddHours(240).AddDays(2).AddHours(1);

        var dto = SlaCalculator.ToDto(Guid.NewGuid(), running: true, state, now, 0.2);

        Assert.Equal("Running", dto.State);
        Assert.Equal("Breached", dto.BreachStatus); // ไม่ใช้ค่าที่เก็บ (OnTrack) — คำนวณสด
        Assert.Equal(2, dto.DaysOverdue);
        Assert.True(dto.Resolution!.IsOverdue);
        Assert.True(dto.Resolution.RemainingSeconds < 0);
        Assert.True(dto.Acknowledge!.IsOverdue);
    }

    [Fact]
    public void ToDto_stopped_timer_freezes_status_and_never_reports_near_due()
    {
        var started = At(Mon, 0);
        var dates = new SlaDueDates(started.AddHours(4), started.AddHours(24), started.AddHours(240));
        var now = started.AddHours(230);

        var nearDue = SlaCalculator.ToDto(
            Guid.NewGuid(), running: false, new SlaTimerState(started, dates, Guid.NewGuid(), SlaBreachStatus.NearDue, null, 1), now, 0.2);
        var breached = SlaCalculator.ToDto(
            Guid.NewGuid(), running: false, new SlaTimerState(started, dates, Guid.NewGuid(), SlaBreachStatus.Breached, null, 2), now, 0.2);

        Assert.Equal("Stopped", nearDue.State);
        Assert.Equal("OnTrack", nearDue.BreachStatus);
        Assert.Equal("Breached", breached.BreachStatus);
        Assert.Equal(0, breached.DaysOverdue);
    }

    [Fact]
    public void NotStarted_dto_carries_the_blocked_reason_and_no_timer_data()
    {
        var id = Guid.NewGuid();

        var dto = SlaCalculator.NotStarted(id, SlaErrorCodes.CriticalityMissing);

        Assert.Equal(id, dto.InstanceId);
        Assert.Equal("NotStarted", dto.State);
        Assert.Equal(SlaErrorCodes.CriticalityMissing, dto.BlockedReason);
        Assert.Null(dto.Resolution);
        Assert.Empty(dto.PreviousDue);
    }

    // ------------------------------------------------------------ escalation matrix (FR-39/40 · BR-11)

    [Theory]
    [InlineData(SlaBreachStatus.OnTrack, 0, SlaEscalationLevel.None)]
    [InlineData(SlaBreachStatus.NearDue, 0, SlaEscalationLevel.NearDue)]
    [InlineData(SlaBreachStatus.Breached, 0, SlaEscalationLevel.Breached)]
    [InlineData(SlaBreachStatus.Breached, 2, SlaEscalationLevel.Breached)]
    [InlineData(SlaBreachStatus.Breached, 3, SlaEscalationLevel.Overdue)]
    [InlineData(SlaBreachStatus.Breached, 10, SlaEscalationLevel.Overdue)]
    public void Determine_maps_status_and_days_overdue_to_level(SlaBreachStatus status, int days, SlaEscalationLevel expected)
    {
        Assert.Equal(expected, SlaEscalationMatrix.Determine(status, days, overdueEscalationDays: 3));
    }

    [Fact]
    public void ShouldEscalate_is_monotonic_so_the_same_level_is_never_sent_twice()
    {
        Assert.True(SlaEscalationMatrix.ShouldEscalate(0, SlaEscalationLevel.NearDue));
        Assert.True(SlaEscalationMatrix.ShouldEscalate(1, SlaEscalationLevel.Breached));
        Assert.True(SlaEscalationMatrix.ShouldEscalate(2, SlaEscalationLevel.Overdue));

        Assert.False(SlaEscalationMatrix.ShouldEscalate(1, SlaEscalationLevel.NearDue));
        Assert.False(SlaEscalationMatrix.ShouldEscalate(2, SlaEscalationLevel.Breached));
        Assert.False(SlaEscalationMatrix.ShouldEscalate(3, SlaEscalationLevel.Overdue));
        Assert.False(SlaEscalationMatrix.ShouldEscalate(3, SlaEscalationLevel.Breached));
        Assert.False(SlaEscalationMatrix.ShouldEscalate(0, SlaEscalationLevel.None));
    }

    [Fact]
    public void Audience_adds_supervisors_only_when_overdue()
    {
        Assert.Equal(SlaAudience.None, SlaEscalationMatrix.AudienceFor(SlaEscalationLevel.None));
        Assert.False(SlaEscalationMatrix.AudienceFor(SlaEscalationLevel.NearDue).HasFlag(SlaAudience.Supervisors));
        Assert.False(SlaEscalationMatrix.AudienceFor(SlaEscalationLevel.Breached).HasFlag(SlaAudience.Supervisors));

        var overdue = SlaEscalationMatrix.AudienceFor(SlaEscalationLevel.Overdue);
        Assert.True(overdue.HasFlag(SlaAudience.Supervisors));
        Assert.True(overdue.HasFlag(SlaAudience.Assignee));
        Assert.True(overdue.HasFlag(SlaAudience.AssetOwners));
    }

    [Theory]
    [InlineData(RiskLevel.Critical, false, true)]
    [InlineData(RiskLevel.Low, true, true)]
    [InlineData(RiskLevel.High, false, false)]
    [InlineData(null, false, false)]
    public void IsUrgent_for_critical_or_kev(RiskLevel? level, bool kev, bool expected)
    {
        Assert.Equal(expected, SlaEscalationMatrix.IsUrgent(level, kev));
    }

    // ------------------------------------------------------------ options

    [Fact]
    public void Options_fall_back_to_defaults_for_out_of_range_values()
    {
        var options = new SlaOptions
        {
            MonitorIntervalMinutes = 0,
            StartupDelaySeconds = -1,
            NearDueThresholdPercent = 100,
            OverdueEscalationDays = 0,
            MaxInstancesPerRun = -5,
        };

        Assert.Equal(TimeSpan.FromMinutes(5), options.Interval);
        Assert.Equal(TimeSpan.FromSeconds(15), options.StartupDelay);
        Assert.Equal(0.2, options.NearDueFraction, 5);
        Assert.Equal(3, options.EffectiveOverdueEscalationDays);
        Assert.Equal(1000, options.EffectiveMaxInstancesPerRun);
    }

    [Fact]
    public void Options_accept_valid_values()
    {
        var options = new SlaOptions
        {
            MonitorIntervalMinutes = 10,
            StartupDelaySeconds = 0,
            NearDueThresholdPercent = 30,
            OverdueEscalationDays = 7,
            MaxInstancesPerRun = 50,
        };

        Assert.Equal(TimeSpan.FromMinutes(10), options.Interval);
        Assert.Equal(TimeSpan.Zero, options.StartupDelay);
        Assert.Equal(0.3, options.NearDueFraction, 5);
        Assert.Equal(7, options.EffectiveOverdueEscalationDays);
        Assert.Equal(50, options.EffectiveMaxInstancesPerRun);
    }
}
