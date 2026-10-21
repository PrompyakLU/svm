namespace SvmApi.Domain.Common;

/// <summary>
/// Base สำหรับ entity ที่ต้องมี audit columns (createdAt/By, updatedAt/By) ตาม data-model.md.
/// </summary>
public abstract class AuditableEntity
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
