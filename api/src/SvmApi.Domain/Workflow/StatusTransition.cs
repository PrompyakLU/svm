using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Domain.Workflow;

/// <summary>config_statusTransition — state machine แบบ table-driven (D3-4). FR-23, BR-27, EX-55/56.</summary>
public class StatusTransition
{
    public Guid Id { get; set; }
    public VulnerabilityStatus FromStatus { get; set; }
    public VulnerabilityStatus ToStatus { get; set; }
    public string? RequiredPermission { get; set; }
    public bool RequiresEvidence { get; set; }
    public bool RequiresApproval { get; set; }
    public bool IsActive { get; set; } = true;
}
