namespace SvmApi.Domain.Sla;

/// <summary>config_businessCalendar — วันทำงาน สำหรับคำนวณ SLA due date. EX-67, US-024.</summary>
public class BusinessCalendar
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public bool IsWorkingDay { get; set; }
    public string? Description { get; set; }
}
