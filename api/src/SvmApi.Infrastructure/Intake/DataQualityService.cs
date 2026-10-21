using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SvmApi.Application.Auth;
using SvmApi.Application.Intake;
using SvmApi.Application.Reference;
using SvmApi.Application.Verification;
using SvmApi.Application.Workflow;
using SvmApi.Domain.Assets;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Infrastructure.Auth.DataScope;
using SvmApi.Infrastructure.Persistence;
using SvmApi.Infrastructure.Risk;

namespace SvmApi.Infrastructure.Intake;

/// <summary>
/// <see cref="IDataQualityService"/> (C4 · US-010/011/012 · FR-09..12): validate → asset/reference → normalize → dedup.
/// <list type="bullet">
/// <item>ไม่ผ่าน validation/asset ไม่อยู่ใน scope/reference ผิดประเภท (EX-15/16) → <see cref="FindingOutcomes.Rejected"/> และไม่เพิ่มอะไรลง DbContext.</item>
/// <item>key ตรง → เชื่อม instance เดิม, ปรับ lastDetectedAt (FR-12) — ไม่สร้าง instance/remediation task ใหม่.</item>
/// <item>ambiguous → finding NeedsReview + instanceId null = คิว dedup (EX-45).</item>
/// <item>reference ไม่พบ/ไม่รองรับ (EX-47/48/49) → บันทึก + NeedsReview (มี instanceId).</item>
/// </list>
/// ต้องเรียกภายใน unit of work ของผู้เรียก: เมธอดนี้ SaveChanges ทุกครั้งที่บันทึก เพื่อให้ finding แถวถัดไปในไฟล์เดียวกัน
/// dedup กับ instance ที่เพิ่งสร้างได้ (อยู่ใน transaction เดียวกัน ยังไม่ commit).
/// <c>rawSeverity</c> เก็บค่า severity ที่ normalize แล้ว (Critical/High/Medium/Low/Informational) ตาม common model.
/// </summary>
public sealed class DataQualityService(
    SvmDbContext db,
    IReferenceDataService reference,
    ICurrentUser user,
    TimeProvider clock,
    RiskScoringService risk,
    IWorkflowService workflow,
    ILogger<DataQualityService> logger) : IDataQualityService
{
    /// <summary>จำนวน candidate สูงสุดที่พิจารณาต่อ finding (asset+cve เดียวกัน).</summary>
    public const int MaxCandidates = 100;

    public async Task<FindingProcessResult> ProcessAsync(RawFindingRow row, Guid? importBatchId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        // 1) mandatory / type / format + normalize (pure)
        var validation = FindingValidator.Validate(row, now);
        if (!validation.IsValid)
        {
            return Rejected(validation.Errors);
        }

        var f = validation.Value!;

        // 2) asset reference — ต้องมีอยู่และอยู่ใน data scope ของผู้นำเข้า (ตอบเหมือนกันทั้งสองกรณี: ไม่เปิดเผยการมีอยู่)
        var asset = await db.Assets.AsNoTracking()
            .ApplyDataScope(user)
            .Where(a => a.AssetCode == f.AssetCode)
            .Select(a => new { a.Id, a.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (asset is null)
        {
            var exists = await db.Assets.AsNoTracking().AnyAsync(a => a.AssetCode == f.AssetCode, cancellationToken);
            return Rejected([new FieldError(IntakeTemplate.AssetCode, IntakeErrorCodes.AssetNotFound)], outOfScope: exists);
        }

        if (asset.Status == AssetStatus.Decommissioned)
        {
            return Rejected([new FieldError(IntakeTemplate.AssetCode, IntakeErrorCodes.AssetDecommissioned)]);
        }

        // 3) reference data (CVE/CWE/CVSS/KEV) — 4.2 ไม่ throw; Intake ตัดสิน EX-16 vs NeedsReview
        var enrichment = await reference.EnrichAsync(
            new ReferenceEnrichmentRequest(f.CveId, f.CweId, f.CvssVersion, f.CvssVector, f.CvssBaseScore), cancellationToken);
        var decision = ReferencePolicy.Evaluate(enrichment);
        if (decision.Rejected)
        {
            return Rejected(decision.Rejections);
        }

        var cveId = Fit(enrichment.CveId ?? f.CveId, FindingLimits.CveId);
        var cweId = Fit(enrichment.CweId ?? f.CweId, FindingLimits.CweId);
        var cvssVersion = Fit(enrichment.CvssVersion, FindingLimits.CvssVersion) ?? Fit(f.CvssVersion, FindingLimits.CvssVersion);
        var cvssVector = Fit(enrichment.CvssVector ?? f.CvssVector, FindingLimits.CvssVector);

        // 4) dedup (BR-12)
        var key = new DedupKey(asset.Id, cveId, f.Location, f.Port, f.Component);
        var candidates = await LoadCandidatesAsync(asset.Id, cveId, cancellationToken);
        var match = DedupMatcher.Match(key, f.FindingName, candidates);

        var finding = new VulnerabilityFinding
        {
            Id = Guid.NewGuid(),
            AssetId = asset.Id,
            SourceSystem = f.SourceSystem,
            SourceRecordId = f.SourceRecordId,
            ImportBatchId = importBatchId,
            FindingName = f.FindingName,
            RawSeverity = f.Severity,
            CveId = cveId,
            CweId = cweId,
            CvssVersion = cvssVersion,
            CvssBaseScore = enrichment.CvssBaseScore,
            CvssVector = cvssVector,
            Location = f.Location,
            Port = f.Port,
            Component = f.Component,
            DetectedAt = f.DetectedAt,
            IsDuplicate = match.Kind == DedupMatchKind.Exact,
            ValidationStatus = ReferencePolicy.StatusFor(match.Kind == DedupMatchKind.Ambiguous, decision.NeedsReview),
            CreatedAt = now,
            CreatedBy = user.UserId,
        };

        string outcome;
        switch (match.Kind)
        {
            case DedupMatchKind.Exact:
            {
                var instance = await db.VulnerabilityInstances.FirstAsync(i => i.Id == match.InstanceId, cancellationToken);
                Touch(instance, f.DetectedAt, enrichment.IsKev, now);
                finding.InstanceId = instance.Id;
                outcome = FindingOutcomes.Duplicate;
                break;
            }

            case DedupMatchKind.Ambiguous:
                outcome = FindingOutcomes.Queued;
                break;

            default:
            {
                var instance = await AddNewInstanceAsync(finding, enrichment.IsKev, now, cancellationToken);
                finding.InstanceId = instance.Id;
                outcome = FindingOutcomes.Created;
                break;
            }
        }

        db.VulnerabilityFindings.Add(finding);
        await db.SaveChangesAsync(cancellationToken);

        // BR-13 / EX-46: พบซ้ำหลัง Resolved/Closed → reopen ผ่าน workflow (หลังบันทึก finding แล้ว · ไม่ throw)
        if (VerificationRules.ShouldReopenOnRecurrence(match))
        {
            await TryReopenRecurrenceAsync(match.InstanceId!.Value, finding.Id, cancellationToken);
        }

        // log เฉพาะ id + fingerprint (ไม่ log location/component/ชื่อ ซึ่งอาจมีข้อมูลภายใน)
        logger.LogInformation(
            "dataquality.processed findingId={FindingId} instanceId={InstanceId} outcome={Outcome} dedupKey={DedupKey} needsReview={NeedsReview}",
            finding.Id, finding.InstanceId, outcome, key.Fingerprint, finding.ValidationStatus == ValidationStatus.NeedsReview);

        return new FindingProcessResult(
            outcome,
            finding.Id,
            finding.InstanceId,
            finding.IsDuplicate,
            finding.ValidationStatus.ToString(),
            decision.ReviewIssues,
            [],
            IsRecurrence: match.IsRecurrence);
    }

    /// <summary>
    /// reopen instance ที่พบซ้ำผ่าน <see cref="IWorkflowService.ReopenAsync"/> (เขียน history + audit ใน transaction เดียวกับผู้เรียก ถ้ามี).
    /// <b>ไม่ throw</b>: ผลไม่สำเร็จ (เช่น ไม่มีแถว transition, instance เปลี่ยนสถานะไปแล้ว) หรือ exception = log warning แล้วไปต่อ —
    /// finding ถูกบันทึกและผูก instance แล้วก่อนหน้านี้ จึงไม่เสียข้อมูล. log เฉพาะ id/รหัส (ไม่มี PII).
    /// </summary>
    private async Task TryReopenRecurrenceAsync(Guid instanceId, Guid findingId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await workflow.ReopenAsync(instanceId, VerificationRules.RecurrenceReopenReason, cancellationToken);
            if (result.Outcome == WorkflowOutcome.Success)
            {
                logger.LogInformation(
                    "dataquality.recurrence.reopened instanceId={InstanceId} findingId={FindingId}", instanceId, findingId);
            }
            else
            {
                logger.LogWarning(
                    "dataquality.recurrence.reopen_skipped instanceId={InstanceId} findingId={FindingId} outcome={Outcome} code={Code}",
                    instanceId, findingId, result.Outcome, result.Code);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                "dataquality.recurrence.reopen_failed instanceId={InstanceId} findingId={FindingId} reason={Reason}",
                instanceId, findingId, ex.GetType().Name);
        }
    }

    /// <summary>instance ที่ยัง active บน asset + CVE เดียวกัน (null == null) — ผู้สมัคร dedup.</summary>
    internal async Task<IReadOnlyList<DedupCandidate>> LoadCandidatesAsync(Guid assetId, string? cveId, CancellationToken ct)
    {
        var rows = await db.VulnerabilityInstances.AsNoTracking()
            .Where(i => i.AssetId == assetId && i.Status != VulnerabilityStatus.Archived && i.CveId == cveId)
            .OrderByDescending(i => i.LastDetectedAt)
            .Take(MaxCandidates)
            .Select(i => new { i.Id, i.AssetId, i.CveId, i.Location, i.Port, i.Component, i.Title, i.Status })
            .ToListAsync(ct);

        return rows
            .Select(r => new DedupCandidate(r.Id, new DedupKey(r.AssetId, r.CveId, r.Location, r.Port, r.Component), r.Title, r.Status))
            .ToList();
    }

    /// <summary>ปรับ instance เดิมเมื่อพบซ้ำ (FR-12): lastDetectedAt = ล่าสุด, firstDetectedAt = เก่าสุด, KEV ติดแล้วไม่ถอด.</summary>
    internal void Touch(VulnerabilityInstance instance, DateTime detectedAt, bool isKev, DateTime now)
    {
        if (detectedAt > instance.LastDetectedAt)
        {
            instance.LastDetectedAt = detectedAt;
        }

        if (detectedAt < instance.FirstDetectedAt)
        {
            instance.FirstDetectedAt = detectedAt;
        }

        instance.IsKev |= isKev;
        instance.UpdatedAt = now;
        instance.UpdatedBy = user.UserId;
    }

    /// <summary>
    /// สร้าง + เพิ่ม instance ใหม่ลง DbContext พร้อมคะแนน risk เริ่มต้น (US-015). ไม่ throw เพราะ scoring:
    /// คำนวณไม่ได้ = instance ถูกบันทึกแบบยังไม่มี score และมี log warning (ดู <see cref="RiskScoringService.TryScoreNewInstanceAsync"/>).
    /// </summary>
    internal async Task<VulnerabilityInstance> AddNewInstanceAsync(
        VulnerabilityFinding finding, bool isKev, DateTime now, CancellationToken cancellationToken)
    {
        var instance = NewInstance(finding, isKev, now);
        db.VulnerabilityInstances.Add(instance);
        await risk.TryScoreNewInstanceAsync(instance, finding.CvssBaseScore, finding.RawSeverity, cancellationToken);
        return instance;
    }

    /// <summary>instance ใหม่จาก finding (สถานะเริ่มต้น New — US-018). risk score เติมโดย <see cref="AddNewInstanceAsync"/>.</summary>
    internal VulnerabilityInstance NewInstance(VulnerabilityFinding finding, bool isKev, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        AssetId = finding.AssetId!.Value,
        Title = finding.FindingName,
        CveId = finding.CveId,
        CweId = finding.CweId,
        Location = finding.Location,
        Port = finding.Port,
        Component = finding.Component,
        FirstDetectedAt = finding.DetectedAt,
        LastDetectedAt = finding.DetectedAt,
        Status = VulnerabilityStatus.New,
        IsKev = isKev,
        CreatedAt = now,
        CreatedBy = user.UserId,
    };

    private static FindingProcessResult Rejected(IReadOnlyList<FieldError> errors, bool outOfScope = false)
        => new(FindingOutcomes.Rejected, null, null, false, null, [], errors, outOfScope);

    private static string? Fit(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : null;
}
