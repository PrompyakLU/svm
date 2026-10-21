namespace SvmApi.Domain.Reference;

/// <summary>config_cvssVersion — CVSS version ที่รองรับ. FR-14, EX-48.</summary>
public class CvssVersion
{
    public Guid Id { get; set; }
    public string Version { get; set; } = default!; // 3.1 / 4.0
    public bool IsSupported { get; set; } = true;
}
