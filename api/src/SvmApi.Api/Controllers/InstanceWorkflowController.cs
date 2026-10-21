using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SvmApi.Api.Auth;
using SvmApi.Application.Auth;
using SvmApi.Application.Workflow;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Api.Controllers;

/// <summary>
/// WorkflowModule (C7 · US-018/019 · FR-22/23 · BR-27): เปลี่ยนสถานะ vulnerability instance แบบ table-driven + ดูประวัติ.
/// ต้องมี <c>instance.transition</c> (เปลี่ยนสถานะ) / <c>instance.read</c> (ประวัติ); สิทธิ์เฉพาะของแต่ละ transition ตรวจใน service.
/// instance นอก data scope ตอบ 404 + บันทึก denied-access (EX-07); transition ไม่อนุญาต/ไม่มีหลักฐาน/ต้องอนุมัติ = 422 (EX-55/56);
/// rowVersion ไม่ตรง/ชนกัน = 409 (EX-57); ไม่มีสิทธิ์ตามแถว transition = 403.
/// </summary>
[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InstanceWorkflowController(
    IWorkflowService workflow,
    IAuthService authService,
    ICurrentUser currentUser,
    ILogger<InstanceWorkflowController> logger) : ControllerBase
{
    private const string TransitionPolicy = PermissionPolicies.Prefix + PermissionCatalog.InstanceTransition;
    private const string ReadPolicy = PermissionPolicies.Prefix + PermissionCatalog.InstanceRead;

    /// <summary>
    /// เปลี่ยนสถานะ: <c>{ "toStatus": "Confirmed", "reason": "...", "rowVersion": "AAAA..." }</c>
    /// — rowVersion บังคับ; reason บังคับเมื่อเป็น FalsePositive/RiskAccepted/ExceptionApproved/Reopened.
    /// </summary>
    [HttpPost("api/v1/instances/{id:guid}/transition")]
    [Authorize(Policy = TransitionPolicy)]
    [ProducesResponseType<TransitionResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Transition(Guid id, [FromBody] TransitionRequest? request, CancellationToken cancellationToken)
        => await MapAsync(await workflow.TransitionAsync(id, request!, cancellationToken));

    /// <summary>ประวัติสถานะ + risk ของ instance (ใหม่สุดก่อน).</summary>
    [HttpGet("api/v1/instances/{id:guid}/history")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType<InstanceHistoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> History(Guid id, CancellationToken cancellationToken)
        => await MapAsync(await workflow.GetHistoryAsync(id, cancellationToken));

    // ------------------------------------------------------------ mapping

    private async Task<IActionResult> MapAsync<T>(WorkflowResult<T> result)
    {
        switch (result.Outcome)
        {
            case WorkflowOutcome.Success:
                return Ok(result.Value);

            case WorkflowOutcome.ValidationFailed:
                return Validation(result.Errors ?? new Dictionary<string, string[]>(), result.Code ?? WorkflowErrorCodes.ValidationError);

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
                    result.Code ?? WorkflowErrorCodes.IndependentVerificationRequired);

            case WorkflowOutcome.ExternalServiceFailed:
                return Problem(
                    StatusCodes.Status502BadGateway, "External service failed",
                    result.Message ?? "An external service failed.", result.Code ?? "EXTERNAL_SERVICE_FAILED");

            case WorkflowOutcome.Unprocessable:
                return Problem(
                    StatusCodes.Status422UnprocessableEntity, "Request cannot be processed",
                    result.Message ?? "The request cannot be processed in the current state.", result.Code ?? WorkflowErrorCodes.InvalidTransition);

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
                throw new InvalidOperationException($"Unhandled workflow outcome '{result.Outcome}'.");
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
                "workflow.access.denied.audit_failed requestId={RequestId} reason={Reason}",
                AuthHttpContext.GetRequestId(HttpContext), ex.GetType().Name);
        }
    }

    /// <summary>ValidationProblemDetails พร้อม <c>code</c> + <c>requestId</c>.</summary>
    private ObjectResult Validation(IReadOnlyDictionary<string, string[]> errors, string code)
    {
        var problem = new ValidationProblemDetails(errors.ToDictionary(p => p.Key, p => p.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid workflow request",
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
