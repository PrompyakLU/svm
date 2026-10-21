using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SvmApi.Api.Middleware;
using SvmApi.Application.Audit;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Infrastructure.Auth.DataScope;

namespace SvmApi.Api.Controllers;

/// <summary>
/// ค้นหา/ตรวจ audit log (C13 · US-032 · FR-68, BR-33, EX-100). ต้องมีสิทธิ์ <c>audit.read</c>.
/// การค้นหาถูกบันทึกเป็น audit ของตัวเอง (action <c>audit.search</c>) โดยไม่เก็บค่า filter.
/// </summary>
[ApiController]
[Route("api/v1/audit")]
public sealed class AuditController(IAuditService auditService, ICurrentUser currentUser) : ControllerBase
{
    private const int MaxActionLength = 50;
    private const int MaxObjectTypeLength = 100;
    private const int MaxObjectIdLength = 100;
    private const int MaxPageSize = 200;

    // PermissionPolicies.For(...) เป็นเมธอด ใช้ใน attribute ไม่ได้ -> ใช้ const Prefix ต่อ string (ผลลัพธ์เท่ากัน)
    private const string ReadPolicy = PermissionPolicies.Prefix + "audit.read";

    /// <summary>ค้นหา audit (object/user/action/ช่วงเวลา) เรียงใหม่→เก่า.</summary>
    [HttpGet]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType<PagedResult<AuditRecordDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<AuditRecordDto>>> Search(
        [FromQuery] string? objectType,
        [FromQuery] string? objectId,
        [FromQuery] Guid? userId,
        [FromQuery] string? action,
        [FromQuery(Name = "from")] DateTime? fromTime,
        [FromQuery(Name = "to")] DateTime? toTime,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!HasOrganizationScope())
        {
            return OutOfScope();
        }

        var errors = new Dictionary<string, string[]>();

        if (page < 1)
        {
            errors["page"] = ["page must be >= 1."];
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];
        }

        if (fromTime is not null && toTime is not null && fromTime > toTime)
        {
            errors["from"] = ["from must be earlier than or equal to to."];
        }

        if (objectType is { Length: > MaxObjectTypeLength })
        {
            errors["objectType"] = [$"objectType must not exceed {MaxObjectTypeLength} characters."];
        }

        if (objectId is { Length: > MaxObjectIdLength })
        {
            errors["objectId"] = [$"objectId must not exceed {MaxObjectIdLength} characters."];
        }

        if (action is { Length: > MaxActionLength })
        {
            errors["action"] = [$"action must not exceed {MaxActionLength} characters."];
        }

        if (errors.Count > 0)
        {
            return InvalidQuery(errors);
        }

        SetNoStore();

        var query = new AuditQuery(objectType, objectId, userId, action, fromTime, toTime, page, pageSize);
        var result = await auditService.SearchAsync(query, cancellationToken);

        // บันทึกการค้นหา (เก็บเฉพาะชื่อ filter ที่ใช้ ไม่เก็บค่า). ถ้าบันทึกไม่ได้ = ปฏิเสธ (EX-98) ไม่คืนผลลัพธ์
        var usedFilters = new List<string>();
        if (!string.IsNullOrWhiteSpace(objectType)) { usedFilters.Add("objectType"); }
        if (!string.IsNullOrWhiteSpace(objectId)) { usedFilters.Add("objectId"); }
        if (userId is not null) { usedFilters.Add("userId"); }
        if (!string.IsNullOrWhiteSpace(action)) { usedFilters.Add("action"); }
        if (fromTime is not null) { usedFilters.Add("from"); }
        if (toTime is not null) { usedFilters.Add("to"); }

        var detail = JsonSerializer.Serialize(new { filters = usedFilters, page = result.Page, pageSize = result.PageSize });

        await auditService.RecordAsync(
            new AuditEntry(
                currentUser.UserId,
                "audit.search",
                "AuditLog",
                "search",
                null,
                detail,
                "Success",
                nameof(AuditController)),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>ตรวจความสมบูรณ์ของ hash chain ทั้งตาราง (EX-100).</summary>
    [HttpGet("verify")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType<AuditChainVerification>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuditChainVerification>> Verify(CancellationToken cancellationToken)
    {
        if (!HasOrganizationScope())
        {
            return OutOfScope();
        }

        SetNoStore();
        var result = await auditService.VerifyChainAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Data scope ของ audit (US-032, BR-30): audit trail เป็นข้อมูลระดับองค์กร (hash chain เดียวทั้งตาราง) จึงอนุญาต
    /// เฉพาะผู้ที่มี scope <c>Organization:*</c> อย่างชัดเจน (เช่น AUDITOR). ผู้ที่มีเพียง BusinessUnit/Asset/Responsibility
    /// หรือไม่มี scope = ไม่เห็นอะไรเลย (fail closed, ตามกติกา "ไม่มี grant = ไม่เห็น" ของ DataScopeExtensions).
    /// </summary>
    private bool HasOrganizationScope()
        => currentUser.Scopes.Any(s =>
            string.Equals(s.Type, DataScopeExtensions.OrganizationType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Value?.Trim(), DataScopeExtensions.OrganizationAll, StringComparison.Ordinal));

    private ObjectResult OutOfScope()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Forbidden",
            Detail = "Your data scope does not include the audit trail.",
        };
        problem.Extensions["code"] = "OUT_OF_SCOPE";
        problem.Extensions["requestId"] = Response.Headers[RequestIdMiddleware.HeaderName].FirstOrDefault()
                                          ?? HttpContext.TraceIdentifier;

        SetNoStore();
        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { "application/problem+json" },
        };
    }

    private void SetNoStore()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
    }

    private ObjectResult InvalidQuery(Dictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid audit query",
            Detail = "One or more query parameters are invalid.",
        };
        problem.Extensions["code"] = "VALIDATION_ERROR";
        problem.Extensions["requestId"] = Response.Headers[RequestIdMiddleware.HeaderName].FirstOrDefault()
                                          ?? HttpContext.TraceIdentifier;

        SetNoStore();
        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }
}
