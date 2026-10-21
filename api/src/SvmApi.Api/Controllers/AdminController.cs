using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SvmApi.Api.Auth;
using SvmApi.Application.Admin;
using SvmApi.Application.Auth;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminController(IAdminService admin) : ControllerBase
{
    [HttpGet("master-data")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminMasterData)]
    public async Task<IActionResult> MasterDataTypes(CancellationToken ct)
        => Map(await admin.ListMasterDataTypesAsync(ct));

    [HttpGet("master-data/{type}")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminMasterData)]
    public async Task<IActionResult> MasterData(string type, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Map(await admin.ListMasterDataAsync(type, from, to, ct));

    [HttpPost("master-data/{type}")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminMasterData)]
    public async Task<IActionResult> CreateMasterData(string type, [FromBody] MasterDataRequest? request, CancellationToken ct)
        => Map(await admin.CreateMasterDataAsync(type, request, ct));

    [HttpPut("master-data/{type}/{id:guid}")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminMasterData)]
    public async Task<IActionResult> UpdateMasterData(string type, Guid id, [FromBody] MasterDataRequest? request, CancellationToken ct)
        => Map(await admin.UpdateMasterDataAsync(type, id, request, ct));

    [HttpGet("status-transitions")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminWorkflow)]
    public async Task<IActionResult> StatusTransitions(CancellationToken ct)
        => Map(await admin.ListStatusTransitionsAsync(ct));

    [HttpPost("status-transitions")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminWorkflow)]
    public async Task<IActionResult> CreateStatusTransition([FromBody] CreateStatusTransitionRequest? request, CancellationToken ct)
        => Map(await admin.CreateStatusTransitionAsync(request, ct));

    [HttpPut("status-transitions/{id:guid}")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminWorkflow)]
    public async Task<IActionResult> UpdateStatusTransition(Guid id, [FromBody] UpdateStatusTransitionRequest? request, CancellationToken ct)
        => Map(await admin.UpdateStatusTransitionAsync(id, request, ct));

    [HttpGet("sla-policies")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminSla)]
    public async Task<IActionResult> SlaPolicies(CancellationToken ct)
        => Map(await admin.ListSlaPoliciesAsync(ct));

    [HttpPost("sla-policies")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminSla)]
    public async Task<IActionResult> CreateSlaPolicy([FromBody] SlaPolicyRequest? request, CancellationToken ct)
        => Map(await admin.CreateSlaPolicyAsync(request, ct));

    [HttpPut("sla-policies/{id:guid}")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminSla)]
    public async Task<IActionResult> UpdateSlaPolicy(Guid id, [FromBody] SlaPolicyRequest? request, CancellationToken ct)
        => Map(await admin.UpdateSlaPolicyAsync(id, request, ct));

    [HttpGet("risk-weights")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminRisk)]
    public async Task<IActionResult> RiskWeights(CancellationToken ct)
        => Map(await admin.GetRiskWeightsAsync(ct));

    [HttpPut("risk-weights")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminRisk)]
    public async Task<IActionResult> ReplaceRiskWeights([FromBody] ReplaceRiskWeightsRequest? request, CancellationToken ct)
        => Map(await admin.ReplaceRiskWeightsAsync(request, ct));

    [HttpPost("risk-weights/validate")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminRisk)]
    public async Task<IActionResult> ValidateRiskWeights([FromBody] ReplaceRiskWeightsRequest? request, CancellationToken ct)
        => Map(await admin.ValidateRiskWeightsAsync(request, ct));

    [HttpGet("risk-thresholds")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminRisk)]
    public async Task<IActionResult> RiskThresholds(CancellationToken ct)
        => Map(await admin.GetRiskThresholdsAsync(ct));

    [HttpPut("risk-thresholds")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminRisk)]
    public async Task<IActionResult> ReplaceRiskThresholds([FromBody] ReplaceRiskThresholdsRequest? request, CancellationToken ct)
        => Map(await admin.ReplaceRiskThresholdsAsync(request, ct));

    [HttpGet("assignment-rules")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminAssignment)]
    public async Task<IActionResult> AssignmentRules(CancellationToken ct)
        => Map(await admin.ListAssignmentRulesAsync(ct));

    [HttpPost("assignment-rules")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminAssignment)]
    public async Task<IActionResult> CreateAssignmentRule([FromBody] AssignmentRuleRequest? request, CancellationToken ct)
        => Map(await admin.CreateAssignmentRuleAsync(request, ct));

    [HttpPut("assignment-rules/{id:guid}")]
    [Authorize(Policy = PermissionPolicies.Prefix + PermissionCatalog.AdminAssignment)]
    public async Task<IActionResult> UpdateAssignmentRule(Guid id, [FromBody] AssignmentRuleRequest? request, CancellationToken ct)
        => Map(await admin.UpdateAssignmentRuleAsync(id, request, ct));

    private IActionResult Map<T>(AdminResult<T> result)
    {
        if (result.Succeeded) return Ok(result.Value);
        var status = result.Outcome switch
        {
            AdminOutcome.ValidationFailed => StatusCodes.Status400BadRequest,
            AdminOutcome.Forbidden => StatusCodes.Status403Forbidden,
            AdminOutcome.NotFound => StatusCodes.Status404NotFound,
            AdminOutcome.Conflict => StatusCodes.Status409Conflict,
            AdminOutcome.Unprocessable => StatusCodes.Status422UnprocessableEntity,
            _ => throw new InvalidOperationException($"Unhandled admin outcome '{result.Outcome}'."),
        };
        ProblemDetails problem = result.Errors is { } errors
            ? new ValidationProblemDetails(errors.ToDictionary(x => x.Key, x => x.Value))
            : new ProblemDetails();
        problem.Status = status;
        problem.Title = result.Code;
        problem.Detail = result.Message ?? "The administration request could not be completed.";
        problem.Extensions["code"] = result.Code;
        problem.Extensions["requestId"] = AuthHttpContext.GetRequestId(HttpContext);
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
