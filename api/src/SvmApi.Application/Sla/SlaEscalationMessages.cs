using System.Globalization;

namespace SvmApi.Application.Sla;

/// <summary>เนื้อหาแจ้งเตือนหนึ่งรายการที่สร้างจาก <see cref="SlaEscalation"/>.</summary>
public sealed record SlaEscalationMessage(string Type, string Title, string Message, bool Urgent);

/// <summary>
/// สร้างประเภท/หัวข้อ/ข้อความของการแจ้งเตือน SLA แบบ pure (US-026 · FR-39..41).
/// ข้อความเป็นภาษาไทยเหมือนการแจ้งเตือนอื่น ๆ และ<b>ไม่มีข้อมูลส่วนบุคคลหรือ secret</b>:
/// ใช้เฉพาะระดับความเสี่ยง จำนวนวัน และวันครบกำหนด (UTC) — ไม่ใส่ชื่อผู้ใช้ ชื่อช่องโหว่ หรือชื่อ asset.
/// </summary>
public static class SlaEscalationMessages
{
    public const string TypeNearDue = "SLA_NEAR_DUE";
    public const string TypeBreach = "SLA_BREACH";
    public const string TypeOverdue = "SLA_OVERDUE";

    /// <summary>คืน null เมื่อไม่ควรแจ้ง (<see cref="SlaEscalationLevel.None"/> หรือค่านอกช่วง).</summary>
    public static SlaEscalationMessage? Build(SlaEscalation escalation)
    {
        ArgumentNullException.ThrowIfNull(escalation);

        var risk = string.IsNullOrWhiteSpace(escalation.RiskLevel) ? "-" : escalation.RiskLevel.Trim();
        var due = escalation.ResolutionDue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var days = Math.Max(escalation.DaysOverdue, 0);

        return escalation.Level switch
        {
            SlaEscalationLevel.NearDue => new SlaEscalationMessage(
                TypeNearDue,
                "ช่องโหว่ใกล้ครบกำหนด SLA",
                $"ช่องโหว่ระดับ {risk} ใกล้ครบกำหนดแก้ไขตาม SLA (กำหนด {due} UTC) — โปรดเร่งดำเนินการ",
                escalation.IsUrgent),

            SlaEscalationLevel.Breached => new SlaEscalationMessage(
                TypeBreach,
                "ช่องโหว่เกินกำหนด SLA",
                $"ช่องโหว่ระดับ {risk} เกินกำหนดแก้ไขตาม SLA แล้ว (กำหนด {due} UTC) — โปรดดำเนินการโดยด่วน",
                escalation.IsUrgent),

            SlaEscalationLevel.Overdue => new SlaEscalationMessage(
                TypeOverdue,
                "ช่องโหว่เกิน SLA ต่อเนื่อง — ยกระดับ",
                $"ช่องโหว่ระดับ {risk} เกินกำหนด SLA มาแล้ว {days} วัน (กำหนด {due} UTC) — ยกระดับเพื่อติดตาม",
                escalation.IsUrgent),

            _ => null,
        };
    }
}
