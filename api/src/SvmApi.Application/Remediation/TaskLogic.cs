using SvmApi.Application.Common;
using SvmApi.Application.Risk;
using SvmApi.Domain.Remediation;

namespace SvmApi.Application.Remediation;

/// <summary>ผลตรวจ <see cref="UpdateTaskRequest"/> (ค่าที่ normalize แล้ว).</summary>
public sealed record TaskUpdateValidation(
    IReadOnlyDictionary<string, string[]> Errors,
    RemediationTaskStatus? Status,
    byte[]? RowVersion)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>กติกา pure ของ remediation task (FR-28/29 · EX-63).</summary>
public static class TaskRules
{
    public const int MaxLongTextLength = 4000;
    public const int MaxChangeReferenceLength = 200;
    public const int MaxCommentLength = 2000;

    /// <summary>
    /// ตรวจคำขออัปเดต: ต้องมี rowVersion, ต้องมีฟิลด์เปลี่ยนอย่างน้อยหนึ่งตัว, progress 0..100, status ถูกต้อง,
    /// Blocked ต้องมี obstacle, Completed ต้อง progress = 100 (หรือไม่ส่ง progress → ระบบตั้ง 100).
    /// </summary>
    public static TaskUpdateValidation ValidateUpdate(UpdateTaskRequest? request, string? existingObstacle)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["body"] = [RemediationErrorCodes.ValidationError];
            return new TaskUpdateValidation(errors, null, null);
        }

        var rowVersion = RiskScoringEngine.ParseRowVersion(request.RowVersion, required: true, out var rvError);
        if (rvError is not null)
        {
            errors["rowVersion"] = [rvError == "ROW_VERSION_REQUIRED" ? RemediationErrorCodes.RowVersionRequired : RemediationErrorCodes.InvalidRowVersion];
        }

        var anyField = request.RootCause is not null || request.ActionPlan is not null || request.TargetDate is not null
                       || request.ChangeReference is not null || request.ProgressPercent is not null || request.Status is not null
                       || request.Obstacle is not null || !string.IsNullOrWhiteSpace(request.Comment);
        if (!anyField)
        {
            errors["body"] = [RemediationErrorCodes.NoChanges];
        }

        CheckLength(errors, "rootCause", request.RootCause, MaxLongTextLength);
        CheckLength(errors, "actionPlan", request.ActionPlan, MaxLongTextLength);
        CheckLength(errors, "obstacle", request.Obstacle, MaxLongTextLength);
        CheckLength(errors, "changeReference", request.ChangeReference, MaxChangeReferenceLength);
        CheckLength(errors, "comment", request.Comment, MaxCommentLength);

        if (request.ProgressPercent is < 0 or > 100)
        {
            errors["progressPercent"] = [RemediationErrorCodes.InvalidProgress];
        }

        if (request.TargetDate is { } target && (target.Year < 2000 || target.Year > 2200))
        {
            errors["targetDate"] = [RemediationErrorCodes.ValidationError];
        }

        RemediationTaskStatus? status = null;
        if (request.Status is not null)
        {
            var raw = request.Status.Trim();
            if (!int.TryParse(raw, out _)
                && Enum.TryParse<RemediationTaskStatus>(raw, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed))
            {
                status = parsed;
            }
            else
            {
                errors["status"] = [RemediationErrorCodes.InvalidTaskStatus];
            }
        }

        if (status == RemediationTaskStatus.Blocked)
        {
            var obstacle = request.Obstacle is null ? existingObstacle : request.Obstacle;
            if (string.IsNullOrWhiteSpace(TextSanitizer.Clean(obstacle, allowLineBreaks: true)))
            {
                errors["obstacle"] = [RemediationErrorCodes.ObstacleRequired];
            }
        }

        if (status == RemediationTaskStatus.Completed && request.ProgressPercent is { } p && p != 100)
        {
            errors["progressPercent"] = [RemediationErrorCodes.ProgressMismatch];
        }

        return new TaskUpdateValidation(errors, status, rowVersion);
    }

    /// <summary>ข้อความ optional → null เมื่อว่างหลังล้าง control char (ใช้ล้างค่าเดิมด้วยสตริงว่าง).</summary>
    public static string? NormalizeText(string? value, bool allowLineBreaks = true)
    {
        var cleaned = TextSanitizer.Clean(value, allowLineBreaks);
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>EX-63: target date เกิน SLA due date (ไม่มี extension) → เตือน/ทำเครื่องหมาย.</summary>
    public static bool ExceedsSla(DateTime? targetDate, DateTime dueDate) => targetDate is { } t && t > dueDate;

    /// <summary>
    /// token ควบคุมการแก้ไขซ้อน (EX-57) ของ task — <c>trans_remediationTask</c> ไม่มีคอลัมน์ rowversion
    /// จึงใช้ ticks ของ updatedAt (หรือ createdAt) เป็น base64 8 ไบต์ ซึ่งเปลี่ยนทุกครั้งที่บันทึก.
    /// </summary>
    public static byte[] ConcurrencyToken(DateTime createdAt, DateTime? updatedAt)
        => BitConverter.GetBytes((updatedAt ?? createdAt).Ticks);

    public static string ConcurrencyTokenBase64(DateTime createdAt, DateTime? updatedAt)
        => Convert.ToBase64String(ConcurrencyToken(createdAt, updatedAt));

    public static bool IsStale(byte[]? expected, DateTime createdAt, DateTime? updatedAt)
        => expected is null || !expected.AsSpan().SequenceEqual(ConcurrencyToken(createdAt, updatedAt));

    private static void CheckLength(Dictionary<string, string[]> errors, string field, string? value, int max)
    {
        if (value is not null && value.Length > max)
        {
            errors[field] = [RemediationErrorCodes.TooLong];
        }
    }
}
