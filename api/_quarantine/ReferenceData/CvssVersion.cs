namespace SvmApi.Domain.ReferenceData;

/// <summary>config_cvssVersion — CVSS version ที่ระบบรองรับ. FR-14, EX-48.</summary>
public class CvssVersion
{
    public Guid Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public bool IsSupported { get; set; } = true;
}
