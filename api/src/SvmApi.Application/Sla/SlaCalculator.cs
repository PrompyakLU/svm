using System.Text.Encodings.Web;
using System.Text.Json;
using SvmApi.Application.Assets;
using SvmApi.Domain.Assets;
using SvmApi.Domain.Sla;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Application.Sla;

/// <summary>ปัจจัยของ instance ที่ใช้เลือก SLA policy.</summary>
public sealed record SlaSubject(RiskLevel RiskLevel, AssetCriticality? Criticality, bool InternetExposure, bool IsKev);

/// <summary>แถว <c>config_slaPolicy</c> ที่ตัดมาเฉพาะที่ใช้คำนวณ (ไม่ผูก EF).</summary>
public sealed record SlaPolicyCandidate(
    Guid Id,
    RiskLevel RiskLevel,
    AssetCriticality? AssetCriticality,
    bool? InternetExposure,
    bool KevOnly,
    int AcknowledgeHours,
    int StartHours,
    int ResolveHours,
    bool IsActive);

/// <summary>due date ทั้งสามจุด (UTC).</summary>
public sealed record SlaDueDates(DateTime AcknowledgeDue, DateTime RemediationStartDue, DateTime ResolutionDue);

public sealed record SlaDueResult(SlaDueDates? Dates, string? ErrorCode)
{
    public bool Succeeded => Dates is not null;
}

/// <summary>ค่าของ timer ที่เก็บอยู่ (ไม่ผูก EF) สำหรับ recalc / แปลงเป็น DTO.</summary>
public sealed record SlaTimerState(
    DateTime StartedAt,
    SlaDueDates Dates,
    Guid PolicyId,
    SlaBreachStatus StoredBreach,
    string? PreviousDueJson,
    int EscalationLevel);

public sealed record SlaRecalcResult(bool Changed, SlaDueDates Dates, Guid PolicyId, string? PreviousDueJson);

/// <summary>
/// business calendar (<c>config_businessCalendar</c>) ในหน่วยความจำ. วันที่ไม่มีแถว = ไม่ทราบ (EX-67) ไม่ใช่วันหยุด.
/// </summary>
public sealed class SlaCalendar
{
    private readonly Dictionary<DateOnly, bool> _days;

    public SlaCalendar(IEnumerable<KeyValuePair<DateOnly, bool>> days)
    {
        ArgumentNullException.ThrowIfNull(days);
        _days = new Dictionary<DateOnly, bool>();
        foreach (var (date, isWorking) in days)
        {
            _days[date] = isWorking;
        }
    }

    public bool HasWorkingDay => _days.ContainsValue(true);

    /// <summary>true/false = วันทำงาน/วันหยุด; null = ปฏิทินไม่ครอบคลุมวันนี้.</summary>
    public bool? IsWorkingDay(DateOnly date) => _days.TryGetValue(date, out var working) ? working : null;
}

/// <summary>
/// ตรรกะ SLA ล้วน (ไม่แตะ DB/เวลาปัจจุบัน): เลือก policy ที่เจาะจงที่สุด, คำนวณ due date บน business calendar,
/// ตรวจ breach/near-due, recalc เก็บค่าเดิม + เหตุผล (FR-35..37, BR-05/08/09/10).
/// <para>
/// <b>นิยามเวลา</b>: ชั่วโมงใน policy คือเวลาจริงที่เดินเฉพาะ "วันทำงาน" ตาม calendar (วันหยุดถูกข้ามทั้งวัน, วันทำงานนับครบ 24 ชม.),
/// ทุกค่าเป็น UTC และตัดวันที่เที่ยงคืน UTC. เช่น Resolve 168 ชม. = 7 วันทำงาน.
/// </para>
/// </summary>
public static class SlaCalculator
{
    public const int MaxPolicyHours = 43_800; // 5 ปี
    public const int MaxPreviousDueEntries = 20;
    private const int MaxScanDays = 5_000;

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>สถานะที่ SLA หยุดนับ (งานจบ/ยกเว้น). PendingVerification ยังนับต่อจนกว่าจะ Resolved.</summary>
    public static readonly IReadOnlyList<VulnerabilityStatus> StoppedStatuses =
    [
        VulnerabilityStatus.Resolved,
        VulnerabilityStatus.Closed,
        VulnerabilityStatus.FalsePositive,
        VulnerabilityStatus.RiskAccepted,
        VulnerabilityStatus.ExceptionApproved,
        VulnerabilityStatus.Archived,
    ];

    public static bool IsTimerRunning(VulnerabilityStatus status) => !StoppedStatuses.Contains(status);

    // ------------------------------------------------------------------ eligibility (BR-05 / EX-65)

    /// <summary>
    /// ตรวจว่าคำนวณ SLA ขั้นสุดท้ายได้หรือไม่. คืน error code หรือ null.
    /// ลำดับ: SLA หยุดนับแล้ว → ไม่มี risk level (EX-65) → asset Active Prod ไม่มี criticality (BR-05) — ไม่สร้าง timer ทั้งสามกรณี.
    /// </summary>
    public static string? CheckEligibility(
        VulnerabilityStatus instanceStatus,
        RiskLevel? riskLevel,
        AssetEnvironment environment,
        AssetStatus assetStatus,
        AssetCriticality? criticality)
    {
        if (!IsTimerRunning(instanceStatus))
        {
            return SlaErrorCodes.NotActive;
        }

        if (riskLevel is null)
        {
            return SlaErrorCodes.RiskLevelMissing;
        }

        return AssetRules.IsCriticalityMissing(environment, assetStatus, criticality)
            ? SlaErrorCodes.CriticalityMissing
            : null;
    }

    // ------------------------------------------------------------------ policy selection (BR-08)

    /// <summary>
    /// เลือก policy ที่ active + ตรงเงื่อนไข และ <b>เจาะจงที่สุด</b> (จำนวนมิติที่ระบุ: criticality / internet exposure / KEV-only มากกว่าชนะ).
    /// เสมอกัน = เลือกอันที่เข้มกว่า (ResolveHours → StartHours → AcknowledgeHours น้อยกว่า) แล้วใช้ Id เพื่อให้ผลคงที่.
    /// ไม่มีที่ตรง = null (EX-66).
    /// </summary>
    public static SlaPolicyCandidate? SelectPolicy(IEnumerable<SlaPolicyCandidate> policies, SlaSubject subject)
    {
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(subject);

        SlaPolicyCandidate? best = null;
        var bestScore = -1;

        foreach (var policy in policies)
        {
            if (!policy.IsActive || policy.RiskLevel != subject.RiskLevel)
            {
                continue;
            }

            if (policy.AssetCriticality is { } criticality && criticality != subject.Criticality)
            {
                continue;
            }

            if (policy.InternetExposure is { } exposure && exposure != subject.InternetExposure)
            {
                continue;
            }

            if (policy.KevOnly && !subject.IsKev)
            {
                continue;
            }

            var score = (policy.AssetCriticality is null ? 0 : 1)
                        + (policy.InternetExposure is null ? 0 : 1)
                        + (policy.KevOnly ? 1 : 0);

            if (best is null || score > bestScore || (score == bestScore && IsStricter(policy, best)))
            {
                best = policy;
                bestScore = score;
            }
        }

        return best;
    }

    private static bool IsStricter(SlaPolicyCandidate candidate, SlaPolicyCandidate current)
    {
        var byResolve = candidate.ResolveHours.CompareTo(current.ResolveHours);
        if (byResolve != 0)
        {
            return byResolve < 0;
        }

        var byStart = candidate.StartHours.CompareTo(current.StartHours);
        if (byStart != 0)
        {
            return byStart < 0;
        }

        var byAck = candidate.AcknowledgeHours.CompareTo(current.AcknowledgeHours);
        return byAck != 0 ? byAck < 0 : candidate.Id.CompareTo(current.Id) < 0;
    }

    public static bool IsPolicyValid(SlaPolicyCandidate policy)
        => policy.AcknowledgeHours is >= 1 and <= MaxPolicyHours
           && policy.StartHours is >= 1 and <= MaxPolicyHours
           && policy.ResolveHours is >= 1 and <= MaxPolicyHours;

    // ------------------------------------------------------------------ due dates (FR-35 / EX-66 / EX-67)

    /// <summary>
    /// คำนวณ due date ทั้งสามจุดจาก <paramref name="startedAt"/> (UTC) บน business calendar.
    /// policy ผิดรูป = <see cref="SlaErrorCodes.PolicyInvalid"/>; calendar ไม่ครอบคลุม/ไม่มีวันทำงาน = <see cref="SlaErrorCodes.CalendarIncomplete"/>.
    /// </summary>
    public static SlaDueResult ComputeDueDates(DateTime startedAt, SlaPolicyCandidate policy, SlaCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(calendar);

        if (!IsPolicyValid(policy))
        {
            return new SlaDueResult(null, SlaErrorCodes.PolicyInvalid);
        }

        if (!calendar.HasWorkingDay)
        {
            return new SlaDueResult(null, SlaErrorCodes.CalendarIncomplete);
        }

        var start = AsUtc(startedAt);
        var ack = AddBusinessHours(start, policy.AcknowledgeHours, calendar);
        var begin = AddBusinessHours(start, policy.StartHours, calendar);
        var resolve = AddBusinessHours(start, policy.ResolveHours, calendar);

        return ack is null || begin is null || resolve is null
            ? new SlaDueResult(null, SlaErrorCodes.CalendarIncomplete)
            : new SlaDueResult(new SlaDueDates(ack.Value, begin.Value, resolve.Value), null);
    }

    /// <summary>
    /// บวก <paramref name="hours"/> ชั่วโมงเข้ากับ <paramref name="start"/> โดยข้ามวันที่ไม่ใช่วันทำงาน.
    /// คืน null เมื่อปฏิทินไม่ครอบคลุมวันที่ต้องผ่าน (EX-67).
    /// </summary>
    public static DateTime? AddBusinessHours(DateTime start, int hours, SlaCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (hours < 1)
        {
            return null;
        }

        var cursor = AsUtc(start);
        var remaining = TimeSpan.FromHours(hours);

        for (var scanned = 0; scanned <= MaxScanDays; scanned++)
        {
            var date = DateOnly.FromDateTime(cursor);
            var working = calendar.IsWorkingDay(date);
            if (working is null)
            {
                return null;
            }

            var nextMidnight = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            if (working.Value)
            {
                var available = nextMidnight - cursor;
                if (remaining <= available)
                {
                    return cursor + remaining;
                }

                remaining -= available;
            }

            cursor = nextMidnight;
        }

        return null;
    }

    // ------------------------------------------------------------------ breach (FR-36 / EX-68)

    /// <summary>
    /// สถานะ SLA ตาม <paramref name="resolutionDue"/>: เกินกำหนด = <see cref="SlaBreachStatus.Breached"/> (now &gt; due),
    /// เวลาที่เหลือ &lt;= <paramref name="nearDueFraction"/> ของช่วง started→due = <see cref="SlaBreachStatus.NearDue"/>, นอกนั้น OnTrack.
    /// </summary>
    public static SlaBreachStatus EvaluateBreach(DateTime startedAt, DateTime resolutionDue, DateTime now, double nearDueFraction)
    {
        if (now > resolutionDue)
        {
            return SlaBreachStatus.Breached;
        }

        var window = resolutionDue - startedAt;
        if (window > TimeSpan.Zero && (resolutionDue - now) <= window * nearDueFraction)
        {
            return SlaBreachStatus.NearDue;
        }

        return SlaBreachStatus.OnTrack;
    }

    /// <summary>จำนวนวันเต็มที่เกินกำหนด (0 ถ้ายังไม่เกิน).</summary>
    public static int DaysOverdue(DateTime due, DateTime now)
        => now <= due ? 0 : (int)Math.Floor((now - due).TotalDays);

    public static long RemainingSeconds(DateTime due, DateTime now)
        => (long)Math.Floor((due - now).TotalSeconds);

    // ------------------------------------------------------------------ recalc (FR-37 / BR-10)

    /// <summary>true เมื่อ <paramref name="candidate"/> รุนแรงกว่า <paramref name="current"/> (enum เรียง Critical=0 … Informational=4).</summary>
    public static bool IsMoreSevere(RiskLevel candidate, RiskLevel current) => candidate < current;

    /// <summary>
    /// คำนวณใหม่โดย <b>เก็บค่าเดิม + เหตุผล</b> ใน <c>previousDue</c> (JSON, ใหม่สุดท้ายสุด, สูงสุด <see cref="MaxPreviousDueEntries"/> รายการ).
    /// BR-10: ถ้า risk สูงขึ้น (<paramref name="riskIncreased"/>) ใช้ due date ที่เข้มกว่า (เร็วกว่า) ของค่าเดิม/ค่าใหม่ในแต่ละจุด;
    /// กรณีอื่น (risk เท่าเดิม/ลดลง/policy เปลี่ยน) ใช้ค่าที่คำนวณใหม่ตามจริง.
    /// ถ้าผลลัพธ์ due date และ policy เหมือนเดิมทุกจุด = <c>Changed=false</c> และ <b>ไม่</b> เพิ่มประวัติ.
    /// </summary>
    public static SlaRecalcResult Recalculate(
        SlaTimerState current,
        SlaDueDates computed,
        Guid computedPolicyId,
        bool riskIncreased,
        SlaTrigger trigger,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(computed);

        var target = riskIncreased ? Stricter(current.Dates, computed) : computed;

        if (target == current.Dates && computedPolicyId == current.PolicyId)
        {
            return new SlaRecalcResult(false, current.Dates, current.PolicyId, current.PreviousDueJson);
        }

        var history = ParsePreviousDue(current.PreviousDueJson).ToList();
        history.Add(new SlaPreviousDue(
            AsUtc(now), trigger.ToCode(), current.PolicyId,
            current.Dates.AcknowledgeDue, current.Dates.RemediationStartDue, current.Dates.ResolutionDue));
        if (history.Count > MaxPreviousDueEntries)
        {
            history.RemoveRange(0, history.Count - MaxPreviousDueEntries);
        }

        return new SlaRecalcResult(true, target, computedPolicyId, JsonSerializer.Serialize(history, Json));
    }

    private static SlaDueDates Stricter(SlaDueDates a, SlaDueDates b)
        => new(
            Min(a.AcknowledgeDue, b.AcknowledgeDue),
            Min(a.RemediationStartDue, b.RemediationStartDue),
            Min(a.ResolutionDue, b.ResolutionDue));

    private static DateTime Min(DateTime a, DateTime b) => a <= b ? a : b;

    /// <summary>อ่าน <c>previousDue</c> (JSON). ข้อมูลเสีย/ว่าง = รายการว่าง (ไม่ throw).</summary>
    public static IReadOnlyList<SlaPreviousDue> ParsePreviousDue(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<SlaPreviousDue>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ------------------------------------------------------------------ DTO

    /// <summary>DTO ของ instance ที่ยังไม่มี timer. <paramref name="blockedReason"/> = สาเหตุที่ยังเริ่มนับไม่ได้ (หรือ null = รอ monitor/risk recalculation สร้าง).</summary>
    public static SlaTimerDto NotStarted(Guid instanceId, string? blockedReason)
        => new(instanceId, "NotStarted", blockedReason, null, null, null, null, null, null, 0, 0, []);

    /// <summary>
    /// แปลง timer เป็น DTO. Running = คำนวณ breach/เวลาที่เหลือสดจาก <paramref name="now"/>;
    /// Stopped = แช่แข็งสถานะที่เก็บไว้ (NearDue แสดงเป็น OnTrack) และไม่นับ escalation.
    /// </summary>
    public static SlaTimerDto ToDto(Guid instanceId, bool running, SlaTimerState state, DateTime now, double nearDueFraction)
    {
        ArgumentNullException.ThrowIfNull(state);

        var breach = running
            ? EvaluateBreach(state.StartedAt, state.Dates.ResolutionDue, now, nearDueFraction)
            : (state.StoredBreach == SlaBreachStatus.NearDue ? SlaBreachStatus.OnTrack : state.StoredBreach);

        return new SlaTimerDto(
            instanceId,
            running ? "Running" : "Stopped",
            null,
            breach.ToString(),
            AsUtc(state.StartedAt),
            state.PolicyId,
            Milestone(state.Dates.AcknowledgeDue, now),
            Milestone(state.Dates.RemediationStartDue, now),
            Milestone(state.Dates.ResolutionDue, now),
            running ? DaysOverdue(state.Dates.ResolutionDue, now) : 0,
            state.EscalationLevel,
            ParsePreviousDue(state.PreviousDueJson));
    }

    private static SlaMilestoneDto Milestone(DateTime due, DateTime now)
        => new(AsUtc(due), RemainingSeconds(due, now), now > due);

    // ------------------------------------------------------------------ helpers

    /// <summary>EF คืน datetime2 เป็น Kind=Unspecified — ถือเป็น UTC เสมอ.</summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

/// <summary>
/// escalation matrix แบบ table-driven (US-026 · FR-39/40 · BR-11): สถานะ SLA + จำนวนวันที่เกิน → ระดับ → ผู้รับ.
/// ระดับ escalate ได้แบบ monotonic เท่านั้น (ไม่ส่งซ้ำระดับเดิม) — ใช้ <see cref="ShouldEscalate"/>.
/// </summary>
public static class SlaEscalationMatrix
{
    public static SlaEscalationLevel Determine(SlaBreachStatus status, int daysOverdue, int overdueEscalationDays)
        => status switch
        {
            SlaBreachStatus.Breached => daysOverdue >= overdueEscalationDays ? SlaEscalationLevel.Overdue : SlaEscalationLevel.Breached,
            SlaBreachStatus.NearDue => SlaEscalationLevel.NearDue,
            _ => SlaEscalationLevel.None,
        };

    public static SlaAudience AudienceFor(SlaEscalationLevel level) => level switch
    {
        SlaEscalationLevel.NearDue => SlaAudience.Assignee | SlaAudience.AssetOwners,
        SlaEscalationLevel.Breached => SlaAudience.Assignee | SlaAudience.AssetOwners,
        SlaEscalationLevel.Overdue => SlaAudience.Assignee | SlaAudience.AssetOwners | SlaAudience.Supervisors,
        _ => SlaAudience.None,
    };

    /// <summary>true เฉพาะเมื่อระดับที่คำนวณได้ "สูงกว่า" ระดับที่เคยส่งแล้ว (idempotent).</summary>
    public static bool ShouldEscalate(int storedLevel, SlaEscalationLevel computed) => (int)computed > storedLevel;

    /// <summary>Critical หรือ KEV → ช่องทางเร่งด่วน (FR-41).</summary>
    public static bool IsUrgent(RiskLevel? riskLevel, bool isKev) => isKev || riskLevel == RiskLevel.Critical;
}
