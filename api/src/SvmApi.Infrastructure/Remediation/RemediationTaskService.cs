using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SvmApi.Application.Audit;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Application.Remediation;
using SvmApi.Domain.Remediation;
using SvmApi.Infrastructure.Auth.DataScope;
using SvmApi.Infrastructure.Persistence;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Infrastructure.Remediation;

/// <summary>
/// <see cref="IRemediationTaskService"/> (C8 · US-021 · FR-27/28/29 · EX-63).
/// <list type="bullet">
/// <item>task เข้าถึงได้ผ่าน data scope ของ instance (นอก scope = 404 + denied-access).</item>
/// <item>แก้ไขได้เมื่อเป็น owner ของ task, task ยังเป็นของทีม (ไม่มี owner user), หรือผู้ใช้มี <c>instance.assign</c>; อื่น ๆ = 403.</item>
/// <item><c>trans_remediationTask</c> ไม่มีคอลัมน์ rowversion → ใช้ token จาก updatedAt/createdAt (<see cref="TaskRules.ConcurrencyToken"/>)
/// และล็อกแถว (UPDLOCK) ใน transaction ก่อนเทียบ เพื่อกันการบันทึกซ้อน (409).</item>
/// <item>target date เกิน due date (SLA) ไม่ถูกปฏิเสธ แต่ทำเครื่องหมาย <c>targetDateExceedsSla</c> + บันทึกใน audit (EX-63).</item>
/// <item>audit ไม่เก็บข้อความอิสระ (root cause/action plan/comment) — เก็บเฉพาะสถานะ ความคืบหน้า และรายชื่อฟิลด์ที่เปลี่ยน.</item>
/// </list>
/// </summary>
public sealed class RemediationTaskService(
    SvmDbContext db,
    IUnitOfWork unitOfWork,
    IAuditService audit,
    ICurrentUser user,
    TimeProvider clock,
    ILogger<RemediationTaskService> logger) : IRemediationTaskService
{
    public const string AuditSource = "RemediationTaskService";
    public const string ActionUpdate = "task.update";

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<RemediationResult<RemediationTaskDto>> GetAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await db.RemediationTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return RemediationResult<RemediationTaskDto>.Fail(RemediationOutcome.NotFound, RemediationErrorCodes.TaskNotFound);
        }

        if (!await InScopeAsync(task.InstanceId, cancellationToken))
        {
            return RemediationResult<RemediationTaskDto>.Fail(RemediationOutcome.OutOfScope, RemediationErrorCodes.TaskNotFound);
        }

        return RemediationResult<RemediationTaskDto>.Ok(ToDto(task));
    }

    public async Task<RemediationResult<RemediationTaskDto>> UpdateAsync(
        Guid taskId, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        var actorId = user.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

        var head = await db.RemediationTasks.AsNoTracking()
            .Where(t => t.Id == taskId)
            .Select(t => new { t.InstanceId, t.OwnerUserId, t.Obstacle })
            .FirstOrDefaultAsync(cancellationToken);
        if (head is null)
        {
            return RemediationResult<RemediationTaskDto>.Fail(RemediationOutcome.NotFound, RemediationErrorCodes.TaskNotFound);
        }

        if (!await InScopeAsync(head.InstanceId, cancellationToken))
        {
            return RemediationResult<RemediationTaskDto>.Fail(RemediationOutcome.OutOfScope, RemediationErrorCodes.TaskNotFound);
        }

        if (head.OwnerUserId is { } ownerId && ownerId != actorId && !user.HasPermission(PermissionCatalog.InstanceAssign))
        {
            return RemediationResult<RemediationTaskDto>.Fail(
                RemediationOutcome.Forbidden, RemediationErrorCodes.NotTaskOwner, "Only the task owner or an assigner can update this task.");
        }

        var validation = TaskRules.ValidateUpdate(request, head.Obstacle);
        if (!validation.IsValid)
        {
            return RemediationResult<RemediationTaskDto>.Invalid(validation.Errors);
        }

        try
        {
            return await unitOfWork.ExecuteAsync(async ct =>
            {
                db.ChangeTracker.Clear(); // กัน state ค้างเมื่อ execution strategy retry ทั้งก้อน

                // ล็อกแถวเพื่อ serialize การแก้ไขซ้อน แล้วค่อยเทียบ token
                var task = await db.RemediationTasks
                    .FromSqlInterpolated($"SELECT * FROM [trans_remediationTask] WITH (UPDLOCK, ROWLOCK) WHERE [id] = {taskId}")
                    .FirstOrDefaultAsync(ct);
                if (task is null)
                {
                    return RemediationResult<RemediationTaskDto>.Fail(RemediationOutcome.NotFound, RemediationErrorCodes.TaskNotFound);
                }

                if (TaskRules.IsStale(validation.RowVersion, task.CreatedAt, task.UpdatedAt))
                {
                    return Conflict();
                }

                var now = clock.GetUtcNow().UtcDateTime;
                var changed = Apply(task, request, validation.Status);
                task.UpdatedAt = now;
                task.UpdatedBy = actorId;

                var commentText = TaskRules.NormalizeText(request.Comment);
                if (commentText is not null)
                {
                    db.Comments.Add(new Comment
                    {
                        Id = Guid.NewGuid(),
                        InstanceId = task.InstanceId,
                        Body = commentText,
                        CreatedBy = actorId,
                        CreatedAt = now,
                    });
                    changed.Add("comment");
                }

                var exceeds = TaskRules.ExceedsSla(task.TargetDate, task.DueDate);
                await audit.AppendAsync(
                    new AuditEntry(
                        actorId, ActionUpdate, "RemediationTask", task.Id.ToString(),
                        null,
                        JsonSerializer.Serialize(
                            new
                            {
                                instanceId = task.InstanceId,
                                status = task.Status.ToString(),
                                progressPercent = task.ProgressPercent,
                                targetDateExceedsSla = exceeds,
                                changed,
                            }, Json),
                        "Success", AuditSource),
                    ct);

                if (exceeds)
                {
                    logger.LogWarning("remediation.task.target_exceeds_sla taskId={TaskId} instanceId={InstanceId}", task.Id, task.InstanceId);
                }

                return RemediationResult<RemediationTaskDto>.Ok(ToDto(task));
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Conflict();
        }
    }

    // ------------------------------------------------------------------ helpers

    private async Task<bool> InScopeAsync(Guid instanceId, CancellationToken ct)
        => await db.VulnerabilityInstances.ApplyDataScope(user).AnyAsync(i => i.Id == instanceId, ct);

    /// <summary>ใส่ค่าเฉพาะฟิลด์ที่ส่งมา (null = ไม่เปลี่ยน; ว่าง = ล้างค่า) แล้วคืนรายชื่อฟิลด์ที่เปลี่ยน.</summary>
    private static List<string> Apply(RemediationTask task, UpdateTaskRequest r, RemediationTaskStatus? status)
    {
        var changed = new List<string>();

        if (r.RootCause is not null)
        {
            task.RootCause = TaskRules.NormalizeText(r.RootCause);
            changed.Add("rootCause");
        }

        if (r.ActionPlan is not null)
        {
            task.ActionPlan = TaskRules.NormalizeText(r.ActionPlan);
            changed.Add("actionPlan");
        }

        if (r.TargetDate is { } target)
        {
            task.TargetDate = target.Kind == DateTimeKind.Local ? target.ToUniversalTime() : DateTime.SpecifyKind(target, DateTimeKind.Utc);
            changed.Add("targetDate");
        }

        if (r.ChangeReference is not null)
        {
            task.ChangeReference = TaskRules.NormalizeText(r.ChangeReference, allowLineBreaks: false);
            changed.Add("changeReference");
        }

        if (r.Obstacle is not null)
        {
            task.Obstacle = TaskRules.NormalizeText(r.Obstacle);
            changed.Add("obstacle");
        }

        if (status is { } s)
        {
            task.Status = s;
            changed.Add("status");
            if (s == RemediationTaskStatus.Completed)
            {
                task.ProgressPercent = 100;
            }
        }

        if (r.ProgressPercent is { } p)
        {
            task.ProgressPercent = p;
            changed.Add("progressPercent");
        }

        return changed;
    }

    private static RemediationTaskDto ToDto(RemediationTask t)
        => new(
            t.Id, t.InstanceId, t.OwnerUserId, t.OwnerTeam, t.DueDate, t.RiskLevel.ToString(), t.Recommendation,
            t.RootCause, t.ActionPlan, t.TargetDate, t.ChangeReference, t.ProgressPercent, t.Status.ToString(), t.Obstacle,
            TaskRules.ExceedsSla(t.TargetDate, t.DueDate),
            TaskRules.ConcurrencyTokenBase64(t.CreatedAt, t.UpdatedAt),
            t.CreatedAt, t.UpdatedAt);

    private static RemediationResult<RemediationTaskDto> Conflict()
        => RemediationResult<RemediationTaskDto>.Fail(
            RemediationOutcome.Conflict, RemediationErrorCodes.ConcurrencyConflict,
            "The record was changed by another user. Reload and retry.");
}
