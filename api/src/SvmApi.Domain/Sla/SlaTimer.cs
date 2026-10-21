using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Domain.Sla;

public enum SlaBreachStatus
{
    OnTrack,
    NearDue,
    Breached,
}

/// <summary>trans_slaTimer — ตัวจับเวลา SLA ของแต่ละ instance. BR-09/10, FR-37, EX-68, US-025.</summary>
public class SlaTimer
{
    public Guid Id { get; set; }

    public Guid InstanceId { get; set; }
    public VulnerabilityInstance Instance { get; set; } = default!;

    public DateTime AcknowledgeDue { get; set; }
    public DateTime RemediationStartDue { get; set; }
    public DateTime ResolutionDue { get; set; }
    public DateTime StartedAt { get; set; }
    public SlaBreachStatus BreachStatus { get; set; } = SlaBreachStatus.OnTrack;

    public Guid PolicyId { get; set; }
    public SlaPolicy Policy { get; set; } = default!;

    /// <summary>JSON — ประวัติ due date เดิมตอน recalc (เก็บค่าเดิม+เหตุผล). FR-37.</summary>
    public string? PreviousDue { get; set; }

    /// <summary>
    /// ระดับ escalation สูงสุดที่ส่งไปแล้ว (0 = ยังไม่เคย, 1 = NearDue, 2 = Breached, 3 = Overdue) — ใช้ทำ escalation แบบ idempotent
    /// (ไม่ส่งซ้ำระดับเดิม). ถูก reset เมื่อสถานะดีขึ้น (เช่น recalc ขยาย due date). FR-39/40, BR-11, EX-70.
    /// </summary>
    public int EscalationLevel { get; set; }

    /// <summary>เวลา (UTC) ที่ escalate ล่าสุด.</summary>
    public DateTime? LastEscalatedAt { get; set; }
}
