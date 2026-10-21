using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SvmApi.Application.Audit;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Application.Remediation;
using SvmApi.Domain.Remediation;
using SvmApi.Infrastructure.Auth.DataScope;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Remediation;

/// <summary>
/// <see cref="IEvidenceService"/> (C8 · US-022 · FR-31 · BR-42 · NFR-18 · EX-21/22).
/// ลำดับการรับไฟล์: scope → ตรวจชื่อ/นามสกุล/ขนาด/magic bytes (<see cref="EvidenceFileValidator"/>) → SHA-256 →
/// malware scan (mock) <b>ก่อน</b>เขียนไฟล์ → เขียนด้วยชื่อที่สร้างเอง (GUID) → แถว <c>trans_evidence</c> + audit ใน transaction เดียว.
/// ไฟล์ที่ Infected/Unscannable ถูกปฏิเสธ (422) และ <b>ไม่ถูกเก็บ</b> — บันทึกเฉพาะ audit (result = Rejected) ที่มี hash/ขนาด/นามสกุล ไม่มีเนื้อหาหรือชื่อไฟล์.
/// ทุกแถวที่เก็บจึงมี <see cref="ScanStatus.Clean"/> (ผ่าน <c>DbEvidenceChecker</c> ของ Workflow). version: ชื่อไฟล์เดียวกันใน instance/task เดียวกัน → +1 (ไม่ทับของเดิม).
/// <c>trans_evidence</c> ไม่มีคอลัมน์ hash — SHA-256 อยู่ใน audit และใน response ตอนอัปโหลดเท่านั้น.
/// </summary>
public sealed class EvidenceService(
    SvmDbContext db,
    IUnitOfWork unitOfWork,
    IAuditService audit,
    IFileStorage storage,
    IMalwareScanner scanner,
    IOptions<EvidenceOptions> options,
    ICurrentUser user,
    TimeProvider clock,
    ILogger<EvidenceService> logger) : IEvidenceService
{
    public const string AuditSource = "EvidenceService";
    public const string ActionUpload = "evidence.upload";
    public const string ActionReject = "evidence.reject";
    private const int MaxListRows = 500;

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<RemediationResult<EvidenceDto>> UploadAsync(
        Guid instanceId, EvidenceUploadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actorId = user.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

        var scopeFailure = await CheckScopeAsync<EvidenceDto>(instanceId, cancellationToken);
        if (scopeFailure is not null)
        {
            return scopeFailure;
        }

        if (request.Content is null)
        {
            return RemediationResult<EvidenceDto>.Invalid(
                new Dictionary<string, string[]> { ["file"] = [RemediationErrorCodes.FileMissing] }, RemediationErrorCodes.FileMissing);
        }

        var content = request.Content;
        var validation = EvidenceFileValidator.Validate(request.FileName, content, options.Value.EffectiveMaxFileBytes);
        var sha256 = content.Length > 0 ? EvidenceFileValidator.Sha256Hex(content) : null;

        if (!validation.IsValid)
        {
            if (validation.ErrorCode == RemediationErrorCodes.ContentMismatch)
            {
                await RecordRejectedAsync(actorId, instanceId, validation.ErrorCode, content.Length, null, sha256, cancellationToken);
            }

            return RemediationResult<EvidenceDto>.Fail(validation.Outcome, validation.ErrorCode!);
        }

        if (request.TaskId is { } requestedTaskId
            && !await db.RemediationTasks.AsNoTracking().AnyAsync(t => t.Id == requestedTaskId && t.InstanceId == instanceId, cancellationToken))
        {
            return RemediationResult<EvidenceDto>.Invalid(
                new Dictionary<string, string[]> { ["taskId"] = [RemediationErrorCodes.TaskMismatch] }, RemediationErrorCodes.TaskMismatch);
        }

        // --- malware scan ก่อนรับไฟล์ (NFR-18): error ของ scanner = ตรวจไม่ได้ (fail-closed)
        ScanStatus scan;
        try
        {
            scan = await scanner.ScanAsync(content, validation.Extension, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("evidence.scan.error instanceId={InstanceId} reason={Reason}", instanceId, ex.GetType().Name);
            scan = ScanStatus.Unscannable;
        }

        if (scan != ScanStatus.Clean)
        {
            var code = scan == ScanStatus.Infected ? RemediationErrorCodes.Malware : RemediationErrorCodes.Unscannable;
            await RecordRejectedAsync(actorId, instanceId, code, content.Length, validation.Extension, sha256, cancellationToken);
            logger.LogWarning("evidence.rejected instanceId={InstanceId} code={Code} scan={Scan}", instanceId, code, scan);

            return RemediationResult<EvidenceDto>.Fail(
                RemediationOutcome.Unprocessable, code,
                scan == ScanStatus.Infected
                    ? "The file was rejected because malware was detected."
                    : "The file was rejected because it could not be scanned (e.g. encrypted).");
        }

        // --- เก็บไฟล์ด้วยชื่อที่สร้างเอง แล้วบันทึก DB; ถ้า DB ล้มเหลวให้ลบไฟล์คืน
        var evidenceId = Guid.NewGuid();
        var storageKey = EvidenceVersioning.BuildStorageKey(instanceId, evidenceId, validation.Extension);
        await storage.SaveAsync(storageKey, content, cancellationToken);

        try
        {
            var dto = await unitOfWork.ExecuteAsync(async ct =>
            {
                db.ChangeTracker.Clear(); // retry-safe

                // ล็อกแถว instance ชั่วคราวเพื่อ serialize การคำนวณเลขเวอร์ชันของ instance เดียวกัน
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM [trans_vulnerabilityInstance] WITH (UPDLOCK, ROWLOCK) WHERE [id] = {instanceId}", ct);

                var seriesKey = EvidenceVersioning.SeriesKey(validation.SafeFileName);
                var taskId = request.TaskId;
                var existing = await db.Evidences.AsNoTracking()
                    .Where(e => e.InstanceId == instanceId && e.TaskId == taskId && e.FileName.ToUpper() == seriesKey)
                    .Select(e => e.Version)
                    .ToListAsync(ct);
                var version = EvidenceVersioning.NextVersion(existing);
                var now = clock.GetUtcNow().UtcDateTime;

                var row = new Evidence
                {
                    Id = evidenceId,
                    InstanceId = instanceId,
                    TaskId = taskId,
                    FileName = validation.SafeFileName,
                    StoragePath = storageKey,
                    ContentType = validation.ContentType,
                    FileSize = content.Length,
                    ScanStatus = ScanStatus.Clean,
                    Version = version,
                    UploadedBy = actorId,
                    UploadedAt = now,
                };
                db.Evidences.Add(row);

                await audit.AppendAsync(
                    new AuditEntry(
                        actorId, ActionUpload, "Evidence", evidenceId.ToString(),
                        null,
                        JsonSerializer.Serialize(
                            new
                            {
                                instanceId,
                                taskId,
                                version,
                                size = row.FileSize,
                                extension = validation.Extension,
                                sha256,
                                scanStatus = nameof(ScanStatus.Clean),
                            }, Json),
                        "Success", AuditSource),
                    ct);

                return ToDto(row, sha256);
            }, cancellationToken);

            logger.LogInformation(
                "evidence.uploaded instanceId={InstanceId} evidenceId={EvidenceId} version={Version}", instanceId, evidenceId, dto.Version);
            return RemediationResult<EvidenceDto>.Ok(dto);
        }
        catch
        {
            await TryDeleteAsync(storageKey);
            throw;
        }
    }

    public async Task<RemediationResult<IReadOnlyList<EvidenceDto>>> ListAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var scopeFailure = await CheckScopeAsync<IReadOnlyList<EvidenceDto>>(instanceId, cancellationToken);
        if (scopeFailure is not null)
        {
            return scopeFailure;
        }

        var rows = await db.Evidences.AsNoTracking()
            .Where(e => e.InstanceId == instanceId)
            .OrderBy(e => e.FileName).ThenByDescending(e => e.Version)
            .Take(MaxListRows)
            .ToListAsync(cancellationToken);

        return RemediationResult<IReadOnlyList<EvidenceDto>>.Ok(rows.Select(r => ToDto(r, null)).ToList());
    }

    // ------------------------------------------------------------------ helpers

    private async Task<RemediationResult<T>?> CheckScopeAsync<T>(Guid instanceId, CancellationToken ct)
    {
        if (await db.VulnerabilityInstances.ApplyDataScope(user).AnyAsync(i => i.Id == instanceId, ct))
        {
            return null;
        }

        var exists = await db.VulnerabilityInstances.AsNoTracking().AnyAsync(i => i.Id == instanceId, ct);
        return RemediationResult<T>.Fail(
            exists ? RemediationOutcome.OutOfScope : RemediationOutcome.NotFound, RemediationErrorCodes.InstanceNotFound);
    }

    /// <summary>บันทึก audit การปฏิเสธไฟล์ (EX-21/22) — เฉพาะ metadata; ห้ามมีเนื้อหา/ชื่อไฟล์.</summary>
    private async Task RecordRejectedAsync(
        Guid actorId, Guid instanceId, string code, long size, string? extension, string? sha256, CancellationToken ct)
    {
        try
        {
            await audit.RecordAsync(
                new AuditEntry(
                    actorId, ActionReject, "VulnerabilityInstance", instanceId.ToString(),
                    null,
                    JsonSerializer.Serialize(new { code, size, extension, sha256 }, Json),
                    "Rejected", AuditSource),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("evidence.reject.audit_failed instanceId={InstanceId} reason={Reason}", instanceId, ex.GetType().Name);
        }
    }

    private async Task TryDeleteAsync(string storageKey)
    {
        try
        {
            await storage.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError("evidence.cleanup.failed reason={Reason}", ex.GetType().Name);
        }
    }

    private static EvidenceDto ToDto(Evidence e, string? sha256)
        => new(
            e.Id, e.InstanceId, e.TaskId, e.Version, e.FileName, e.ContentType, e.FileSize,
            e.ScanStatus.ToString(), sha256, e.UploadedBy, e.UploadedAt);
}
