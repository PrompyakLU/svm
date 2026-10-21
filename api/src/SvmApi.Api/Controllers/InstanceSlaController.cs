using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SvmApi.Api.Auth;
using SvmApi.Application.Auth;
using SvmApi.Application.Sla;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Api.Controllers;

/// <summary>
/// SlaModule (C9 · US-024/025 · FR-35/36): สถานะ SLA ของ vulnerability instance (due date, เวลาที่เหลือ, breach, ประวัติ due date เดิม).
/// ต้องมี <c>instance.read</c>; instance นอก data scope ตอบ 404 + บันทึก denied-access (EX-07).
/// instance ที่ยังไม่มี timer ตอบ 200 พร้อม <c>state = NotStarted</c> และ <c>blockedReason</c> (เช่น ASSET_CRITICALITY_MISSING — BR-05).
/// </summary>
[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InstanceSlaController(
    ISlaService sla,
    IAuthService authService,
    ICurrentUser currentUser,
    ILogger<InstanceSlaController> logger) : ControllerBase
{
    private const string ReadPolicy = PermissionPolicies.Prefix + PermissionCatalog.InstanceRead;

    /// <summary>SLA timer + due dates + breach status ของ instance (คำนวณเวลาที่เหลือสด ณ ตอนเรียก).</summary>
    [HttpGet("api/v1/instances/{id:guid}/sla")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType<SlaTimerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await sla.GetAsync(id, cancellationToken);

        switch (result.Outcome)
        {
            case SlaOutcome.Success:
                return Ok(result.Value);

            case SlaOutcome.OutOfScope:
                // EX-07: บันทึก denied-access แต่ตอบเหมือนไม่พบ (ไม่เปิดเผยการมีอยู่)
                await RecordDeniedAsync();
                return NotFoundProblem();

            case SlaOutcome.NotFound:
                return NotFoundProblem();

            case SlaOutcome.Unprocessable:
                return Problem(
                    StatusCodes.Status422UnprocessableEntity, "SLA cannot be calculated",
                    result.Message ?? "The SLA cannot be calculated with the current data.", result.Code ?? SlaErrorCodes.PolicyNotFound);

            case SlaOutcome.Conflict:
                return Problem(
                    StatusCodes.Status409Conflict, "Conflict",
                    result.Message ?? "The request conflicts with current state.", result.Code ?? SlaErrorCodes.ConcurrencyConflict);

            default:
                throw new InvalidOperationException($"Unhandled SLA outcome '{result.Outcome}'.");
        }
    }

    private ObjectResult Problem(int status, string title, string detail, string code)
        => AuthProblem.ToResult(HttpContext, status, title, detail, code);

    private ObjectResult NotFoundProblem()
        => Problem(StatusCodes.Status404NotFound, "Not found", "The requested record was not found.", SlaErrorCodes.InstanceNotFound);

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
                "sla.access.denied.audit_failed requestId={RequestId} reason={Reason}",
                AuthHttpContext.GetRequestId(HttpContext), ex.GetType().Name);
        }
    }
}
