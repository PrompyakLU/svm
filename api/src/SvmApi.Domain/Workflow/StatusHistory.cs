using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Domain.Workflow;

/// <summary>trans_statusHistory — ประวัติการเปลี่ยนสถานะของ instance. FR-22/23, BR-27.</summary>
public class StatusHistory
{
    public Guid Id { get; set; }

    public Guid InstanceId { get; set; }
    public VulnerabilityInstance Instance { get; set; } = default!;

    public VulnerabilityStatus FromStatus { get; set; }
    public VulnerabilityStatus ToStatus { get; set; }
    public string? Reason { get; set; }

    public Guid ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
}
