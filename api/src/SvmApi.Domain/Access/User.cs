using SvmApi.Domain.Common;

namespace SvmApi.Domain.Access;

/// <summary>config_user — บัญชีผู้ใช้ (local/mock auth, OIDC-ready). FR-01..03, BR-43/44.</summary>
public class User : AuditableEntity
{
    public string Username { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? Email { get; set; }
    public string? PasswordHash { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<UserScope> UserScopes { get; set; } = new List<UserScope>();
}
