namespace SvmApi.Domain.Access;

public enum ScopeType
{
    BusinessUnit,
    Asset,
    Responsibility,
    Organization,
}

/// <summary>config_userScope — ขอบเขตข้อมูลของผู้ใช้ (Data Segregation). FR-63, BR-30.</summary>
public class UserScope
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    public ScopeType ScopeType { get; set; }
    public string ScopeValue { get; set; } = default!;
}
