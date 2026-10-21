using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SvmApi.Api.Middleware;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Application.Reference;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Api.Controllers;

/// <summary>
/// Reference data แบบอ่านอย่างเดียว (C5 · US-013/014 · FR-13/14/18). ต้องมีสิทธิ์ <c>reference.read</c>.
/// ข้อมูลเป็น master seed ระดับองค์กร (ไม่ผูก data scope).
/// </summary>
[ApiController]
[Route("api/v1/reference")]
public sealed class ReferenceController(IReferenceDataService referenceData) : ControllerBase
{
    private const int MaxPageSize = 100;
    private const int MaxSearchLength = 100;

    private const string ReadPolicy = PermissionPolicies.Prefix + PermissionCatalog.ReferenceRead;

    /// <summary>ค้นหา/แสดงรายการ CVE (กรอง KEV ได้) เรียงตาม cveId.</summary>
    [HttpGet("cve")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType<PagedResult<CveDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<CveDto>>> SearchCves(
        [FromQuery] string? q,
        [FromQuery] bool? isKev,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidatePaging(page, pageSize, q);
        if (errors.Count > 0)
        {
            return InvalidQuery(errors);
        }

        var result = await referenceData.SearchCvesAsync(new CveQuery(q, isKev, page, pageSize), cancellationToken);
        return Ok(result);
    }

    /// <summary>รายละเอียด CVE (รวม isKev).</summary>
    [HttpGet("cve/{cveId}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType<CveDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CveDto>> GetCve(string cveId, CancellationToken cancellationToken)
    {
        if (!ReferenceIds.IsValidCveId(cveId))
        {
            return InvalidQuery(new Dictionary<string, string[]>
            {
                ["cveId"] = ["cveId must match CVE-YYYY-NNNN."],
            });
        }

        var cve = await referenceData.GetCveAsync(cveId, cancellationToken);
        return cve is null ? NotFoundProblem("CVE", cveId) : Ok(cve);
    }

    /// <summary>ค้นหา/แสดงรายการ CWE เรียงตาม cweId.</summary>
    [HttpGet("cwe")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType<PagedResult<CweDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<CweDto>>> SearchCwes(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidatePaging(page, pageSize, q);
        if (errors.Count > 0)
        {
            return InvalidQuery(errors);
        }

        var result = await referenceData.SearchCwesAsync(new CweQuery(q, page, pageSize), cancellationToken);
        return Ok(result);
    }

    /// <summary>รายละเอียด CWE.</summary>
    [HttpGet("cwe/{cweId}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType<CweDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CweDto>> GetCwe(string cweId, CancellationToken cancellationToken)
    {
        if (!ReferenceIds.IsValidCweId(cweId))
        {
            return InvalidQuery(new Dictionary<string, string[]>
            {
                ["cweId"] = ["cweId must match CWE-NNN."],
            });
        }

        var cwe = await referenceData.GetCweAsync(cweId, cancellationToken);
        return cwe is null ? NotFoundProblem("CWE", cweId) : Ok(cwe);
    }

    private static Dictionary<string, string[]> ValidatePaging(int page, int pageSize, string? search)
    {
        var errors = new Dictionary<string, string[]>();

        if (page < 1)
        {
            errors["page"] = ["page must be >= 1."];
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];
        }

        if (search is { Length: > MaxSearchLength })
        {
            errors["q"] = [$"q must not exceed {MaxSearchLength} characters."];
        }

        return errors;
    }

    private ObjectResult NotFoundProblem(string kind, string id)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = $"{kind} not found",
            Detail = $"{kind} '{id}' was not found in reference data.",
        };
        problem.Extensions["code"] = "REFERENCE_NOT_FOUND";
        problem.Extensions["requestId"] = RequestId();

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status404NotFound,
            ContentTypes = { "application/problem+json" },
        };
    }

    private ObjectResult InvalidQuery(Dictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid reference query",
            Detail = "One or more request parameters are invalid.",
        };
        problem.Extensions["code"] = "VALIDATION_ERROR";
        problem.Extensions["requestId"] = RequestId();

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }

    private string RequestId()
        => Response.Headers[RequestIdMiddleware.HeaderName].FirstOrDefault() ?? HttpContext.TraceIdentifier;
}
