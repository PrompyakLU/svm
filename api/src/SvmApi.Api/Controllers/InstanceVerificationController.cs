using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SvmApi.Api.Auth;
using SvmApi.Application.Auth;
using SvmApi.Application.Verification;
using SvmApi.Application.Workflow;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Api.Controllers;

/// <summary>
/// VerificationModule (C10 · US-027/028/029 · FR-43..47 · BR-13/14/15/16): ส่งตรวจ, rescan (scanner mock), บันทึกผล, ปิด/reopen ผ่าน Workflow.
/// สิทธิ์: <c>verification.submit</c> / <c>verification.rescan</c> / <c>verification.record</c> (policy) + สิทธิ์ของแถว transition (ตรวจใน service).
/// instance นอก data scope ตอบ 404 + บันทึก denied-access (EX-07); rowVersion ไม่ตรง = 409 (EX-57);
/// Critical ที่ไม่ผ่าน independent verifier = 422 <c>INDEPENDENT_VERIFICATION_REQUIRED</c> (BR-16, EX-80);
/// scanner ล้มเหลว = 502 <c>RESCAN_FAILED</c> (EX-74), ไม่พบ target = 422 <c>RESCAN_TARGET_NOT_FOUND</c> (EX-75).
/// </summary>
[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InstanceVerificationController(
    IVerificationService verification,
    IAuthService authService,
    ICurrentUser currentUser,
    ILogger<InstanceVerificationController> logger) : ControllerBase
{
    private const string SubmitPolicy = PermissionPolicies.Prefix + PermissionCatalog.VerificationSubmit;
    private const string RescanPolicy = PermissionPolicies.Prefix + PermissionCatalog.VerificationRescan;
    private const string RecordPolicy = PermissionPolicies.Prefix + PermissionCatalog.VerificationRecord;

    /// <summary>
    /// ส่ง instance เข้า Pending Verification: <c>{ "rowVersion": "AAAA...", "notes": "..." }</c>
    /// — ต้องมีหลักฐานที่ผ่าน malware scan แล้ว (BR-14, EX-56).
    /// </summary>
    [HttpPost("api/v1/instances/{id:guid}/submit-verification")]
    [Authorize(Policy = SubmitPolicy)]
    [ProducesResponseType<TransitionResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Submit(Guid id, [FromBody] SubmitVerificationRequest? request, CancellationToken cancellationToken)
        => await MapAsync(await verification.SubmitAsync(id, request!, cancellationToken));

    /// <summary>ขอ rescan (scanner mock). พบซ้ำ → บันทึก NotResolved และเปิดกลับ (BR-13); ไม่พบ → 200 ไม่เปลี่ยนสถานะ.</summary>
    [HttpPost("api/v1/instances/{id:guid}/rescan")]
    [Authorize(Policy = RescanPolicy)]
    [ProducesResponseType<RescanResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Rescan(Guid id, CancellationToken cancellationToken)
        => await MapAsync(await verification.RescanAsync(id, cancellationToken));

    /// <summary>
    /// บันทึกผล: <c>{ "result": "Verified|NotResolved|PartiallyResolved", "method": "Rescan|Manual", "notes": "...", "rowVersion": "AAAA..." }</c>
    /// — Verified → Resolved; ผลอื่น → Reopened. Critical + Verified ต้องเป็น independent verifier (BR-16).
    /// </summary>
    [HttpPost("api/v1/instances/{id:guid}/verification")]
    [Authorize(Policy = RecordPolicy)]
    [ProducesResponseType<VerificationRecordedDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Record(Guid id, [FromBody] RecordVerificationRequest? request, CancellationToken cancellationToken)
        => await MapAsync(await verification.RecordAsync(id, request!, cancellationToken));

    // ------------------------------------------------------------ mapping

    private async Task<IActionResult> MapAsync<T>(WorkflowResult<T> result)
    {
        switch (result.Outcome)
        {
            case WorkflowOutcome.Success:
                return Ok(result.Value);

            case WorkflowOutcome.ValidationFailed:
                return Validation(result.Errors ?? new Dictionary<string, string[]>(), result.Code ?? VerificationErrorCodes.ValidationError);

            case WorkflowOutcome.InvalidTransition:
                return Problem(
                    StatusCodes.Status422UnprocessableEntity, "Invalid status transition",
                    result.Message ?? "The status transition is not allowed.", result.Code ?? WorkflowErrorCodes.InvalidTransition);

            case WorkflowOutcome.MissingEvidence:
                return Problem(
                    StatusCodes.Status422UnprocessableEntity, "Evidence required",
                    result.Message ?? "Evidence is required before this status transition.", result.Code ?? WorkflowErrorCodes.MissingEvidence);

            case WorkflowOutcome.ApprovalRequired:
                return Problem(
                    StatusCodes.Status422UnprocessableEntity, "Approval required",
                    result.Message ?? "An approval is required before this status transition.", result.Code ?? WorkflowErrorCodes.ApprovalRequired);

            case WorkflowOutcome.IndependentVerificationRequired:
                return Problem(
                    StatusCodes.Status422UnprocessableEntity, "Independent verification required",
                    result.Message ?? "Critical findings require independent verification.",
                    result.Code ?? VerificationErrorCodes.IndependentVerificationRequired);

            case WorkflowOutcome.Unprocessable:
                return Problem(
                    StatusCodes.Status422UnprocessableEntity, "Request cannot be processed",
                    result.Message ?? "The request cannot be processed in the current state.",
                    result.Code ?? VerificationErrorCodes.NotPendingVerification);

            case WorkflowOutcome.ExternalServiceFailed:
                return Problem(
                    StatusCodes.Status502BadGateway, "Scanner unavailable",
                    result.Message ?? "The scanner could not complete the request.", result.Code ?? VerificationErrorCodes.RescanFailed);

            case WorkflowOutcome.Forbidden:
                await RecordDeniedAsync();
                return Problem(
                    StatusCodes.Status403Forbidden, "Forbidden",
                    result.Message ?? "You do not have permission to perform this status transition.",
                    result.Code ?? WorkflowErrorCodes.PermissionDenied);

            case WorkflowOutcome.OutOfScope:
                // EX-07: บันทึก denied-access แต่ตอบเหมือนไม่พบ (ไม่เปิดเผยการมีอยู่)
                await RecordDeniedAsync();
                return NotFoundProblem();

            case WorkflowOutcome.NotFound:
                return NotFoundProblem();

            case WorkflowOutcome.Conflict:
                return Problem(
                    StatusCodes.Status409Conflict, "Conflict",
                    result.Message ?? "The request conflicts with current state.", result.Code ?? WorkflowErrorCodes.ConcurrencyConflict);

            default:
                throw new InvalidOperationException($"Unhandled verification outcome '{result.Outcome}'.");
        }
    }

    private ObjectResult Problem(int status, string title, string detail, string code)
        => AuthProblem.ToResult(HttpContext, status, title, detail, code);

    private ObjectResult NotFoundProblem()
        => Problem(StatusCodes.Status404NotFound, "Not found", "The requested record was not found.", WorkflowErrorCodes.InstanceNotFound);

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
                "verification.access.denied.audit_failed requestId={RequestId} reason={Reason}",
                AuthHttpContext.GetRequestId(HttpContext), ex.GetType().Name);
        }
    }

    /// <summary>ValidationProblemDetails พร้อม <c>code</c> + <c>requestId</c>.</summary>
    private ObjectResult Validation(IReadOnlyDictionary<string, string[]> errors, string code)
    {
        var problem = new ValidationProblemDetails(errors.ToDictionary(p => p.Key, p => p.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid verification request",
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
