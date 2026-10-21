using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Domain.Remediation;

/// <summary>ผลสแกน malware ของไฟล์หลักฐาน (NFR-18). Pending = ยังไม่สแกน.</summary>
public enum ScanStatus
{
    Pending,
    Clean,
    Infected,
    Unscannable,
}

/// <summary>trans_evidence — หลักฐานประกอบ instance/task/verification. FR-31, BR-42, NFR-18, EX-21/22.</summary>
public class Evidence
{
    public Guid Id { get; set; }

    public Guid InstanceId { get; set; }
    public VulnerabilityInstance Instance { get; set; } = default!;

    public Guid? TaskId { get; set; }
    public RemediationTask? Task { get; set; }

    /// <summary>FK ไป trans_verificationRecord (scalar เท่านั้น — ไม่มี navigation).</summary>
    public Guid? VerificationId { get; set; }

    public string FileName { get; set; } = default!;
    public string StoragePath { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long FileSize { get; set; }
    public ScanStatus ScanStatus { get; set; } = ScanStatus.Pending;
    /// <summary>เลขเวอร์ชัน — ห้ามแทนที่หลักฐานโดยไม่มี version history (BR-42).</summary>
    public int Version { get; set; } = 1;

    public Guid UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }
}
