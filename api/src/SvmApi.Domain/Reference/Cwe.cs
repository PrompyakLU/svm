namespace SvmApi.Domain.Reference;

/// <summary>master_cwe — seed reference data.</summary>
public class Cwe
{
    public Guid Id { get; set; }
    public string CweId { get; set; } = default!; // CWE-NNN
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
}
