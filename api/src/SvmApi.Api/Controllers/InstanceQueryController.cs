using Microsoft.AspNetCore.Mvc;
using SvmApi.Api.Auth;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Application.Dashboard;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Api.Controllers;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequirePermission(PermissionCatalog.InstanceRead)]
public sealed class InstanceQueryController(
    IInstanceQueryService instances,
    IBusinessUnitQueryService businessUnits,
    IAuthService authService,
    ICurrentUser currentUser,
    ILogger<InstanceQueryController> logger) : ControllerBase
{
    [HttpGet("api/v1/instances")]
    [ProducesResponseType<PagedResult<InstanceListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] InstanceListQuery query, CancellationToken cancellationToken)
        => await MapAsync(await instances.SearchAsync(query, cancellationToken));

    [HttpGet("api/v1/instances/{id:guid}")]
    [ProducesResponseType<InstanceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => await MapAsync(await instances.GetAsync(id, cancellationToken));

    [HttpGet("api/v1/business-units")]
    [ProducesResponseType<BusinessUnitsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BusinessUnits(CancellationToken cancellationToken)
        => Ok(await businessUnits.GetAsync(cancellationToken));

    private async Task<IActionResult> MapAsync<T>(DashboardResult<T> result)
    {
        switch (result.Outcome)
        {
            case DashboardOutcome.Success:
                return Ok(result.Value);
            case DashboardOutcome.ValidationFailed:
                return DashboardHttpResults.Validation(HttpContext, result.Errors);
            case DashboardOutcome.OutOfScope:
                await RecordDeniedAsync();
                return NotFoundProblem();
            case DashboardOutcome.NotFound:
                return NotFoundProblem();
            default:
                throw new InvalidOperationException($"Unhandled instance query outcome '{result.Outcome}'.");
        }
    }

    private ObjectResult NotFoundProblem()
        => AuthProblem.ToResult(HttpContext, StatusCodes.Status404NotFound, "Not found",
            "The requested record was not found.", DashboardErrorCodes.InstanceNotFound);

    private async Task RecordDeniedAsync()
    {
        try
        {
            await authService.RecordDeniedAsync(currentUser.UserId, $"{Request.Method} {Request.Path}",
                AuthHttpContext.GetRequestId(HttpContext), HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("instance.access.denied.audit_failed requestId={RequestId} reason={Reason}",
                AuthHttpContext.GetRequestId(HttpContext), ex.GetType().Name);
        }
    }
}
