using System.Text.Json.Serialization;

namespace SvmApi.Application.Auth;

public sealed record AuthScope(string Type, string Value);
public sealed record AuthUserDto(Guid Id, string Username, string DisplayName,
    IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, IReadOnlyList<AuthScope> Scopes);
public sealed record AuthState(AuthUserDto User, DateTimeOffset IdleExpiresAt, DateTimeOffset SessionExpiresAt);
public sealed record AuthResponse(string Token, DateTimeOffset ExpiresAt,
    DateTimeOffset IdleExpiresAt, DateTimeOffset SessionExpiresAt, AuthUserDto User)
{
    /// <summary>ไม่พิมพ์ bearer token ลง log โดยไม่ตั้งใจ.</summary>
    public override string ToString() => $"AuthResponse {{ UserId = {User.Id}, ExpiresAt = {ExpiresAt:O} }}";
}

public sealed record AuthGrant(AuthResponse Response, [property: JsonIgnore] string RefreshToken, Guid SessionId)
{
    /// <summary>ไม่พิมพ์ refresh credential ลง log โดยไม่ตั้งใจ.</summary>
    public override string ToString() => $"AuthGrant {{ SessionId = {SessionId} }}";
}
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public interface IAuthProvider
{
    Task<AuthUserDto?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken);
    Task<AuthUserDto?> GetActiveUserAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IAccessTokenIssuer
{
    AccessToken Issue(Guid sessionId, AuthUserDto user);
}

public interface IAuthService
{
    Task<AuthGrant?> LoginAsync(string username, string password, string requestId, CancellationToken cancellationToken);
    Task<AuthGrant?> RefreshAsync(string refreshToken, string requestId, CancellationToken cancellationToken);
    Task<AuthState?> ValidateAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken);
    Task<AuthState?> RecordActivityAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken);
    Task LogoutAsync(Guid sessionId, Guid userId, string requestId, CancellationToken cancellationToken);
    Task RecordDeniedAsync(Guid? userId, string resource, string requestId, CancellationToken cancellationToken);
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public int AccessTokenMinutes { get; set; } = 5;
    public int IdleTimeoutMinutes { get; set; } = 30;
    public int AbsoluteSessionHours { get; set; } = 8;
}

public sealed class JwtOptions
{
    public const string SectionName = "Auth:Jwt";
    public string Issuer { get; set; } = "svm-local";
    public string Audience { get; set; } = "svm-web";
    public string SigningKey { get; set; } = string.Empty;
}
