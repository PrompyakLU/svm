using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SvmApi.Api.Auth;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Application.Intake;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Api.Controllers;

/// <summary>
/// Intake (C3 · US-007/008/009 · FR-05/07/08) + Data Quality / dedup queue (C4 · US-010/011/012 · FR-09..12).
/// ทุก list/by-id ถูกกรองด้วย data scope ใน <see cref="IIntakeService"/>; id นอก scope ตอบ 404 + บันทึก denied-access (EX-07).
/// การอัปโหลดถูกจำกัดขนาด (config <c>Intake:MaxFileBytes</c>, เพดาน <see cref="IntakeOptions.HardMaxFileBytes"/>) และจำนวนแถว,
/// รับเฉพาะ .csv/.json ที่เนื้อหาตรงชนิด; error ไม่สะท้อนเนื้อหาไฟล์.
/// </summary>
[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FindingIntakeController(
    IIntakeService intake,
    IAuthService authService,
    ICurrentUser currentUser,
    IOptions<IntakeOptions> options,
    ILogger<FindingIntakeController> logger) : ControllerBase
{
    private const string ImportPolicy = PermissionPolicies.Prefix + PermissionCatalog.FindingImport;
    private const string CreatePolicy = PermissionPolicies.Prefix + PermissionCatalog.FindingCreate;
    private const string DedupPolicy = PermissionPolicies.Prefix + PermissionCatalog.FindingDedup;

    // เผื่อ overhead ของ multipart boundary/header เหนือขนาดไฟล์
    private const long MultipartOverheadBytes = 64 * 1024;

    /// <summary>ดาวน์โหลด template นำเข้า (CSV ค่าเริ่มต้น หรือ JSON) — สร้างจากนิยาม column เดียวกับ validation (US-009).</summary>
    [HttpGet("api/v1/findings/import-template")]
    [Authorize(Policy = ImportPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public IActionResult GetTemplate([FromQuery] string? format = "csv")
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        var f = (format ?? "csv").Trim();

        if (string.Equals(f, "csv", StringComparison.OrdinalIgnoreCase))
        {
            return File(IntakeTemplate.BuildCsv(), "text/csv; charset=utf-8", "svm-findings-template.csv");
        }

        if (string.Equals(f, "json", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(IntakeTemplate.BuildJson());
        }

        return Validation(new Dictionary<string, string[]> { ["format"] = [IntakeErrorCodes.InvalidFormat] }, IntakeErrorCodes.InvalidFormat);
    }

    /// <summary>
    /// นำเข้า finding จากไฟล์ CSV/JSON (multipart: <c>file</c>, <c>transactionId</c> optional) — ผล Success/Partial/Failed รายแถว (EX-11..18).
    /// ส่ง transactionId เดิมซ้ำ = ได้ผลเดิม (<c>replayed=true</c>) ไม่สร้างข้อมูลซ้ำ (BR-36).
    /// </summary>
    [HttpPost("api/v1/findings/import")]
    [Authorize(Policy = ImportPolicy)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(IntakeOptions.HardMaxFileBytes + MultipartOverheadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = IntakeOptions.HardMaxFileBytes + MultipartOverheadBytes)]
    [ProducesResponseType<ImportResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> Import(IFormFile? file, [FromForm] string? transactionId, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            var code = file is null ? IntakeErrorCodes.FileMissing : IntakeErrorCodes.FileEmpty;
            return Validation(new Dictionary<string, string[]> { ["file"] = [code] }, code);
        }

        var maxBytes = options.Value.EffectiveMaxFileBytes;
        if (file.Length > maxBytes)
        {
            return Validation(
                new Dictionary<string, string[]> { ["file"] = [$"{IntakeErrorCodes.FileTooLarge}: max {maxBytes} bytes"] },
                IntakeErrorCodes.FileTooLarge);
        }

        var buffer = new byte[(int)file.Length];
        await using (var stream = file.OpenReadStream())
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken);
        }

        return await MapAsync(
            await intake.ImportAsync(new ImportFile(file.FileName, buffer), transactionId, cancellationToken),
            value => Ok(value));
    }

    /// <summary>ลงทะเบียน finding ด้วยมือ (US-008) — ผ่าน validation/normalize/dedup ชุดเดียวกับ import; 201 พร้อมผล dedup.</summary>
    [HttpPost("api/v1/findings/manual")]
    [Authorize(Policy = CreatePolicy)]
    [ProducesResponseType<ManualFindingResultDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RegisterManual([FromBody] ManualFindingRequest? request, CancellationToken cancellationToken)
        => await MapAsync(
            await intake.RegisterManualAsync(request!, cancellationToken),
            value => StatusCode(StatusCodes.Status201Created, value));

    /// <summary>รายการ import batch (ผู้ใช้ scope แคบเห็นเฉพาะ batch ของตน).</summary>
    [HttpGet("api/v1/import-batches")]
    [Authorize(Policy = ImportPolicy)]
    [ProducesResponseType<PagedResult<ImportBatchDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListBatches(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
        => await MapAsync(
            await intake.ListBatchesAsync(new ImportBatchQuery(status, page, pageSize), cancellationToken),
            value => Ok(value));

    /// <summary>รายละเอียด batch + error รายแถว (เลขแถว + รหัส/ชื่อ field เท่านั้น).</summary>
    [HttpGet("api/v1/import-batches/{id:guid}")]
    [Authorize(Policy = ImportPolicy)]
    [ProducesResponseType<ImportBatchDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBatch(Guid id, CancellationToken cancellationToken)
        => await MapAsync(await intake.GetBatchAsync(id, cancellationToken), value => Ok(value));

    /// <summary>คิว duplicate ที่ตัดสินอัตโนมัติไม่ได้ (EX-45) พร้อม candidate instance.</summary>
    [HttpGet("api/v1/dedup-queue")]
    [Authorize(Policy = DedupPolicy)]
    [ProducesResponseType<PagedResult<DedupQueueItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDedupQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
        => await MapAsync(
            await intake.GetDedupQueueAsync(new DedupQueueQuery(page, pageSize), cancellationToken),
            value => Ok(value));

    /// <summary>ตัดสิน duplicate: <c>Duplicate</c> + instanceId (เชื่อม instance เดิม) หรือ <c>Distinct</c> (สร้าง instance ใหม่).</summary>
    [HttpPost("api/v1/findings/{id:guid}/resolve-duplicate")]
    [Authorize(Policy = DedupPolicy)]
    [ProducesResponseType<ResolveDuplicateResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResolveDuplicate(
        Guid id, [FromBody] ResolveDuplicateRequest? request, CancellationToken cancellationToken)
        => await MapAsync(await intake.ResolveDuplicateAsync(id, request!, cancellationToken), value => Ok(value));

    // ------------------------------------------------------------ mapping

    private async Task<IActionResult> MapAsync<T>(IntakeResult<T> result, Func<T, IActionResult> onSuccess)
    {
        switch (result.Outcome)
        {
            case IntakeOutcome.Success:
                return onSuccess(result.Value!);

            case IntakeOutcome.ValidationFailed:
                return Validation(result.Errors ?? new Dictionary<string, string[]>(), result.Code ?? IntakeErrorCodes.ValidationError);

            case IntakeOutcome.OutOfScope:
                // EX-07: บันทึก denied-access แต่ตอบเหมือนไม่พบ (ไม่เปิดเผยการมีอยู่)
                await RecordDeniedAsync();
                return result.Errors is { } errors
                    ? Validation(errors, result.Code ?? IntakeErrorCodes.ValidationError)
                    : NotFoundProblem(result.Code);

            case IntakeOutcome.NotFound:
                return NotFoundProblem(result.Code);

            case IntakeOutcome.Conflict:
                return AuthProblem.ToResult(
                    HttpContext, StatusCodes.Status409Conflict, "Conflict",
                    result.Message ?? "The request conflicts with current state.", result.Code ?? "CONFLICT");

            default:
                throw new InvalidOperationException($"Unhandled intake outcome '{result.Outcome}'.");
        }
    }

    private ObjectResult NotFoundProblem(string? code)
        => AuthProblem.ToResult(
            HttpContext, StatusCodes.Status404NotFound, "Not found", "The requested record was not found.",
            code ?? IntakeErrorCodes.FindingNotFound);

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
                "intake.access.denied.audit_failed requestId={RequestId} reason={Reason}",
                AuthHttpContext.GetRequestId(HttpContext), ex.GetType().Name);
        }
    }

    /// <summary>400 (หรือ 413/415 ตามรหัส) แบบ ValidationProblemDetails พร้อม <c>code</c> + <c>requestId</c>.</summary>
    private ObjectResult Validation(IReadOnlyDictionary<string, string[]> errors, string code)
    {
        var status = code switch
        {
            IntakeErrorCodes.FileTooLarge => StatusCodes.Status413PayloadTooLarge,
            IntakeErrorCodes.UnsupportedFormat => StatusCodes.Status415UnsupportedMediaType,
            _ => StatusCodes.Status400BadRequest,
        };

        var problem = new ValidationProblemDetails(errors.ToDictionary(p => p.Key, p => p.Value))
        {
            Status = status,
            Title = "Invalid intake request",
            Detail = "One or more fields are invalid.",
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
