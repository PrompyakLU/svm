using SvmApi.Domain.Common;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Domain.Remediation;

/// <summary>สถานะของงานแก้ไข (ใช้ชื่อ RemediationTaskStatus เพื่อไม่ชน System.Threading.Tasks.TaskStatus).</summary>
public enum RemediationTaskStatus
{
    Open,
    InProgress,
    Blocked,
    Completed,
}

/// <summary>trans_remediationTask — งานแก้ไข (plan + progress). FR-27/28/29, EX-63.</summary>
public class RemediationTask : AuditableEntity
{
    public Guid InstanceId { get; set; }
    public VulnerabilityInstance Instance { get; set; } = default!;

    public Guid? OwnerUserId { get; set; }
    public string? OwnerTeam { get; set; }

    public DateTime DueDate { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public string Recommendation { get; set; } = default!;

    public string? RootCause { get; set; }
    public string? ActionPlan { get; set; }
    /// <summary>วันที่เป้าหมาย — ต้องไม่เกิน SLA (EX-63).</summary>
    public DateTime? TargetDate { get; set; }
    public string? ChangeReference { get; set; }

    public int ProgressPercent { get; set; }
    public RemediationTaskStatus Status { get; set; } = RemediationTaskStatus.Open;
    public string? Obstacle { get; set; }

    public ICollection<Evidence> Evidences { get; set; } = new List<Evidence>();
}
