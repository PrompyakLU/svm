using SvmApi.Domain.Sla;

namespace SvmApi.Application.Sla;

/// <summary>รหัส error ของ SlaModule (C9 · US-024/025/026 · EX-65..70).</summary>
public static class SlaErrorCodes
{
    public const string InstanceNotFound = "INSTANCE_NOT_FOUND";

    /// <summary>EX-65: instance ยังไม่มี risk level จึงคำนวณ SLA ไม่ได้.</summary>
    public const string RiskLevelMissing = "SLA_RISK_LEVEL_MISSING";

    /// <summary>EX-66: ไม่พบ SLA policy ที่ตรง.</summary>
    public const string PolicyNotFound = "SLA_POLICY_NOT_FOUND";

    /// <summary>policy มีจำนวนชั่วโมงไม่ถูกต้อง (&lt;= 0 หรือเกินขอบเขต).</summary>
    public const string PolicyInvalid = "SLA_POLICY_INVALID";

    /// <summary>EX-67: business calendar ไม่ครอบคลุมช่วงที่ต้องใช้ (หรือไม่มีวันทำงานเลย).</summary>
    public const string CalendarIncomplete = "SLA_CALENDAR_INCOMPLETE";

    /// <summary>BR-05/EX-28: asset ใน Active Prod ไม่มี criticality — กันไม่ให้คำนวณ SLA ขั้นสุดท้าย (ไม่สร้าง timer).</summary>
    public const string CriticalityMissing = "ASSET_CRITICALITY_MISSING";

    /// <summary>instance อยู่ในสถานะที่ SLA หยุดนับแล้ว (Resolved/Closed/FalsePositive/RiskAccepted/ExceptionApproved/Archived).</summary>
    public const string NotActive = "SLA_NOT_ACTIVE";

    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
}

/// <summary>เหตุผลที่คำนวณ SLA ใหม่ (เก็บใน <c>previousDue</c> เป็นรหัส ไม่ใช่ข้อความอิสระ — ไม่มี PII). FR-37, BR-10.</summary>
public enum SlaTrigger
{
    /// <summary>สร้าง timer ครั้งแรก (BR-09).</summary>
    Initial,

    /// <summary>risk level ของ instance เปลี่ยน (US-025).</summary>
    RiskLevelChanged,

    /// <summary>SLA policy ที่ใช้อยู่ถูกแก้/เปลี่ยน.</summary>
    PolicyChanged,

    /// <summary>ปัจจัยของ asset เปลี่ยน (criticality/internet exposure/KEV).</summary>
    AssetChanged,

    /// <summary>สั่งคำนวณใหม่ด้วยตนเอง.</summary>
    Manual,
}

public static class SlaTriggerCodes
{
    public static string ToCode(this SlaTrigger trigger) => trigger switch
    {
        SlaTrigger.Initial => "INITIAL",
        SlaTrigger.RiskLevelChanged => "RISK_LEVEL_CHANGED",
        SlaTrigger.PolicyChanged => "POLICY_CHANGED",
        SlaTrigger.AssetChanged => "ASSET_CHANGED",
        SlaTrigger.Manual => "MANUAL",
        _ => "UNKNOWN",
    };
}

/// <summary>ระดับ escalation (ตารางใน <see cref="SlaEscalationMatrix"/>). ค่าตัวเลขเรียงจากเบาไปหนัก และเก็บใน <c>escalationLevel</c>.</summary>
public enum SlaEscalationLevel
{
    None = 0,

    /// <summary>ใกล้ครบกำหนด → reminder (FR-39).</summary>
    NearDue = 1,

    /// <summary>เกิน SLA → แจ้ง owner (FR-40).</summary>
    Breached = 2,

    /// <summary>เกิน SLA ตั้งแต่ N วัน → ยกระดับถึงผู้บังคับบัญชา (FR-40, BR-11).</summary>
    Overdue = 3,
}

/// <summary>ผู้รับของแต่ละระดับ escalation (table-driven).</summary>
[Flags]
public enum SlaAudience
{
    None = 0,
    Assignee = 1,
    AssetOwners = 2,
    Supervisors = 4,
}

/// <summary>ตัวเลือกของ SlaModule + <c>SlaMonitorJob</c> (section <c>Sla</c> ใน appsettings). ค่านอกช่วงใช้ค่าเริ่มต้น (ไม่ throw).</summary>
public sealed class SlaOptions
{
    public const string SectionName = "Sla";

    public const int DefaultIntervalMinutes = 5;
    public const int DefaultStartupDelaySeconds = 15;
    public const int DefaultNearDuePercent = 20;
    public const int DefaultOverdueEscalationDays = 3;
    public const int DefaultMaxInstancesPerRun = 1000;

    /// <summary>เปิด/ปิด background job (ปิดได้ใน environment ที่ไม่ต้องการ เช่น หลายอินสแตนซ์จนกว่าจะมี distributed lock).</summary>
    public bool MonitorEnabled { get; set; } = true;

    public int MonitorIntervalMinutes { get; set; } = DefaultIntervalMinutes;
    public int StartupDelaySeconds { get; set; } = DefaultStartupDelaySeconds;

    /// <summary>"ใกล้ครบกำหนด" = เวลาที่เหลือ &lt;= เปอร์เซ็นต์นี้ของช่วง StartedAt → ResolutionDue.</summary>
    public int NearDueThresholdPercent { get; set; } = DefaultNearDuePercent;

    /// <summary>เกิน SLA ตั้งแต่กี่วันจึงยกระดับถึงผู้บังคับบัญชา.</summary>
    public int OverdueEscalationDays { get; set; } = DefaultOverdueEscalationDays;

    public int MaxInstancesPerRun { get; set; } = DefaultMaxInstancesPerRun;

    public TimeSpan Interval
        => TimeSpan.FromMinutes(MonitorIntervalMinutes is >= 1 and <= 1440 ? MonitorIntervalMinutes : DefaultIntervalMinutes);

    public TimeSpan StartupDelay
        => TimeSpan.FromSeconds(StartupDelaySeconds is >= 0 and <= 3600 ? StartupDelaySeconds : DefaultStartupDelaySeconds);

    public double NearDueFraction
        => (NearDueThresholdPercent is > 0 and < 100 ? NearDueThresholdPercent : DefaultNearDuePercent) / 100.0;

    public int EffectiveOverdueEscalationDays
        => OverdueEscalationDays is >= 1 and <= 365 ? OverdueEscalationDays : DefaultOverdueEscalationDays;

    public int EffectiveMaxInstancesPerRun
        => MaxInstancesPerRun is >= 1 and <= 100_000 ? MaxInstancesPerRun : DefaultMaxInstancesPerRun;
}

/// <summary>จุดตัดเวลา (milestone) หนึ่งจุดของ SLA พร้อมเวลาที่เหลือ ณ ตอนอ่าน.</summary>
public sealed record SlaMilestoneDto(DateTime DueAt, long RemainingSeconds, bool IsOverdue);

/// <summary>ประวัติ due date เดิมก่อน recalc (เก็บค่าเดิม + เหตุผล). FR-37, BR-10.</summary>
public sealed record SlaPreviousDue(
    DateTime RecordedAt,
    string Reason,
    Guid PolicyId,
    DateTime AcknowledgeDue,
    DateTime RemediationStartDue,
    DateTime ResolutionDue);

/// <summary>
/// สถานะ SLA ของ instance (<c>GET /api/v1/instances/{id}/sla</c>).
/// <c>State</c>: <c>Running</c> | <c>Stopped</c> (instance ปิด/ยกเว้นแล้ว) | <c>NotStarted</c> (ยังไม่มี timer — ดู <c>BlockedReason</c>).
/// <c>BreachStatus</c> ของ Running คำนวณสดจากเวลาปัจจุบัน (ไม่ได้อ่านจากค่าที่เก็บ) เพื่อไม่ล้าหลัง job.
/// </summary>
public sealed record SlaTimerDto(
    Guid InstanceId,
    string State,
    string? BlockedReason,
    string? BreachStatus,
    DateTime? StartedAt,
    Guid? PolicyId,
    SlaMilestoneDto? Acknowledge,
    SlaMilestoneDto? RemediationStart,
    SlaMilestoneDto? Resolution,
    int DaysOverdue,
    int EscalationLevel,
    IReadOnlyList<SlaPreviousDue> PreviousDue);

public enum SlaOutcome
{
    Success,
    NotFound,

    /// <summary>มีอยู่แต่อยู่นอก data scope (EX-07): ตอบ 404 + บันทึก denied-access.</summary>
    OutOfScope,

    /// <summary>คำนวณไม่ได้ด้วยข้อมูล/config ปัจจุบัน (EX-65/66/67, BR-05) → 422.</summary>
    Unprocessable,

    /// <summary>ชนกันตอนบันทึก (เช่น สร้าง timer พร้อมกัน) → 409.</summary>
    Conflict,
}

public sealed record SlaResult<T>(SlaOutcome Outcome, T? Value = default, string? Code = null, string? Message = null)
{
    public bool Succeeded => Outcome == SlaOutcome.Success;

    public static SlaResult<T> Ok(T value) => new(SlaOutcome.Success, value);

    public static SlaResult<T> Fail(SlaOutcome outcome, string code, string? message = null)
        => new(outcome, default, code, message);
}

/// <summary>
/// SlaModule (C9 · US-024/025/026 · FR-35..40 · BR-05/08/09/10): คำนวณ due date จาก <c>config_slaPolicy</c> + <c>config_businessCalendar</c>,
/// จัดการ timer และ recalc (เก็บค่าเดิม + เหตุผล). การเขียนทุกครั้งอยู่ใน transaction เดียวกับ audit.
/// </summary>
public interface ISlaService
{
    /// <summary>อ่านสถานะ SLA ของ instance (ผ่าน data scope · fail-closed · ไม่เขียนข้อมูล).</summary>
    Task<SlaResult<SlaTimerDto>> GetAsync(Guid instanceId, CancellationToken cancellationToken);

    /// <summary>
    /// สร้าง timer (ถ้ายังไม่มี) หรือคำนวณใหม่ (ถ้ามี — เก็บ due date เดิม + เหตุผลใน <c>previousDue</c>).
    /// เป็นงานระดับระบบ: <b>ไม่</b> กรอง data scope (ผู้เรียก เช่น risk recalculation / SlaMonitorJob รับผิดชอบสิทธิ์มาก่อน).
    /// BR-05: asset Active Prod ไม่มี criticality → <see cref="SlaErrorCodes.CriticalityMissing"/> และไม่สร้าง timer.
    /// ไม่มีการเปลี่ยนแปลง (due date/policy เดิม) = สำเร็จโดยไม่เขียนอะไร.
    /// </summary>
    Task<SlaResult<SlaTimerDto>> CalculateAsync(Guid instanceId, SlaTrigger trigger, CancellationToken cancellationToken);

    /// <summary>
    /// เหมือน <see cref="CalculateAsync"/> แต่ <b>ไม่ throw</b> (ยกเว้นยกเลิก) — ใช้เป็น hook จาก flow อื่น (เช่น risk recalculation)
    /// ที่ล้มเหลวของ SLA ต้องไม่ทำให้งานหลักล้ม. คืน true เมื่อ timer อยู่ในสถานะที่ถูกต้องหลังเรียก.
    /// </summary>
    Task<bool> TryCalculateAsync(Guid instanceId, SlaTrigger trigger, CancellationToken cancellationToken);
}

/// <summary>เหตุการณ์ escalation ที่ <c>SlaMonitorJob</c> ส่งให้ผู้แจ้งเตือน. ไม่มีข้อมูลส่วนบุคคล (เฉพาะ id/ระดับ/ตัวเลข).</summary>
public sealed record SlaEscalation(
    Guid InstanceId,
    Guid TimerId,
    SlaEscalationLevel Level,
    string RiskLevel,
    bool IsUrgent,
    int DaysOverdue,
    DateTime ResolutionDue,
    IReadOnlyList<Guid> RecipientUserIds);

/// <summary>
/// ช่องทางส่ง escalation ออกจาก SlaModule (seam ไปยัง NotificationModule · US-026 · FR-38..42).
/// เรียกหลัง commit ระดับ escalation แล้ว (at-most-once ต่อระดับ); implementation ต้อง idempotent พอสมควรและไม่ควร throw —
/// ถ้า throw job จะ log แล้วไปรายการถัดไป (ไม่ retry ระดับเดิม; retry การส่งเป็นหน้าที่ของ NotificationModule ตาม FR-42).
/// </summary>
public interface ISlaEscalationNotifier
{
    Task NotifyAsync(SlaEscalation escalation, CancellationToken cancellationToken);
}

/// <summary>สรุปผลหนึ่งรอบของ monitor (ใช้ log + test).</summary>
public sealed record SlaMonitorSummary(
    int TimersCreated,
    int TimersRecalculated,
    int BreachStatusUpdated,
    int Escalated,
    int Skipped,
    int Failed);
