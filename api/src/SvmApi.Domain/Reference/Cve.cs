namespace SvmApi.Domain.Reference;

/// <summary>master_cve — seed reference data. EX-47 (not found -&gt; mark, keep value).</summary>
public class Cve
{
    public Guid Id { get; set; }
    public string CveId { get; set; } = default!; // CVE-YYYY-NNNN
    public string? Description { get; set; }
    public DateTime? PublishedDate { get; set; }
    public bool IsKev { get; set; }
    public DateTime? KevDateAdded { get; set; }
}
