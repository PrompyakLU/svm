using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SvmApi.Api.Auth;
using SvmApi.Application.Assets;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Api.Controllers;

/// <summary>
/// Asset inventory / ownership / criticality / CMDB sync (C2 · US-004/005/006 · FR-01..04).
/// ทุก list/by-id ถูกกรองด้วย data scope ใน <see cref="IAssetService"/>. การเข้า id นอก scope (EX-07) ตอบ 404 เหมือนไม่มีอยู่
/// (api-spec: "404 not found/scope-hidden" — ไม่เปิดเผยการมีอยู่) และบันทึก denied-access ผ่าน
/// <see cref="IAuthService.RecordDeniedAsync"/> (เส้นทางเดียวกับ SvmAuthorizationResultHandler).
/// </summary>
[ApiController]
[Route("api/v1/assets")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssetController(
    IAssetService assets,
    IAuthService authService,
    ICurrentUser currentUser,
    ILogger<AssetController> logger) : ControllerBase
{
    private const string ReadPolicy = PermissionPolicies.Prefix + PermissionCatalog.AssetRead;
    private const string WritePolicy = PermissionPolicies.Prefix + PermissionCatalog.AssetWrite;
    private const string CriticalityPolicy = PermissionPolicies.Prefix + PermissionCatalog.AssetCriticality;
    private const string SyncPolicy = PermissionPolicies.Prefix + PermissionCatalog.AssetSync;

    /// <summary>รายการ asset (ใน data scope) พร้อม filter/ค้นหา/แบ่งหน้า.</summary>
    [HttpGet]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType<PagedResult<AssetDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery(Name = "q")] string? search,
        [FromQuery] string? assetType,
        [FromQuery] string? environment,
        [FromQuery] string? businessUnit,
        [FromQuery] string? criticality,
        [FromQuery] bool? internetExposure,
        [FromQuery] string? status,
        [FromQuery] bool? criticalityMissing,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var query = new AssetQuery(
            search, assetType, environment, businessUnit, criticality, internetExposure, status,
            criticalityMissing, page, pageSize);

        return await MapAsync(await assets.SearchAsync(query, cancellationToken), value => Ok(value));
    }

    /// <summary>รายละเอียด asset (404 ทั้งกรณีไม่มีอยู่และอยู่นอก scope).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType<AssetDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => await MapAsync(await assets.GetAsync(id, cancellationToken), value => Ok(value));

    /// <summary>สร้าง asset (ไม่รับ criticality — ใช้ PUT /{id}/criticality).</summary>
    [HttpPost]
    [Authorize(Policy = WritePolicy)]
    [ProducesResponseType<AssetDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateAssetRequest request, CancellationToken cancellationToken)
        => await MapAsync(
            await assets.CreateAsync(request, cancellationToken),
            value => CreatedAtAction(nameof(Get), new { id = value.Id }, value));

    /// <summary>แก้ไข asset และ/หรือ ownership (PUT = แทนที่ field ที่แก้ไขได้).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = WritePolicy)]
    [ProducesResponseType<AssetDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateAssetRequest request, CancellationToken cancellationToken)
        => await MapAsync(await assets.UpdateAsync(id, request, cancellationToken), value => Ok(value));

    /// <summary>กำหนด/ยืนยัน criticality พร้อมเหตุผล (FR-04) — บันทึกผู้แก้ไข/เวลา/เหตุผลใน audit.</summary>
    [HttpPut("{id:guid}/criticality")]
    [Authorize(Policy = CriticalityPolicy)]
    [ProducesResponseType<AssetDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetCriticality(
        Guid id, [FromBody] CriticalityRequest request, CancellationToken cancellationToken)
        => await MapAsync(await assets.SetCriticalityAsync(id, request, cancellationToken), value => Ok(value));

    /// <summary>ประวัติ criticality (ผู้แก้ไข/เวลา/เหตุผล — FR-04) จาก audit trail; 404 ทั้งกรณีไม่มีอยู่และอยู่นอก scope.</summary>
    [HttpGet("{id:guid}/criticality-history")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType<IReadOnlyList<CriticalityHistoryEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCriticalityHistory(Guid id, CancellationToken cancellationToken)
        => await MapAsync(await assets.GetCriticalityHistoryAsync(id, cancellationToken), value => Ok(value));

    /// <summary>สั่ง sync จาก CMDB (mock) — 202 พร้อมสรุปผล; CMDB ไม่พร้อม = 202 status IntegrationError (EX-30).</summary>
    [HttpPost("sync")]
    [Authorize(Policy = SyncPolicy)]
    [ProducesResponseType<AssetSyncResult>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Sync(CancellationToken cancellationToken)
        => await MapAsync(await assets.SyncAsync(cancellationToken), value => Accepted(value));

    // ------------------------------------------------------------ mapping

    private async Task<IActionResult> MapAsync<T>(AssetResult<T> result, Func<T, IActionResult> onSuccess)
    {
        switch (result.Outcome)
        {
            case AssetOutcome.Success:
                return onSuccess(result.Value!);

            case AssetOutcome.ValidationFailed:
                return Validation(result.Errors ?? new Dictionary<string, string[]>());

            case AssetOutcome.OutOfScope:
                // EX-07: เข้า record นอก scope ผ่าน id -> audit แล้วตอบเหมือนไม่พบ (ไม่เปิดเผยการมีอยู่)
                await RecordDeniedAsync();
                return ProblemResult(StatusCodes.Status404NotFound, "Asset not found", "The asset was not found.", AssetErrorCodes.NotFound);

            case AssetOutcome.ScopeViolation:
                await RecordDeniedAsync();
                return ProblemResult(
                    StatusCodes.Status403Forbidden, "Forbidden", "The asset is outside your data scope.", AssetErrorCodes.OutOfScope);

            case AssetOutcome.NotFound:
                return ProblemResult(StatusCodes.Status404NotFound, "Asset not found", "The asset was not found.", AssetErrorCodes.NotFound);

            case AssetOutcome.Conflict:
                return ProblemResult(
                    StatusCodes.Status409Conflict, "Conflict", result.Message ?? "The request conflicts with current state.",
                    result.Code ?? "CONFLICT");

            case AssetOutcome.BusinessRule:
                return ProblemResult(
                    StatusCodes.Status422UnprocessableEntity, "Business rule violation",
                    result.Message ?? "The request violates a business rule.", result.Code ?? "BUSINESS_RULE");

            default:
                throw new InvalidOperationException($"Unhandled asset outcome '{result.Outcome}'.");
        }
    }

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
            // audit ล้มเหลวต้องไม่เปลี่ยนผลปฏิเสธเป็น 500 — log เฉพาะชนิด exception
            logger.LogError(
                "asset.access.denied.audit_failed requestId={RequestId} reason={Reason}",
                AuthHttpContext.GetRequestId(HttpContext), ex.GetType().Name);
        }
    }

    private ObjectResult ProblemResult(int status, string title, string detail, string code)
        => AuthProblem.ToResult(HttpContext, status, title, detail, code);

    private ObjectResult Validation(IReadOnlyDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors.ToDictionary(p => p.Key, p => p.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid asset request",
            Detail = "One or more fields are invalid.",
        };
        problem.Extensions["code"] = AssetErrorCodes.ValidationError;
        problem.Extensions["requestId"] = AuthHttpContext.GetRequestId(HttpContext);

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }
}
