namespace SvmApi.Domain.Access;

/// <summary>config_rolePermission — join table (M:N) Role &lt;-&gt; Permission.</summary>
public class RolePermission
{
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = default!;

    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = default!;

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
