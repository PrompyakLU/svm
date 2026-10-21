using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SvmApi.Application.Audit;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Application.Notifications;
using SvmApi.Application.Remediation;
using SvmApi.Domain.Remediation;
using SvmApi.Infrastructure.Auth.DataScope;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Remediation;

/// <summary>
/// <see cref="ICommentService"/> (C8 · US-023 · FR-30). ข้อความถูกล้าง control char และจำกัดความยาว (<see cref="CommentRules.MaxBodyLength"/>);
/// ฝั่ง API ไม่ render HTML (UI ต้อง encode ตอนแสดงผล). @mention ดึงจาก <c>@username</c> และ resolve เฉพาะผู้ใช้ที่มีอยู่ ใช้งานได้
/// และมี scope ครอบคลุม asset ของ instance — ที่เหลือถูกมองเป็นข้อความธรรมดา. ผู้ถูก mention ได้ in-app notification (ยกเว้นผู้เขียนเอง) หลัง commit.
/// audit ไม่เก็บเนื้อหา comment (เก็บเฉพาะความยาวและจำนวน mention).
/// </summary>
public sealed class CommentService(
    SvmDbContext db,
    IUnitOfWork unitOfWork,
    IAuditService audit,
    INotificationService notifications,
    ICurrentUser user,
    TimeProvider clock,
    ILogger<CommentService> logger) : ICommentService
{
    public const string AuditSource = "CommentService";
    public const string ActionCreate = "comment.create";

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<RemediationResult<CommentDto>> AddAsync(Guid instanceId, AddCommentRequest request, CancellationToken cancellationToken)
    {
        var actorId = user.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

        var instance = await db.VulnerabilityInstances.ApplyDataScope(user).AsNoTracking()
            .Where(i => i.Id == instanceId)
            .Select(i => new
            {
                i.Id,
                i.Title,
                i.Asset.BusinessUnit,
                i.Asset.AssetCode,
                OwnerIds = i.Asset.Ownerships.Where(o => o.OwnerUserId != null).Select(o => o.OwnerUserId!.Value).ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (instance is null)
        {
            var exists = await db.VulnerabilityInstances.AsNoTracking().AnyAsync(i => i.Id == instanceId, cancellationToken);
            return RemediationResult<CommentDto>.Fail(
                exists ? RemediationOutcome.OutOfScope : RemediationOutcome.NotFound, RemediationErrorCodes.InstanceNotFound);
        }

        var (body, error) = CommentRules.NormalizeBody(request?.Body);
        if (body is null)
        {
            return RemediationResult<CommentDto>.Invalid(
                new Dictionary<string, string[]> { ["body"] = [error!] }, error!);
        }

        // --- resolve @mention เฉพาะผู้ใช้ที่มีอยู่ + active + อยู่ใน scope ของ asset
        var mentions = new List<MentionDto>();
        var names = CommentRules.ExtractMentions(body);
        if (names.Count > 0)
        {
            var now0 = clock.GetUtcNow().UtcDateTime;
            var candidates = await db.Users.AsNoTracking()
                .Where(u => names.Contains(u.Username) && u.IsActive)
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.ExpiresAt,
                    Scopes = u.UserScopes.Select(s => new { s.ScopeType, s.ScopeValue }).ToList(),
                })
                .ToListAsync(cancellationToken);

            foreach (var name in names)
            {
                var match = candidates.FirstOrDefault(c => string.Equals(c.Username, name, StringComparison.OrdinalIgnoreCase));
                if (match is null || (match.ExpiresAt is { } exp && exp <= now0))
                {
                    continue;
                }

                var grants = match.Scopes.Select(s => new ScopeGrantFacts(s.ScopeType.ToString(), s.ScopeValue));
                if (AssigneeScopeMatcher.HasScope(grants, instance.BusinessUnit, instance.AssetCode, instance.OwnerIds.Contains(match.Id))
                    && mentions.All(m => m.UserId != match.Id))
                {
                    mentions.Add(new MentionDto(match.Id, match.Username));
                }
            }
        }

        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            InstanceId = instanceId,
            Body = body,
            Mentions = mentions.Count == 0 ? null : JsonSerializer.Serialize(mentions.Select(m => m.UserId).ToList(), Json),
            CreatedBy = actorId,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };

        await unitOfWork.ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear(); // retry-safe
            db.Comments.Add(comment);

            await audit.AppendAsync(
                new AuditEntry(
                    actorId, ActionCreate, "Comment", comment.Id.ToString(),
                    null,
                    JsonSerializer.Serialize(new { instanceId, length = body.Length, mentionCount = mentions.Count }, Json),
                    "Success", AuditSource),
                ct);
        }, cancellationToken);

        var recipients = NotificationRules.DistinctRecipients(mentions.Select(m => m.UserId), exclude: actorId);
        if (recipients.Count > 0)
        {
            try
            {
                await notifications.NotifyManyAsync(
                    recipients, "MENTION", "คุณถูกกล่าวถึงใน comment",
                    $"{user.Username ?? "ผู้ใช้"} กล่าวถึงคุณในช่องโหว่ \"{TextSanitizer.Truncate(instance.Title, 200)}\"",
                    instanceId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    "comment.notify.failed instanceId={InstanceId} reason={Reason}", instanceId, ex.GetType().Name);
            }
        }

        logger.LogInformation(
            "comment.created instanceId={InstanceId} commentId={CommentId} mentions={Mentions}", instanceId, comment.Id, mentions.Count);

        return RemediationResult<CommentDto>.Ok(
            new CommentDto(comment.Id, instanceId, comment.Body, mentions, comment.CreatedBy, comment.CreatedAt));
    }
}
