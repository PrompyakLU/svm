namespace SvmApi.Domain.ReferenceData;

/// <summary>master_cve — CVE reference data (seed). FR-13/14/18, US-013/014.</summary>
public class Cve
{
    public Guid Id { get; set; }
    public string CveId { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? PublishedDate { get; set; }
    /// <summary>true เมื่อ CVE ตรงกับรายการ KEV (seed). FR-18, BR-03.</summary>
    public bool IsKev { get; set; }
    public DateTime? KevDateAdded { get; set; }
}
