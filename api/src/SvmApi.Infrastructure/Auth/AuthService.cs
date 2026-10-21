using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SvmApi.Application.Audit;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Domain.Access;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Auth;

/// <summary>
/// <see cref="IAuthService"/> (US-001/002/003, EX-01/04/05/06/07, NFR security):
/// session ฝั่ง server + refresh หมุนเวียน + replay detection + audit atomic.
/// ทุก mutation ของ session และ audit event commit ใน transaction เดียวผ่าน <see cref="IUnitOfWork"/>.
/// ห้าม log username/password/token/cookie/hash — log เฉพาะ requestId/userId/sessionId/event.
/// </summary>
public sealed class AuthService(
    SvmDbContext db,
    IAuthProvider provider,
    IAccessTokenIssuer tokenIssuer,
    IAuditService audit,
    IUnitOfWork unitOfWork,
    IOptions<AuthOptions> options,
    TimeProvider clock,
    ILogger<AuthService> logger) : IAuthService
{
    private const string Source = "api";
    private const int MaxAttempts = 3;
    private const int MaxRequestIdLength = 64;

    public async Task<AuthGrant?> LoginAsync(
        string username, string password, string requestId, CancellationToken cancellationToken)
    {
        var user = await provider.AuthenticateAsync(username, password, cancellationToken);
        if (user is null)
        {
            // ไม่บันทึก username (อาจเป็นข้อมูลอ่อนไหว/พิมพ์ผิดเป็นรหัสผ่าน) และไม่บอกสาเหตุ
            await audit.RecordAsync(
                new AuditEntry(null, "auth.login", "Auth", "login", null, RequestJson(requestId), "Failure", Source),
                cancellationToken);
            logger.LogInformation("auth.login.failed requestId={RequestId}", requestId);
            return null;
        }

        var settings = options.Value;

        var created = await unitOfWork.ExecuteAsync<(Guid SessionId, string Token, DateTime LastActivityAt, DateTime AbsoluteExpiresAt)>(
            async ct =>
            {
                db.ChangeTracker.Clear(); // work อาจถูก retry ทั้งก้อน

                var now = clock.GetUtcNow().UtcDateTime;
                var sessionId = Guid.NewGuid();
                var credential = RefreshCredential.Generate(sessionId);

                var session = new AuthSession
                {
                    Id = sessionId,
                    UserId = user.Id,
                    RefreshTokenHash = credential.ComputeHash(),
                    PreviousRefreshTokenHash = null,
                    CreatedAt = now,
                    LastActivityAt = now,
                    AbsoluteExpiresAt = now.AddHours(settings.AbsoluteSessionHours),
                    RevokedAt = null,
                    Version = 1,
                };

                db.AuthSessions.Add(session);
                await db.SaveChangesAsync(ct);
                await audit.AppendAsync(
                    new AuditEntry(user.Id, "auth.login", "AuthSession", sessionId.ToString(), null,
                        RequestJson(requestId), "Success", Source),
                    ct);

                return (sessionId, credential.Encode(), session.LastActivityAt, session.AbsoluteExpiresAt);
            },
            cancellationToken);

        logger.LogInformation(
            "auth.login.succeeded requestId={RequestId} userId={UserId} sessionId={SessionId}",
            requestId, user.Id, created.SessionId);

        return BuildGrant(created.SessionId, created.LastActivityAt, created.AbsoluteExpiresAt, user, created.Token);
    }

    public async Task<AuthGrant?> RefreshAsync(string refreshToken, string requestId, CancellationToken cancellationToken)
    {
        if (!RefreshCredential.TryParse(refreshToken, out var presented))
        {
            return null;
        }

        RefreshOutcome? outcome;
        try
        {
            outcome = await unitOfWork.ExecuteAsync<RefreshOutcome?>(async ct =>
            {
                db.ChangeTracker.Clear();

                var now = clock.GetUtcNow().UtcDateTime;
                var session = await db.AuthSessions.FirstOrDefaultAsync(s => s.Id == presented.SessionId, ct);
                if (session is null || !IsLive(session, now))
                {
                    return null;
                }

                // คำนวณทั้งสองค่าเสมอ (ไม่ short-circuit)
                var presentedHash = presented.ComputeHash();
                var matchesCurrent = RefreshCredential.HashEquals(session.RefreshTokenHash, presentedHash);
                var matchesPrevious = RefreshCredential.HashEquals(session.PreviousRefreshTokenHash, presentedHash);

                if (!matchesCurrent && matchesPrevious)
                {
                    // ใช้ credential เดิมซ้ำ = สงสัยถูกขโมย -> revoke ทั้ง session (EX-06/07)
                    session.RevokedAt = now;
                    session.Version++;
                    await db.SaveChangesAsync(ct);
                    await audit.AppendAsync(
                        new AuditEntry(session.UserId, "auth.refresh.replay", "AuthSession", session.Id.ToString(),
                            null, RequestJson(requestId), "Denied", Source),
                        ct);
                    return new RefreshOutcome(null, null, session.Id, session.UserId, session.LastActivityAt,
                        session.AbsoluteExpiresAt, Replay: true);
                }

                if (!matchesCurrent)
                {
                    // secret ไม่ตรงและไม่ใช่ของเดิม: ไม่ revoke session ของคนอื่น
                    return null;
                }

                var user = await provider.GetActiveUserAsync(session.UserId, ct);
                if (user is null)
                {
                    return null;
                }

                var next = RefreshCredential.Generate(session.Id);
                session.PreviousRefreshTokenHash = session.RefreshTokenHash;
                session.RefreshTokenHash = next.ComputeHash();
                session.Version++; // concurrency token — refresh พร้อมกันฝั่งหนึ่งจะชน
                await db.SaveChangesAsync(ct); // ไม่ต่อ LastActivityAt: passive refresh ไม่ขยาย idle

                return new RefreshOutcome(user, next.Encode(), session.Id, session.UserId, session.LastActivityAt,
                    session.AbsoluteExpiresAt, Replay: false);
            },
            cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation(
                "auth.refresh.conflict requestId={RequestId} sessionId={SessionId}", requestId, presented.SessionId);
            return null;
        }

        if (outcome is null)
        {
            return null;
        }

        if (outcome.Replay)
        {
            logger.LogWarning(
                "auth.refresh.replay requestId={RequestId} userId={UserId} sessionId={SessionId}",
                requestId, outcome.UserId, outcome.SessionId);
            return null;
        }

        return BuildGrant(outcome.SessionId, outcome.LastActivityAt, outcome.AbsoluteExpiresAt, outcome.User!, outcome.Token!);
    }

    public async Task<AuthState?> ValidateAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken)
    {
        var session = await db.AuthSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, cancellationToken);
        if (session is null || !IsLive(session, clock.GetUtcNow().UtcDateTime))
        {
            return null;
        }

        var user = await provider.GetActiveUserAsync(userId, cancellationToken);
        return user is null ? null : ToState(session.LastActivityAt, session.AbsoluteExpiresAt, user);
    }

    public async Task<AuthState?> RecordActivityAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                return await unitOfWork.ExecuteAsync<AuthState?>(async ct =>
                {
                    db.ChangeTracker.Clear();

                    var now = clock.GetUtcNow().UtcDateTime;
                    var session = await db.AuthSessions
                        .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
                    if (session is null || !IsLive(session, now))
                    {
                        return null;
                    }

                    var user = await provider.GetActiveUserAsync(userId, ct);
                    if (user is null)
                    {
                        return null;
                    }

                    // ต่ออายุ idle เฉพาะหลัง validate ผ่านแล้ว (US-003)
                    session.LastActivityAt = now;
                    session.Version++;
                    await db.SaveChangesAsync(ct);

                    return ToState(session.LastActivityAt, session.AbsoluteExpiresAt, user);
                }, cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // session ถูกเปลี่ยนพร้อมกัน (เช่น refresh หมุน) — โหลดใหม่แล้วลองอีกครั้ง
            }
        }

        return await ValidateAsync(sessionId, userId, cancellationToken);
    }

    public async Task LogoutAsync(Guid sessionId, Guid userId, string requestId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                var revoked = await unitOfWork.ExecuteAsync<bool>(async ct =>
                {
                    db.ChangeTracker.Clear();

                    var session = await db.AuthSessions
                        .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
                    if (session is null || session.RevokedAt is not null)
                    {
                        return false;
                    }

                    session.RevokedAt = clock.GetUtcNow().UtcDateTime;
                    session.Version++;
                    await db.SaveChangesAsync(ct);
                    await audit.AppendAsync(
                        new AuditEntry(userId, "auth.logout", "AuthSession", sessionId.ToString(), null,
                            RequestJson(requestId), "Success", Source),
                        ct);
                    return true;
                }, cancellationToken);

                if (revoked)
                {
                    logger.LogInformation(
                        "auth.logout requestId={RequestId} userId={UserId} sessionId={SessionId}",
                        requestId, userId, sessionId);
                }

                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                // ลองใหม่ด้วยข้อมูลล่าสุด
            }
        }

        throw new InvalidOperationException("Could not revoke the session because of repeated concurrent updates.");
    }

    public async Task RecordDeniedAsync(Guid? userId, string resource, string requestId, CancellationToken cancellationToken)
    {
        await audit.RecordAsync(
            new AuditEntry(userId, "auth.access.denied", "Endpoint", Truncate(resource, 100), null,
                RequestJson(requestId), "Denied", Source),
            cancellationToken);
        logger.LogInformation("auth.access.denied requestId={RequestId} userId={UserId}", requestId, userId);
    }

    // --- helpers ---

    private AuthGrant BuildGrant(
        Guid sessionId, DateTime lastActivityAt, DateTime absoluteExpiresAt, AuthUserDto user, string refreshToken)
    {
        var access = tokenIssuer.Issue(sessionId, user);
        var state = ToState(lastActivityAt, absoluteExpiresAt, user);
        var response = new AuthResponse(access.Value, access.ExpiresAt, state.IdleExpiresAt, state.SessionExpiresAt, user);
        return new AuthGrant(response, refreshToken, sessionId);
    }

    private AuthState ToState(DateTime lastActivityAt, DateTime absoluteExpiresAt, AuthUserDto user)
    {
        var absolute = AsUtc(absoluteExpiresAt);
        var idle = AsUtc(lastActivityAt).AddMinutes(options.Value.IdleTimeoutMinutes);
        return new AuthState(user, idle < absolute ? idle : absolute, absolute);
    }

    /// <summary>session ใช้งานได้: ไม่ถูก revoke, ไม่เกิน absolute deadline, ไม่เกิน idle deadline.</summary>
    private bool IsLive(AuthSession session, DateTime utcNow)
        => session.RevokedAt is null
           && utcNow < session.AbsoluteExpiresAt
           && utcNow < session.LastActivityAt.AddMinutes(options.Value.IdleTimeoutMinutes);

    private static DateTimeOffset AsUtc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string RequestJson(string requestId)
        => JsonSerializer.Serialize(new { requestId = Truncate(requestId ?? string.Empty, MaxRequestIdLength) });

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private sealed record RefreshOutcome(
        AuthUserDto? User,
        string? Token,
        Guid SessionId,
        Guid UserId,
        DateTime LastActivityAt,
        DateTime AbsoluteExpiresAt,
        bool Replay);
}
