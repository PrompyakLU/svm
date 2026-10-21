using SvmApi.Domain.Access;
using SvmApi.Domain.Common;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Domain.Assignments;

public enum RemediationTaskStatus
{
    Open,
    InProgress,
    Blocked,
    Completed,
}

/// <summary>trans_remediationTask — งานแก้ไข (plan + progress). FR-27/28/29, EX-63, US-021.</summary>
public class RemediationTask : AuditableEntity
{
    public Guid InstanceId { get; set; }
    public VulnerabilityInstance Instance { get; set; } = null!;

    public Guid? OwnerUserId { get; set; }
    public User? OwnerUser { get; set; }
    public string? OwnerTeam { get; set; }

    public DateTime DueDate { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public string Recommendation { get; set; } = string.Empty;

    public string? RootCause { get; set; }
    public string? ActionPlan { get; set; }
    public DateTime? TargetDate { get; set; }
    public string? ChangeReference { get; set; }

    public int ProgressPercent { get; set; }
    public RemediationTaskStatus Status { get; set; } = RemediationTaskStatus.Open;
    public string? Obstacle { get; set; }

    public ICollection<Evidence> Evidences { get; set; } = [];
}
