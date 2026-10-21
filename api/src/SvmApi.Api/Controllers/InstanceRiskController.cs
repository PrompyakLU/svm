using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SvmApi.Api.Auth;
using SvmApi.Application.Auth;
using SvmApi.Application.Risk;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Api.Controllers;

/// <summary>
/// Risk scoring ของ vulnerability instance (C6 · US-015/016/017 · FR-16/17/20/21).
/// ต้องมี <c>instance.risk.adjust</c>; instance นอก data scope ตอบ 404 + บันทึก denied-access (EX-07).
/// rowVersion ไม่ตรง/ชนกัน = 409 (EX-57); ข้อมูลหรือ configuration ไม่พอคำนวณ = 422 (EX-50/51/54).
/// </summary>
[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InstanceRiskController(
    IRiskScoringService risk,
    IAuthService authService,
    ICurrentUser currentUser,
    ILogger<InstanceRiskController> logger) : ControllerBase
{
    private const string RiskAdjustPolicy = PermissionPolicies.Prefix + PermissionCatalog.InstanceRiskAdjust;

    /// <summary>คำนวณ risk score ใหม่จาก weight/threshold ปัจจุบัน (FR-21). body optional: <c>{ "rowVersion": "..." }</c>.</summary>
    [HttpPost("api/v1/instances/{id:guid}/recalculate")]
    [Authorize(Policy = RiskAdjustPolicy)]
    [ProducesResponseType<InstanceRiskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Recalculate(
        Guid id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RecalculateRiskRequest? request,
        CancellationToken cancellationToken)
        => await MapAsync(await risk.RecalculateAsync(id, request, cancellationToken));

    /// <summary>
    /// ปรับ risk ด้วยตนเอง (FR-20): <c>{ "riskLevel": "High", "score": 72.5, "reason": "...", "rowVersion": "..." }</c>
    /// — reason + rowVersion บังคับ, ต้องมี score หรือ riskLevel; บันทึกค่าเดิม/ใหม่/เหตุผล/ผู้แก้ไข/เวลาใน history.
    /// </summary>
    [HttpPut("api/v1/instances/{id:guid}/risk")]
    [Authorize(Policy = RiskAdjustPolicy)]
    [ProducesResponseType<InstanceRiskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Override(Guid id, [FromBody] OverrideRiskRequest? request, CancellationToken cancellationToken)
        => await MapAsync(await risk.OverrideAsync(id, request!, cancellationToken));

    // ------------------------------------------------------------ mapping

    private async Task<IActionResult> MapAsync(RiskResult<InstanceRiskDto> result)
    {
        switch (result.Outcome)
        {
            case RiskOutcome.Success:
                return Ok(result.Value);

            case RiskOutcome.ValidationFailed:
                return Validation(StatusCodes.Status400BadRequest, "Invalid risk request",
                    result.Errors ?? new Dictionary<string, string[]>(), result.Code ?? RiskErrorCodes.ValidationError);

            case RiskOutcome.Unprocessable:
                return Validation(StatusCodes.Status422UnprocessableEntity, "Risk score cannot be calculated",
                    result.Errors ?? new Dictionary<string, string[]>(), result.Code ?? RiskErrorCodes.RiskInputIncomplete);

            case RiskOutcome.OutOfScope:
                // EX-07: บันทึก denied-access แต่ตอบเหมือนไม่พบ (ไม่เปิดเผยการมีอยู่)
                await RecordDeniedAsync();
                return NotFoundProblem();

            case RiskOutcome.NotFound:
                return NotFoundProblem();

            case RiskOutcome.Conflict:
                return AuthProblem.ToResult(
                    HttpContext, StatusCodes.Status409Conflict, "Conflict",
                    result.Message ?? "The request conflicts with current state.", result.Code ?? RiskErrorCodes.ConcurrencyConflict);

            default:
                throw new InvalidOperationException($"Unhandled risk outcome '{result.Outcome}'.");
        }
    }

    private ObjectResult NotFoundProblem()
        => AuthProblem.ToResult(
            HttpContext, StatusCodes.Status404NotFound, "Not found", "The requested record was not found.",
            RiskErrorCodes.InstanceNotFound);

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
                "risk.access.denied.audit_failed requestId={RequestId} reason={Reason}",
                AuthHttpContext.GetRequestId(HttpContext), ex.GetType().Name);
        }
    }

    /// <summary>ValidationProblemDetails พร้อม <c>code</c> + <c>requestId</c>.</summary>
    private ObjectResult Validation(int status, string title, IReadOnlyDictionary<string, string[]> errors, string code)
    {
        var problem = new ValidationProblemDetails(errors.ToDictionary(p => p.Key, p => p.Value))
        {
            Status = status,
            Title = title,
            Detail = status == StatusCodes.Status422UnprocessableEntity
                ? "The risk score cannot be calculated with the current data or configuration."
                : "One or more fields are invalid.",
        };
        problem.Extensions["code"] = code;
        problem.Extensions["requestId"] = AuthHttpContext.GetRequestId(HttpContext);

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }
}
