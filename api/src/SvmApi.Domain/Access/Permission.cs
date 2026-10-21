namespace SvmApi.Domain.Access;

/// <summary>config_permission — สิทธิ์ย่อย (least privilege) action+resource เช่น finding.assign.</summary>
public class Permission
{
    public Guid Id { get; set; }
    public string Code { get; set; } = default!;
    public string? Description { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
