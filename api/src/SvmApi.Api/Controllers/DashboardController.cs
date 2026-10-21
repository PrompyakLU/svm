using Microsoft.AspNetCore.Mvc;
using SvmApi.Api.Auth;
using SvmApi.Application.Dashboard;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DashboardController(IDashboardService dashboard) : ControllerBase
{
    [HttpGet("operations")]
    [RequirePermission(PermissionCatalog.DashboardOps)]
    [ProducesResponseType<OperationsDashboardDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Operations(
        CancellationToken cancellationToken, [FromQuery] int listSize = DashboardMath.DefaultListSize)
        => Map(await dashboard.GetOperationsAsync(listSize, cancellationToken));

    [HttpGet("executive")]
    [RequirePermission(PermissionCatalog.DashboardExec)]
    [ProducesResponseType<ExecutiveDashboardDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Executive(
        CancellationToken cancellationToken,
        [FromQuery] int weeks = DashboardMath.DefaultWeeks,
        [FromQuery] int topAssets = DashboardMath.DefaultTopAssets)
        => Map(await dashboard.GetExecutiveAsync(weeks, topAssets, cancellationToken));

    private IActionResult Map<T>(DashboardResult<T> result)
    {
        if (result.Outcome == DashboardOutcome.Success)
            return Ok(result.Value);
        if (result.Outcome != DashboardOutcome.ValidationFailed)
            throw new InvalidOperationException($"Unhandled dashboard outcome '{result.Outcome}'.");
        return DashboardHttpResults.Validation(HttpContext, result.Errors);
    }
}

internal static class DashboardHttpResults
{
    internal static ObjectResult Validation(HttpContext context, IReadOnlyDictionary<string, string[]>? errors)
    {
        var problem = new ValidationProblemDetails(
            errors?.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string, string[]>())
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid dashboard query",
            Detail = "One or more fields are invalid.",
        };
        problem.Extensions["code"] = DashboardErrorCodes.ValidationError;
        problem.Extensions["requestId"] = AuthHttpContext.GetRequestId(context);
        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }
}
