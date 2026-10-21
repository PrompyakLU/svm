namespace SvmApi.Domain.Vulnerabilities;

public enum ImportBatchStatus
{
    Processing,
    Success,
    Partial,
    Failed,
}

/// <summary>config_importBatch — ติดตามการนำเข้าไฟล์ (idempotency). FR-05, BR-36, EX-17.</summary>
public class ImportBatch
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = default!;
    public string? TransactionId { get; set; }
    public int TotalRows { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Processing;
    public Guid ImportedBy { get; set; }
    public DateTime ImportedAt { get; set; }
    public string? ErrorDetail { get; set; }

    public ICollection<VulnerabilityFinding> Findings { get; set; } = new List<VulnerabilityFinding>();
}
