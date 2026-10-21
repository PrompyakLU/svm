using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Domain.Verification;

public enum VerificationResult
{
    Verified,
    NotResolved,
    PartiallyResolved,
}

public enum VerificationMethod
{
    Rescan,
    Manual,
}

/// <summary>trans_verificationRecord — ผล verification ก่อนปิด/reopen. BR-15/16 (บังคับ + independent สำหรับ Critical), EX-80, US-027/028/029.</summary>
public class VerificationRecord
{
    public Guid Id { get; set; }

    public Guid InstanceId { get; set; }
    public VulnerabilityInstance Instance { get; set; } = default!;

    public VerificationResult Result { get; set; }
    public VerificationMethod Method { get; set; }

    /// <summary>true เมื่อผู้ verify ≠ remediation owner — บังคับสำหรับ Critical. BR-16.</summary>
    public bool IsIndependent { get; set; }

    /// <summary>FK ไป config_user (scalar เท่านั้น ไม่มี navigation).</summary>
    public Guid VerifiedBy { get; set; }
    public DateTime VerifiedAt { get; set; }
    public string? Notes { get; set; }
}
