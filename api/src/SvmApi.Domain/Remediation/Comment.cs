using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Domain.Remediation;

/// <summary>trans_comment — comment และ mention บน instance. FR-30.</summary>
public class Comment
{
    public Guid Id { get; set; }

    public Guid InstanceId { get; set; }
    public VulnerabilityInstance Instance { get; set; } = default!;

    public string Body { get; set; } = default!;
    /// <summary>JSON array ของ userId ที่ถูก mention.</summary>
    public string? Mentions { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
