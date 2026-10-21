using SvmApi.Domain.Access;
using SvmApi.Domain.Verification;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Domain.Assignments;

public enum EvidenceScanStatus
{
    Pending,
    Clean,
    Infected,
    Unscannable,
}

/// <summary>trans_evidence — หลักฐานประกอบ finding/task/verification. FR-31, BR-42, NFR-18, EX-21/22, US-022.</summary>
public class Evidence
{
    public Guid Id { get; set; }

    public Guid InstanceId { get; set; }
    public VulnerabilityInstance Instance { get; set; } = null!;

    public Guid? TaskId { get; set; }
    public RemediationTask? Task { get; set; }

    public Guid? VerificationId { get; set; }
    public VerificationRecord? Verification { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public EvidenceScanStatus ScanStatus { get; set; } = EvidenceScanStatus.Pending;
    /// <summary>version history — ห้ามแทนที่ evidence โดยไม่มี version. BR-42.</summary>
    public int Version { get; set; } = 1;

    public Guid UploadedBy { get; set; }
    public User UploadedByUser { get; set; } = null!;
    public DateTime UploadedAt { get; set; }
}
