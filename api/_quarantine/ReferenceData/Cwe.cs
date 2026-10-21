namespace SvmApi.Domain.ReferenceData;

/// <summary>master_cwe — CWE reference data (seed). FR-13, US-013.</summary>
public class Cwe
{
    public Guid Id { get; set; }
    public string CweId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
