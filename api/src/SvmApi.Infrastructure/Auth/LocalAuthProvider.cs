using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SvmApi.Application.Auth;
using SvmApi.Domain.Access;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Auth;

/// <summary>
/// <see cref="IAuthProvider"/> แบบ local (US-001, EX-01/04/05): ตรวจรหัสผ่านด้วย <see cref="IPasswordHasher{TUser}"/>.
/// ผู้ใช้ไม่พบ / ปิดใช้งาน / หมดอายุ / รหัสผิด ให้ผลเดียวกัน (null) และใช้เวลาใกล้เคียงกันด้วย dummy hash.
/// roles/permissions/scopes โหลดจาก DB ล่าสุดทุกครั้ง.
/// </summary>
public sealed class LocalAuthProvider(
    SvmDbContext db,
    IPasswordHasher<User> passwordHasher,
    TimeProvider clock) : IAuthProvider
{
    private const int MaxUsernameLength = 256;
    private const int MaxPasswordLength = 512;

    // hash ปลอมสำหรับเทียบเมื่อไม่พบ user — สร้างครั้งเดียวต่อ process
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<User>().HashPassword(
            new User { Username = "-", DisplayName = "-" },
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))));

    private static readonly User DummyUser = new() { Username = "-", DisplayName = "-" };

    public async Task<AuthUserDto?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
    {
        var name = (username ?? string.Empty).Trim();
        var candidate = password ?? string.Empty;
        var inputAcceptable = name.Length is > 0 and <= MaxUsernameLength
            && candidate.Length is > 0 and <= MaxPasswordLength;

        User? user = null;
        if (inputAcceptable)
        {
            user = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == name, cancellationToken);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var eligible = inputAcceptable
            && user is not null
            && IsEligible(user, now)
            && !string.IsNullOrEmpty(user.PasswordHash);

        // ทำงานเท่ากันทุกกรณีเพื่อลด timing difference
        var hashToCheck = eligible ? user!.PasswordHash! : DummyHash.Value;
        var passwordOk = Verify(user ?? DummyUser, hashToCheck, inputAcceptable ? candidate : "x");

        if (!eligible || !passwordOk)
        {
            return null;
        }

        return await LoadAuthUserAsync(user!, cancellationToken);
    }

    public async Task<AuthUserDto?> GetActiveUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || !IsEligible(user, clock.GetUtcNow().UtcDateTime))
        {
            return null;
        }

        return await LoadAuthUserAsync(user, cancellationToken);
    }

    private static bool IsEligible(User user, DateTime utcNow)
        => user.IsActive && (user.ExpiresAt is null || user.ExpiresAt.Value > utcNow);

    private bool Verify(User user, string hash, string candidate)
    {
        try
        {
            return passwordHasher.VerifyHashedPassword(user, hash, candidate) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            // hash ใน DB เสียรูปแบบ — ถือว่าไม่ผ่าน
            return false;
        }
    }

    private async Task<AuthUserDto> LoadAuthUserAsync(User user, CancellationToken cancellationToken)
    {
        var userId = user.Id;

        var roles = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.Role.Code)
            .Distinct()
            .OrderBy(code => code)
            .ToListAsync(cancellationToken);

        var permissions = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .OrderBy(code => code)
            .ToListAsync(cancellationToken);

        var scopeRows = await db.UserScopes.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new { s.ScopeType, s.ScopeValue })
            .ToListAsync(cancellationToken);

        var scopes = scopeRows
            .Select(s => new AuthScope(s.ScopeType.ToString(), s.ScopeValue))
            .Distinct()
            .ToList();

        return new AuthUserDto(user.Id, user.Username, user.DisplayName, roles, permissions, scopes);
    }
}
