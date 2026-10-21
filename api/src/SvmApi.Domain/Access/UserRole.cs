namespace SvmApi.Domain.Access;

/// <summary>config_userRole — join table (M:N) User &lt;-&gt; Role.</summary>
public class UserRole
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = default!;

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
