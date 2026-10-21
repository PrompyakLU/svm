using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SvmApi.Api.Auth;
using SvmApi.Application.Auth;
using SvmApi.Application.Remediation;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Api.Controllers;

/// <summary>
/// AssignmentRemediationModule (C8 · US-020..023 · FR-24..31): มอบหมาย/bulk, remediation task, evidence, comment.
/// ทุก endpoint ผ่าน <c>perm:</c> policy + data scope ใน service; id นอก scope ตอบ 404 + บันทึก denied-access (EX-07).
/// status: 400 ตรวจข้อมูลไม่ผ่าน · 403 ไม่ใช่เจ้าของงาน · 404 · 409 rowVersion ไม่ตรง/ชนกัน (EX-57) ·
/// 413 ไฟล์ใหญ่เกิน · 415 ชนิด/เนื้อหาไฟล์ไม่ตรง allowlist · 422 assignee ใช้ไม่ได้/สถานะไม่รองรับ/<c>EVIDENCE_MALWARE</c>/<c>EVIDENCE_UNSCANNABLE</c> (EX-21/22/60).
/// error ไม่สะท้อนเนื้อหาไฟล์หรือข้อความที่ผู้ใช้ส่งมา.
/// </summary>
[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssignmentRemediationController(
    IAssignmentService assignments,
    IRemediationTaskService tasks,
    IEvidenceService evidence,
    ICommentService comments,
    IAuthService authService,
    ICurrentUser currentUser,
    IOptions<EvidenceOptions> evidenceOptions,
    ILogger<AssignmentRemediationController> logger) : ControllerBase
{
    private const string AssignPolicy = PermissionPolicies.Prefix + PermissionCatalog.InstanceAssign;
    private const string TaskReadPolicy = PermissionPolicies.Prefix + PermissionCatalog.TaskRead;
    private const string TaskUpdatePolicy = PermissionPolicies.Prefix + PermissionCatalog.TaskUpdate;
    private const string EvidenceUploadPolicy = PermissionPolicies.Prefix + PermissionCatalog.EvidenceUpload;
    private const string InstanceReadPolicy = PermissionPolicies.Prefix + PermissionCatalog.InstanceRead;
    private const string CommentPolicy = PermissionPolicies.Prefix + PermissionCatalog.CommentWrite;

    // เผื่อ overhead ของ multipart boundary/header เหนือขนาดไฟล์
    private const long MultipartOverheadBytes = 64 * 1024;

    // ------------------------------------------------------------ assignment

    /// <summary>
    /// มอบหมาย instance: <c>{ "assignToUserId": "...", "reason": "...", "rowVersion": "..." }</c> หรือ <c>assignToTeam</c>;
    /// ไม่ส่ง assignee = อัตโนมัติตาม assignment rule (FR-24). ผล <c>status</c> = Assigned | Queued (ไม่พบกฎ/assignee ของกฎใช้ไม่ได้ — EX-59/60).
    /// </summary>
    [HttpPost("api/v1/instances/{id:guid}/assign")]
    [Authorize(Policy = AssignPolicy)]
    [ProducesResponseType<AssignResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignRequest? request, CancellationToken cancellationToken)
        => await MapAsync(
            await assignments.AssignAsync(id, request ?? new AssignRequest(null, null, null, null), cancellationToken),
            value => Ok(value));

    /// <summary>มอบหมายหลายรายการ: <c>{ "instanceIds": [...], "assignToUserId": "..." }</c> → <c>{ assigned, skipped:[{id, reason}] }</c> (EX-61).</summary>
    [HttpPost("api/v1/instances/bulk-assign")]
    [Authorize(Policy = AssignPolicy)]
    [ProducesResponseType<BulkAssignResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkAssign([FromBody] BulkAssignRequest? request, CancellationToken cancellationToken)
    {
        var result = await assignments.BulkAssignAsync(
            request ?? new BulkAssignRequest(null, null, null, null), cancellationToken);

        if (result.Outcome == RemediationOutcome.Success
            && result.Value!.Skipped.Any(s => s.Reason == RemediationErrorCodes.OutOfScope))
        {
            await RecordDeniedAsync(); // EX-07: บางรายการอยู่นอก scope
        }

        return await MapAsync(result, value => Ok(value));
    }

    // ------------------------------------------------------------ remediation task

    /// <summary>รายละเอียด remediation task (รวม <c>rowVersion</c> สำหรับ PUT และ <c>targetDateExceedsSla</c> — EX-63).</summary>
    [HttpGet("api/v1/tasks/{id:guid}")]
    [Authorize(Policy = TaskReadPolicy)]
    [ProducesResponseType<RemediationTaskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTask(Guid id, CancellationToken cancellationToken)
        => await MapAsync(await tasks.GetAsync(id, cancellationToken), value => Ok(value));

    /// <summary>อัปเดตแผน/ความคืบหน้า (partial · <c>rowVersion</c> บังคับ). เฉพาะ owner ของ task หรือผู้มี <c>instance.assign</c>.</summary>
    [HttpPut("api/v1/tasks/{id:guid}")]
    [Authorize(Policy = TaskUpdatePolicy)]
    [ProducesResponseType<RemediationTaskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateTask(Guid id, [FromBody] UpdateTaskRequest? request, CancellationToken cancellationToken)
        => await MapAsync(await tasks.UpdateAsync(id, request!, cancellationToken), value => Ok(value));

    // ------------------------------------------------------------ evidence

    /// <summary>
    /// อัปโหลด evidence (multipart: <c>file</c>, <c>taskId</c> optional). ตรวจนามสกุล/ขนาด/magic bytes + malware scan ก่อนรับ;
    /// ชื่อไฟล์ซ้ำ = เวอร์ชันถัดไป (ไม่ทับ). malware → 422 <c>EVIDENCE_MALWARE</c>; ตรวจไม่ได้ (เช่น เข้ารหัส) → 422 <c>EVIDENCE_UNSCANNABLE</c>.
    /// </summary>
    [HttpPost("api/v1/instances/{id:guid}/evidence")]
    [Authorize(Policy = EvidenceUploadPolicy)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(EvidenceOptions.HardMaxFileBytes + MultipartOverheadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = EvidenceOptions.HardMaxFileBytes + MultipartOverheadBytes)]
    [ProducesResponseType<EvidenceDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UploadEvidence(
        Guid id, IFormFile? file, [FromForm] Guid? taskId, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            var code = file is null ? RemediationErrorCodes.FileMissing : RemediationErrorCodes.FileEmpty;
            return Validation(new Dictionary<string, string[]> { ["file"] = [code] }, code);
        }

        var maxBytes = evidenceOptions.Value.EffectiveMaxFileBytes;
        if (file.Length > maxBytes)
        {
            return Problem(
                StatusCodes.Status413PayloadTooLarge, "File too large",
                $"The file exceeds the maximum size of {maxBytes} bytes.", RemediationErrorCodes.FileTooLarge);
        }

        var buffer = new byte[(int)file.Length];
        await using (var stream = file.OpenReadStream())
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken);
        }

        // Content-Type ของ client ไม่ถูกใช้ — service ตัดสินจากนามสกุล + เนื้อหา
        return await MapAsync(
            await evidence.UploadAsync(id, new EvidenceUploadRequest(file.FileName, buffer, taskId), cancellationToken),
            value => StatusCode(StatusCodes.Status201Created, value));
    }

    /// <summary>รายการ evidence ทุกเวอร์ชันของ instance (ไม่เปิดเผย storage path).</summary>
    [HttpGet("api/v1/instances/{id:guid}/evidence")]
    [Authorize(Policy = InstanceReadPolicy)]
    [ProducesResponseType<IReadOnlyList<EvidenceDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListEvidence(Guid id, CancellationToken cancellationToken)
        => await MapAsync(await evidence.ListAsync(id, cancellationToken), value => Ok(value));

    // ------------------------------------------------------------ comments

    /// <summary>เพิ่ม comment: <c>{ "body": "ดู @officer1 ด้วย" }</c> — @mention ที่ resolve ได้เท่านั้นที่ถูกแจ้งเตือน in-app.</summary>
    [HttpPost("api/v1/instances/{id:guid}/comments")]
    [Authorize(Policy = CommentPolicy)]
    [ProducesResponseType<CommentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddComment(Guid id, [FromBody] AddCommentRequest? request, CancellationToken cancellationToken)
        => await MapAsync(
            await comments.AddAsync(id, request ?? new AddCommentRequest(null), cancellationToken),
            value => StatusCode(StatusCodes.Status201Created, value));

    // ------------------------------------------------------------ mapping

    private async Task<IActionResult> MapAsync<T>(RemediationResult<T> result, Func<T, IActionResult> onSuccess)
    {
        switch (result.Outcome)
        {
            case RemediationOutcome.Success:
                return onSuccess(result.Value!);

            case RemediationOutcome.ValidationFailed:
                return Validation(result.Errors ?? new Dictionary<string, string[]>(), result.Code ?? RemediationErrorCodes.ValidationError);

            case RemediationOutcome.Forbidden:
                await RecordDeniedAsync();
                return Problem(
                    StatusCodes.Status403Forbidden, "Forbidden",
                    result.Message ?? "You do not have permission to perform this action.", result.Code ?? AuthProblem.Forbidden);

            case RemediationOutcome.OutOfScope:
                await RecordDeniedAsync(); // EX-07: บันทึก denied-access แต่ตอบเหมือนไม่พบ
                return NotFoundProblem(result.Code);

            case RemediationOutcome.NotFound:
                return NotFoundProblem(result.Code);

            case RemediationOutcome.Conflict:
                return Problem(
                    StatusCodes.Status409Conflict, "Conflict",
                    result.Message ?? "The request conflicts with current state.", result.Code ?? RemediationErrorCodes.ConcurrencyConflict);

            case RemediationOutcome.Unprocessable:
                return Problem(
                    StatusCodes.Status422UnprocessableEntity, "Unprocessable request",
                    result.Message ?? "The request could not be processed.", result.Code ?? RemediationErrorCodes.ValidationError);

            case RemediationOutcome.PayloadTooLarge:
                return Problem(
                    StatusCodes.Status413PayloadTooLarge, "File too large",
                    result.Message ?? "The file is too large.", result.Code ?? RemediationErrorCodes.FileTooLarge);

            case RemediationOutcome.UnsupportedMediaType:
                return Problem(
                    StatusCodes.Status415UnsupportedMediaType, "Unsupported file",
                    result.Message ?? "The file type or content is not allowed.", result.Code ?? RemediationErrorCodes.TypeNotAllowed);

            default:
                throw new InvalidOperationException($"Unhandled remediation outcome '{result.Outcome}'.");
        }
    }

    private ObjectResult Problem(int status, string title, string detail, string code)
        => AuthProblem.ToResult(HttpContext, status, title, detail, code);

    private ObjectResult NotFoundProblem(string? code)
        => Problem(StatusCodes.Status404NotFound, "Not found", "The requested record was not found.", code ?? RemediationErrorCodes.InstanceNotFound);

    private async Task RecordDeniedAsync()
    {
        try
        {
            await authService.RecordDeniedAsync(
                currentUser.UserId,
                $"{Request.Method} {Request.Path}", // ไม่รวม query string
                AuthHttpContext.GetRequestId(HttpContext),
                HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                "remediation.access.denied.audit_failed requestId={RequestId} reason={Reason}",
                AuthHttpContext.GetRequestId(HttpContext), ex.GetType().Name);
        }
    }

    /// <summary>ValidationProblemDetails พร้อม <c>code</c> + <c>requestId</c>.</summary>
    private ObjectResult Validation(IReadOnlyDictionary<string, string[]> errors, string code)
    {
        var problem = new ValidationProblemDetails(errors.ToDictionary(p => p.Key, p => p.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid request",
            Detail = "One or more fields are invalid.",
        };
        problem.Extensions["code"] = code;
        problem.Extensions["requestId"] = AuthHttpContext.GetRequestId(HttpContext);

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }
}
