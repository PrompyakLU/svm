using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SvmApi.Application.Audit;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Application.Intake;
using SvmApi.Application.Reference;
using SvmApi.Domain.Remediation;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Infrastructure.Auth.DataScope;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Intake;

/// <summary>
/// <see cref="IIntakeService"/> (C3 · US-007/008/009 · FR-05/07/08) + คิว dedup (C4 · EX-45).
/// <list type="bullet">
/// <item>import ประมวลผล inline ใน unit of work เดียว (batch + findings + instances + audit commit พร้อมกัน).</item>
/// <item>idempotent ด้วย transactionId (BR-36/EX-17): unique filtered index เป็นตัวกันชนสุดท้าย; ชนแล้ว replay ผลเดิม.</item>
/// <item>audit ไม่มี PII/เนื้อหาไฟล์: เก็บเฉพาะ id, จำนวน, รหัสผล (ไม่มีชื่อไฟล์/ค่าจากไฟล์).</item>
/// </list>
/// </summary>
public sealed class IntakeService(
    SvmDbContext db,
    IUnitOfWork unitOfWork,
    IAuditService audit,
    DataQualityService dataQuality,
    IReferenceDataService reference,
    ICurrentUser user,
    IOptions<IntakeOptions> options,
    TimeProvider clock,
    ILogger<IntakeService> logger) : IIntakeService
{
    public const string AuditSource = "IntakeService";
    public const string ActionImport = "finding.import";
    public const string ActionManual = "finding.create";
    public const string ActionResolve = "finding.dedup.resolve";

    private const int MaxPageSize = 100;
    private const int MaxCandidatesPerItem = 20;

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ------------------------------------------------------------------ import

    public async Task<IntakeResult<ImportResultDto>> ImportAsync(
        ImportFile file, string? transactionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        var userId = RequireUser();
        var opts = options.Value;

        var (txValid, txId) = ImportIdempotency.NormalizeTransactionId(transactionId);
        if (!txValid)
        {
            return IntakeResult<ImportResultDto>.Invalid(
                new Dictionary<string, string[]> { ["transactionId"] = [IntakeErrorCodes.InvalidTransactionId] },
                IntakeErrorCodes.InvalidTransactionId);
        }

        if (txId is not null)
        {
            var early = await TryReplayAsync(txId, userId, cancellationToken);
            if (early is not null)
            {
                return early;
            }
        }

        var (parsed, fileError) = IntakeFileReader.Read(
            file.FileName, file.Content.Span, opts.EffectiveMaxFileBytes, opts.EffectiveMaxRows);
        if (fileError is not null)
        {
            logger.LogWarning("intake.import.rejected code={Code} userId={UserId}", fileError.Code, userId);
            var errors = new Dictionary<string, string[]> { ["file"] = [$"{fileError.Code}: {fileError.Message}"] };
            foreach (var field in fileError.Fields)
            {
                errors[field] = [fileError.Code];
            }

            return IntakeResult<ImportResultDto>.Invalid(errors, fileError.Code);
        }

        var now = Now();
        var rows = parsed!.Rows;
        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(),
            FileName = IntakeFileReader.SanitizeFileName(file.FileName),
            TransactionId = txId,
            TotalRows = rows.Count,
            Status = ImportBatchStatus.Processing,
            ImportedBy = userId,
            ImportedAt = now,
        };

        ImportSummary summary;
        try
        {
            summary = await unitOfWork.ExecuteAsync(async ct =>
            {
                db.ImportBatches.Add(batch);
                await db.SaveChangesAsync(ct); // ชน unique(transactionId) ที่นี่ถ้ามีคำขอพร้อมกัน

                var s = new ImportSummary(opts.EffectiveMaxReportedErrors);
                foreach (var row in rows)
                {
                    var result = await dataQuality.ProcessAsync(row.Data, batch.Id, ct);
                    s.Add(row.Row, result);
                }

                batch.SuccessCount = s.Success;
                batch.FailedCount = s.Failed;
                batch.Status = ImportIdempotency.StatusFor(batch.TotalRows, s.Success);
                batch.ErrorDetail = JsonSerializer.Serialize(s.ToDetail(), Json);

                await audit.AppendAsync(
                    new AuditEntry(
                        userId, ActionImport, "ImportBatch", batch.Id.ToString(), null,
                        JsonSerializer.Serialize(
                            new
                            {
                                transactionId = txId,
                                totalRows = batch.TotalRows,
                                successCount = s.Success,
                                failedCount = s.Failed,
                                createdCount = s.Created,
                                duplicateCount = s.Duplicate,
                                queuedCount = s.Queued,
                                needsReviewCount = s.NeedsReview,
                                status = batch.Status.ToString(),
                            },
                            Json),
                        batch.Status.ToString(), AuditSource),
                    ct);
                return s;
            }, cancellationToken);
        }
        catch (DbUpdateException) when (txId is not null)
        {
            // คำขอพร้อมกันด้วย transactionId เดียวกัน: อีกคำขอ commit ก่อน -> replay/409 ตามเจ้าของ
            db.ChangeTracker.Clear();
            var replay = await TryReplayAsync(txId, userId, cancellationToken);
            if (replay is not null)
            {
                return replay;
            }

            throw;
        }

        logger.LogInformation(
            "intake.import.completed batchId={BatchId} status={Status} total={Total} success={Success} failed={Failed} queued={Queued}",
            batch.Id, batch.Status, batch.TotalRows, summary.Success, summary.Failed, summary.Queued);

        return IntakeResult<ImportResultDto>.Ok(ToImportResult(batch, summary.ToDetail(), replayed: false));
    }

    private async Task<IntakeResult<ImportResultDto>?> TryReplayAsync(string txId, Guid userId, CancellationToken ct)
    {
        var existing = await db.ImportBatches.AsNoTracking()
            .Where(b => b.TransactionId == txId)
            .FirstOrDefaultAsync(ct);

        var decision = ImportIdempotency.Decide(
            existing is null ? null : new ExistingBatch(existing.Id, existing.ImportedBy, existing.Status), userId);

        return decision switch
        {
            IdempotencyDecision.Proceed => null,
            IdempotencyDecision.Replay => IntakeResult<ImportResultDto>.Ok(
                ToImportResult(existing!, ParseDetail(existing!.ErrorDetail), replayed: true)),
            IdempotencyDecision.InProgress => IntakeResult<ImportResultDto>.Fail(
                IntakeOutcome.Conflict, IntakeErrorCodes.ImportInProgress, "An import with this transactionId is still processing."),
            _ => IntakeResult<ImportResultDto>.Fail(
                IntakeOutcome.Conflict, IntakeErrorCodes.TransactionIdConflict, "This transactionId has already been used."),
        };
    }

    // ------------------------------------------------------------------ manual

    public async Task<IntakeResult<ManualFindingResultDto>> RegisterManualAsync(
        ManualFindingRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var row = request?.ToRawRow();

        FindingProcessResult? result = null;
        await unitOfWork.ExecuteAsync(async ct =>
        {
            result = await dataQuality.ProcessAsync(row!, null, ct);
            if (!result.IsValid)
            {
                return; // ไม่มีอะไรถูกเพิ่ม — transaction commit ว่าง
            }

            await audit.AppendAsync(
                new AuditEntry(
                    userId, ActionManual, "VulnerabilityFinding", result.FindingId!.Value.ToString(), null,
                    JsonSerializer.Serialize(
                        new
                        {
                            instanceId = result.InstanceId,
                            outcome = result.Outcome,
                            isDuplicate = result.IsDuplicate,
                            validationStatus = result.ValidationStatus,
                            reviewIssues = result.ReviewIssues,
                        },
                        Json),
                    "Success", AuditSource),
                ct);
        }, cancellationToken);

        var r = result!;
        if (!r.IsValid)
        {
            var errors = r.Errors
                .GroupBy(e => e.Field, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Code).Distinct().ToArray());

            return r.AssetOutOfScope
                ? new IntakeResult<ManualFindingResultDto>(IntakeOutcome.OutOfScope, default, errors, IntakeErrorCodes.ValidationError)
                : IntakeResult<ManualFindingResultDto>.Invalid(errors);
        }

        return IntakeResult<ManualFindingResultDto>.Ok(new ManualFindingResultDto(
            r.FindingId!.Value, r.InstanceId, r.Outcome, r.IsDuplicate, r.ValidationStatus!, r.ReviewIssues));
    }

    // ------------------------------------------------------------------ batches

    public async Task<IntakeResult<PagedResult<ImportBatchDto>>> ListBatchesAsync(
        ImportBatchQuery query, CancellationToken cancellationToken)
    {
        var errors = ValidatePaging(query.Page, query.PageSize);
        ImportBatchStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (Enum.TryParse<ImportBatchStatus>(query.Status.Trim(), ignoreCase: true, out var s) && Enum.IsDefined(s))
            {
                status = s;
            }
            else
            {
                errors["status"] = [IntakeErrorCodes.InvalidFormat];
            }
        }

        if (errors.Count > 0)
        {
            return IntakeResult<PagedResult<ImportBatchDto>>.Invalid(errors);
        }

        var q = db.ImportBatches.AsNoTracking().ApplyDataScope(user);
        if (status is { } st)
        {
            q = q.Where(b => b.Status == st);
        }

        var total = await q.LongCountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(b => b.ImportedAt)
            .ThenBy(b => b.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return IntakeResult<PagedResult<ImportBatchDto>>.Ok(
            new PagedResult<ImportBatchDto>(items.Select(ToBatchDto).ToList(), query.Page, query.PageSize, total));
    }

    public async Task<IntakeResult<ImportBatchDetailDto>> GetBatchAsync(Guid id, CancellationToken cancellationToken)
    {
        var batch = await db.ImportBatches.AsNoTracking().ApplyDataScope(user).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (batch is null)
        {
            var exists = await db.ImportBatches.AsNoTracking().AnyAsync(b => b.Id == id, cancellationToken);
            return IntakeResult<ImportBatchDetailDto>.Fail(
                exists ? IntakeOutcome.OutOfScope : IntakeOutcome.NotFound, IntakeErrorCodes.BatchNotFound);
        }

        return IntakeResult<ImportBatchDetailDto>.Ok(new ImportBatchDetailDto(ToBatchDto(batch), ParseDetail(batch.ErrorDetail).Errors));
    }

    // ------------------------------------------------------------------ dedup queue

    public async Task<IntakeResult<PagedResult<DedupQueueItemDto>>> GetDedupQueueAsync(
        DedupQueueQuery query, CancellationToken cancellationToken)
    {
        var errors = ValidatePaging(query.Page, query.PageSize);
        if (errors.Count > 0)
        {
            return IntakeResult<PagedResult<DedupQueueItemDto>>.Invalid(errors);
        }

        var q = QueueQuery(db.VulnerabilityFindings.AsNoTracking().ApplyDataScope(user));
        var total = await q.LongCountAsync(cancellationToken);
        var rows = await q
            .OrderBy(f => f.CreatedAt)
            .ThenBy(f => f.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(f => new { Finding = f, AssetCode = f.Asset != null ? f.Asset.AssetCode : null })
            .ToListAsync(cancellationToken);

        var items = new List<DedupQueueItemDto>(rows.Count);
        foreach (var r in rows)
        {
            var f = r.Finding;
            IReadOnlyList<DedupCandidateDto> candidates = [];
            if (f.AssetId is { } assetId)
            {
                var key = new DedupKey(assetId, f.CveId, f.Location, f.Port, f.Component);
                var all = await dataQuality.LoadCandidatesAsync(assetId, f.CveId, cancellationToken);
                var ids = DedupMatcher.Match(key, f.FindingName, all).CandidateIds.Take(MaxCandidatesPerItem).ToList();
                candidates = await db.VulnerabilityInstances.AsNoTracking()
                    .ApplyDataScope(user)
                    .Where(i => ids.Contains(i.Id))
                    .OrderByDescending(i => i.LastDetectedAt)
                    .Select(i => new DedupCandidateDto(
                        i.Id, i.Title, i.CveId, i.Location, i.Port, i.Component, i.Status.ToString(), i.LastDetectedAt))
                    .ToListAsync(cancellationToken);
            }

            items.Add(new DedupQueueItemDto(
                f.Id, f.AssetId, r.AssetCode, f.FindingName, f.SourceSystem, f.SourceRecordId, f.RawSeverity, f.CveId,
                f.Location, f.Port, f.Component, Utc(f.DetectedAt), f.ImportBatchId,
                candidates.Select(c => c with { LastDetectedAt = Utc(c.LastDetectedAt) }).ToList()));
        }

        return IntakeResult<PagedResult<DedupQueueItemDto>>.Ok(
            new PagedResult<DedupQueueItemDto>(items, query.Page, query.PageSize, total));
    }

    public async Task<IntakeResult<ResolveDuplicateResultDto>> ResolveDuplicateAsync(
        Guid findingId, ResolveDuplicateRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUser();

        // --- validate request
        var errors = new Dictionary<string, string[]>();
        var decision = request?.Decision?.Trim();
        if (string.Equals(decision, DedupDecisions.Duplicate, StringComparison.OrdinalIgnoreCase))
        {
            decision = DedupDecisions.Duplicate;
            if (request!.InstanceId is null || request.InstanceId == Guid.Empty)
            {
                errors["instanceId"] = [IntakeErrorCodes.MissingMandatory];
            }
        }
        else if (string.Equals(decision, DedupDecisions.Distinct, StringComparison.OrdinalIgnoreCase))
        {
            decision = DedupDecisions.Distinct;
        }
        else
        {
            errors["decision"] = [string.IsNullOrEmpty(decision) ? IntakeErrorCodes.MissingMandatory : IntakeErrorCodes.InvalidFormat];
        }

        var reason = request?.Reason?.Trim();
        if (reason is { Length: > FindingLimits.ResolveReason })
        {
            errors["reason"] = [IntakeErrorCodes.TooLong];
        }
        else if (reason is not null && reason.Any(c => char.IsControl(c) && c is not '\n' and not '\r'))
        {
            errors["reason"] = [IntakeErrorCodes.InvalidFormat];
        }

        if (errors.Count > 0)
        {
            return IntakeResult<ResolveDuplicateResultDto>.Invalid(errors);
        }

        // --- load (scoped) finding
        var finding = await db.VulnerabilityFindings.ApplyDataScope(user).FirstOrDefaultAsync(f => f.Id == findingId, cancellationToken);
        if (finding is null)
        {
            var exists = await db.VulnerabilityFindings.AsNoTracking().AnyAsync(f => f.Id == findingId, cancellationToken);
            return IntakeResult<ResolveDuplicateResultDto>.Fail(
                exists ? IntakeOutcome.OutOfScope : IntakeOutcome.NotFound, IntakeErrorCodes.FindingNotFound);
        }

        if (finding.ValidationStatus != ValidationStatus.NeedsReview || finding.InstanceId is not null || finding.AssetId is null)
        {
            return IntakeResult<ResolveDuplicateResultDto>.Fail(
                IntakeOutcome.Conflict, IntakeErrorCodes.NotInQueue, "The finding is not waiting in the dedup queue.");
        }

        var assetId = finding.AssetId.Value;
        var now = Now();

        // reference ยังต้องตรวจหรือไม่ (EX-47/48/49) — ตัดสิน validationStatus หลังออกจากคิว
        var enrichment = await reference.EnrichAsync(
            new ReferenceEnrichmentRequest(finding.CveId, finding.CweId, finding.CvssVersion, finding.CvssVector, finding.CvssBaseScore),
            cancellationToken);
        var refDecision = ReferencePolicy.Evaluate(enrichment);

        VulnerabilityInstance instance;
        if (decision == DedupDecisions.Duplicate)
        {
            var target = await db.VulnerabilityInstances
                .ApplyDataScope(user)
                .FirstOrDefaultAsync(
                    i => i.Id == request!.InstanceId && i.AssetId == assetId && i.Status != VulnerabilityStatus.Archived,
                    cancellationToken);
            if (target is null)
            {
                return IntakeResult<ResolveDuplicateResultDto>.Invalid(
                    new Dictionary<string, string[]> { ["instanceId"] = [IntakeErrorCodes.InstanceNotCandidate] },
                    IntakeErrorCodes.InstanceNotCandidate);
            }

            dataQuality.Touch(target, finding.DetectedAt, enrichment.IsKev, now);
            finding.IsDuplicate = true;
            instance = target;
        }
        else
        {
            // Distinct: dedup key ต้องยังว่าง (unique filtered index) — ไม่เช่นนั้นต้องเลือก Duplicate
            var key = new DedupKey(assetId, finding.CveId, finding.Location, finding.Port, finding.Component);
            var candidates = await dataQuality.LoadCandidatesAsync(assetId, finding.CveId, cancellationToken);
            if (candidates.Any(c => c.Key.Equals(key)))
            {
                return IntakeResult<ResolveDuplicateResultDto>.Fail(
                    IntakeOutcome.Conflict, IntakeErrorCodes.DedupKeyExists,
                    "An active instance with the same dedup key already exists; resolve as Duplicate instead.");
            }

            instance = await dataQuality.AddNewInstanceAsync(finding, enrichment.IsKev, now, cancellationToken);
            finding.IsDuplicate = false;
        }

        finding.InstanceId = instance.Id;
        finding.ValidationStatus = ReferencePolicy.StatusFor(ambiguous: false, refDecision.NeedsReview);
        finding.UpdatedAt = now;
        finding.UpdatedBy = userId;

        // เหตุผลเก็บเป็น comment บน instance (ไม่ใส่ข้อความอิสระลง audit — กัน PII)
        Comment? comment = null;
        if (!string.IsNullOrEmpty(reason))
        {
            comment = new Comment
            {
                Id = Guid.NewGuid(),
                InstanceId = instance.Id,
                Body = $"[Dedup {decision}] {reason}",
                CreatedBy = userId,
                CreatedAt = now,
            };
            db.Comments.Add(comment);
        }

        try
        {
            await unitOfWork.ExecuteAsync(async ct =>
            {
                await audit.AppendAsync(
                    new AuditEntry(
                        userId, ActionResolve, "VulnerabilityFinding", finding.Id.ToString(),
                        JsonSerializer.Serialize(new { validationStatus = nameof(ValidationStatus.NeedsReview), instanceId = (Guid?)null }, Json),
                        JsonSerializer.Serialize(
                            new
                            {
                                decision,
                                instanceId = instance.Id,
                                validationStatus = finding.ValidationStatus.ToString(),
                                commentId = comment?.Id,
                            },
                            Json),
                        "Success", AuditSource),
                    ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return IntakeResult<ResolveDuplicateResultDto>.Fail(
                IntakeOutcome.Conflict, IntakeErrorCodes.ConcurrencyConflict, "The record was changed by another user. Reload and retry.");
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return IntakeResult<ResolveDuplicateResultDto>.Fail(
                IntakeOutcome.Conflict, IntakeErrorCodes.DedupKeyExists, "The dedup key conflicts with an existing instance.");
        }

        return IntakeResult<ResolveDuplicateResultDto>.Ok(
            new ResolveDuplicateResultDto(finding.Id, instance.Id, decision!, finding.ValidationStatus.ToString()));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>คิว dedup = NeedsReview ที่ยังไม่ผูก instance (EX-45).</summary>
    public static IQueryable<VulnerabilityFinding> QueueQuery(IQueryable<VulnerabilityFinding> source)
        => source.Where(f => f.ValidationStatus == ValidationStatus.NeedsReview && f.InstanceId == null);

    private static Dictionary<string, string[]> ValidatePaging(int page, int pageSize)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1 || page > 100_000)
        {
            errors["page"] = [IntakeErrorCodes.InvalidFormat];
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            errors["pageSize"] = [IntakeErrorCodes.InvalidFormat];
        }

        return errors;
    }

    private static ImportResultDto ToImportResult(ImportBatch b, ImportDetail d, bool replayed)
        => new(
            b.Id, b.Status.ToString(), b.TotalRows, b.SuccessCount, b.FailedCount,
            d.CreatedCount, d.DuplicateCount, d.QueuedCount, d.NeedsReviewCount, replayed, d.Errors);

    private static ImportBatchDto ToBatchDto(ImportBatch b)
        => new(b.Id, b.FileName, b.TransactionId, b.TotalRows, b.SuccessCount, b.FailedCount, b.Status.ToString(), b.ImportedBy, Utc(b.ImportedAt));

    private static ImportDetail ParseDetail(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ImportDetail.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<ImportDetail>(json, Json) ?? ImportDetail.Empty;
        }
        catch (JsonException)
        {
            return ImportDetail.Empty;
        }
    }

    private Guid RequireUser()
        => user.UserId ?? throw new InvalidOperationException("Intake requires an authenticated user.");

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>สรุปผลที่เก็บใน <c>config_importBatch.errorDetail</c> (JSON) — เฉพาะเลขแถว + รหัส/ชื่อ field.</summary>
    public sealed record ImportDetail(
        int CreatedCount,
        int DuplicateCount,
        int QueuedCount,
        int NeedsReviewCount,
        bool ErrorsTruncated,
        IReadOnlyList<ImportRowError> Errors)
    {
        public static ImportDetail Empty { get; } = new(0, 0, 0, 0, false, []);
    }

    private sealed class ImportSummary(int maxErrors)
    {
        private readonly List<ImportRowError> errors = [];
        private bool truncated;

        public int Success { get; private set; }
        public int Failed { get; private set; }
        public int Created { get; private set; }
        public int Duplicate { get; private set; }
        public int Queued { get; private set; }
        public int NeedsReview { get; private set; }

        public void Add(int row, FindingProcessResult r)
        {
            if (!r.IsValid)
            {
                Failed++;
                foreach (var e in r.Errors)
                {
                    if (errors.Count >= maxErrors)
                    {
                        truncated = true;
                        break;
                    }

                    errors.Add(new ImportRowError(row, e.Reason));
                }

                return;
            }

            Success++;
            switch (r.Outcome)
            {
                case FindingOutcomes.Created: Created++; break;
                case FindingOutcomes.Duplicate: Duplicate++; break;
                case FindingOutcomes.Queued: Queued++; break;
            }

            if (r.ValidationStatus == nameof(ValidationStatus.NeedsReview))
            {
                NeedsReview++;
            }
        }

        public ImportDetail ToDetail() => new(Created, Duplicate, Queued, NeedsReview, truncated, errors.ToList());
    }
}
