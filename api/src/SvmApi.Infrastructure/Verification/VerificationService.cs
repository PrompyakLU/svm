using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SvmApi.Application.Audit;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Application.Intake;
using SvmApi.Application.Notifications;
using SvmApi.Application.Verification;
using SvmApi.Application.Workflow;
using SvmApi.Domain.Verification;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Infrastructure.Auth.DataScope;
using SvmApi.Infrastructure.Persistence;
using SvmApi.Infrastructure.Workflow;

namespace SvmApi.Infrastructure.Verification;

/// <summary>
/// <see cref="IVerificationService"/> (C10 · US-027/028/029 · FR-43..47 · BR-13/14/15/16 · EX-74..80).
/// <list type="bullet">
/// <item>การเปลี่ยนสถานะทั้งหมดผ่าน <see cref="IWorkflowService"/> (ไม่ set Status ตรง): submit/verify/reopen ใช้ <c>TransitionAsync</c>,
/// พบซ้ำหลัง Resolved/Closed ใช้ <c>ReopenAsync</c> (BR-13).</item>
/// <item>บันทึก <c>trans_verificationRecord</c> + audit + การเปลี่ยนสถานะอยู่ใน transaction เดียว (workflow join transaction เดียวกัน);
/// ถ้า workflow ปฏิเสธ (สิทธิ์/หลักฐาน/rowVersion/BR-16) transaction ถูก rollback ทั้งชุด — ไม่มี record ค้าง.</item>
/// <item>independent verifier (BR-16): ตัดสินด้วย <see cref="VerificationRules"/> (pure) จาก assignee + เจ้าของ task + ผู้ส่งเข้า Pending Verification.</item>
/// <item>audit ไม่เก็บ notes (ข้อความอิสระ อาจมี PII) — เก็บ id/ผล/วิธี/สถานะเท่านั้น; notes อยู่ใน verification record.</item>
/// <item>การแจ้งเตือนทำหลัง commit แบบ best-effort (ล้มเหลวไม่ทำให้ผลหลักล้ม).</item>
/// </list>
/// </summary>
public sealed class VerificationService(
    SvmDbContext db,
    IUnitOfWork unitOfWork,
    IAuditService audit,
    IWorkflowService workflow,
    IScannerAdapter scanner,
    INotificationService notifications,
    ICurrentUser user,
    TimeProvider clock,
    ILogger<VerificationService> logger) : IVerificationService
{
    public const string AuditSource = "VerificationService";
    public const string ActionSubmit = "verification.submit";
    public const string ActionRescan = "verification.rescan";
    public const string ActionRecord = "verification.record";

    /// <summary>notes ตอน submit ถูกส่งเป็น reason ของ history จึงต้องไม่เกินขีดของ workflow.</summary>
    private const int MaxSubmitNotesLength = WorkflowService.MaxReasonLength;

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed record InstanceInfo(
        Guid Id,
        Guid AssetId,
        string AssetCode,
        string Title,
        string? CveId,
        string? Location,
        int? Port,
        string? Component,
        VulnerabilityStatus Status,
        RiskLevel? RiskLevel,
        Guid? AssignedToUserId,
        byte[] RowVersion);

    /// <summary>ใช้ยกเลิก transaction เมื่อ workflow ปฏิเสธ — ผลถูกส่งกลับผู้เรียกโดยไม่ rollback แบบเงียบ.</summary>
    private sealed class AbortException(WorkflowOutcome outcome, string? code, string? message, IReadOnlyDictionary<string, string[]>? errors)
        : Exception("Verification transaction aborted.")
    {
        public WorkflowOutcome Outcome { get; } = outcome;
        public string? Code { get; } = code;
        public string? DenyMessage { get; } = message;
        public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;

        public WorkflowResult<T> ToResult<T>()
            => new(Outcome, default, Errors, Code, DenyMessage);
    }

    // ------------------------------------------------------------------ submit (US-027)

    public async Task<WorkflowResult<TransitionResultDto>> SubmitAsync(
        Guid instanceId, SubmitVerificationRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUser();

        var errors = new Dictionary<string, string[]>();
        var notes = TextSanitizer.Clean(request?.Notes, allowLineBreaks: true);
        if (notes.Length > MaxSubmitNotesLength)
        {
            errors["notes"] = [VerificationErrorCodes.NotesTooLong];
        }

        if (string.IsNullOrWhiteSpace(request?.RowVersion))
        {
            errors["rowVersion"] = [VerificationErrorCodes.RowVersionRequired];
        }

        if (errors.Count > 0)
        {
            return WorkflowResult<TransitionResultDto>.Invalid(errors);
        }

        var (info, failure) = await LoadAsync(instanceId, cancellationToken);
        if (info is null)
        {
            return WorkflowResult<TransitionResultDto>.Fail(failure!.Value, WorkflowErrorCodes.InstanceNotFound);
        }

        // guard (permission verification.submit / evidence Clean — BR-14, EX-56 / rowVersion — EX-57) อยู่ใน workflow
        var result = await workflow.TransitionAsync(
            instanceId,
            new TransitionRequest(
                VulnerabilityStatus.PendingVerification.ToString(),
                notes.Length == 0 ? "Submitted for verification." : notes,
                request!.RowVersion),
            cancellationToken);

        logger.LogInformation(
            "verification.submit instanceId={InstanceId} userId={UserId} outcome={Outcome} code={Code}",
            instanceId, userId, result.Outcome, result.Code);

        return result;
    }

    // ------------------------------------------------------------------ rescan (US-027 / US-029)

    public async Task<WorkflowResult<RescanResultDto>> RescanAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var userId = RequireUser();

        var (info, failure) = await LoadAsync(instanceId, cancellationToken);
        if (info is null)
        {
            return WorkflowResult<RescanResultDto>.Fail(failure!.Value, WorkflowErrorCodes.InstanceNotFound);
        }

        if (!VerificationRules.CanRequestRescan(info.Status))
        {
            return WorkflowResult<RescanResultDto>.Fail(
                WorkflowOutcome.Unprocessable, VerificationErrorCodes.RescanNotAllowed,
                $"A rescan can only be requested while the instance is '{VulnerabilityStatus.PendingVerification}', '{VulnerabilityStatus.Resolved}' or '{VulnerabilityStatus.Closed}'.");
        }

        RescanResponse response;
        try
        {
            response = await scanner.RequestRescanAsync(
                new RescanRequest(info.AssetId, info.AssetCode, info.Id, info.CveId, info.Location, info.Port, info.Component),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                "verification.rescan.scanner_error instanceId={InstanceId} reason={Reason}", instanceId, ex.GetType().Name);
            response = new RescanResponse(string.Empty, RescanStatus.Failed);
        }

        switch (response.Status)
        {
            case RescanStatus.Failed:
            case RescanStatus.TargetNotFound:
                return await RescanFailedAsync(info, response, userId, cancellationToken);

            case RescanStatus.StillDetected when VerificationRules.ShouldReopenOnRescan(response.Status, info.Status):
                return await RescanReopenAsync(info, response, userId, cancellationToken);

            default:
                // Resolved: ไม่พบช่องโหว่แล้ว — ไม่เปลี่ยนสถานะ (ผู้ตรวจต้องบันทึกผลเอง) · เก็บ audit ว่ามีการ rescan
                await AuditRescanAsync(info, response, userId, "Success", reopened: false, cancellationToken);
                logger.LogInformation(
                    "verification.rescan instanceId={InstanceId} status={Status} reopened=false", instanceId, response.Status);
                return WorkflowResult<RescanResultDto>.Ok(new RescanResultDto(
                    response.RequestId, response.Status.ToString(), false, null,
                    info.Status.ToString(), info.Status.ToString(), Convert.ToBase64String(info.RowVersion)));
        }
    }

    private async Task<WorkflowResult<RescanResultDto>> RescanFailedAsync(
        InstanceInfo info, RescanResponse response, Guid userId, CancellationToken ct)
    {
        var notFound = response.Status == RescanStatus.TargetNotFound;
        await AuditRescanAsync(info, response, userId, "Failed", reopened: false, ct);

        logger.LogWarning(
            "verification.rescan.failed instanceId={InstanceId} status={Status}", info.Id, response.Status);

        // EX-74/75: บันทึกและแจ้งผู้ขอ
        await TryNotifyAsync(
            [userId],
            notFound ? "RESCAN_TARGET_NOT_FOUND" : "RESCAN_FAILED",
            notFound ? "ไม่พบเป้าหมายสำหรับ rescan" : "ส่งคำขอ rescan ไม่สำเร็จ",
            $"คำขอ rescan ช่องโหว่ \"{TextSanitizer.Truncate(info.Title, 200)}\" "
            + (notFound ? "ไม่พบเป้าหมายใน scanner — โปรดตรวจสอบข้อมูล asset" : "ไม่สำเร็จ — โปรดลองใหม่ภายหลัง หรือขอ rescan ด้วยตนเอง"),
            info.Id,
            ct);

        return notFound
            ? WorkflowResult<RescanResultDto>.Fail(
                WorkflowOutcome.Unprocessable, VerificationErrorCodes.RescanTargetNotFound, "The scanner could not find the rescan target.")
            : WorkflowResult<RescanResultDto>.Fail(
                WorkflowOutcome.ExternalServiceFailed, VerificationErrorCodes.RescanFailed, "The rescan request could not be completed.");
    }

    /// <summary>rescan ยังพบช่องโหว่: บันทึก NotResolved(Rescan) + reopen ผ่าน workflow ใน transaction เดียว (BR-13).</summary>
    private async Task<WorkflowResult<RescanResultDto>> RescanReopenAsync(
        InstanceInfo info, RescanResponse response, Guid userId, CancellationToken ct)
    {
        var responsible = await LoadResponsibleAsync(info, ct);
        var record = NewRecord(
            info.Id, VerificationRules.ResultFromRescan(response.Status)!.Value, VerificationMethod.Rescan,
            VerificationRules.IsIndependent(userId, responsible), userId,
            $"Rescan still detects the vulnerability (requestId={response.RequestId}).");
        var reason = VerificationRules.RescanReopenReason(response.RequestId);

        TransitionResultDto transition;
        try
        {
            transition = await unitOfWork.ExecuteAsync<TransitionResultDto>(async c =>
            {
                db.VerificationRecords.Add(record);
                await audit.AppendAsync(
                    RecordAudit(userId, ActionRescan, record, info.Status, VulnerabilityStatus.Reopened, response.RequestId), c);

                var r = info.Status == VulnerabilityStatus.PendingVerification
                    ? await workflow.TransitionAsync(
                        info.Id,
                        new TransitionRequest(
                            VulnerabilityStatus.Reopened.ToString(), reason, Convert.ToBase64String(info.RowVersion)),
                        c)
                    : await workflow.ReopenAsync(info.Id, reason, c); // Resolved/Closed: system action (BR-13)

                if (r.Outcome != WorkflowOutcome.Success)
                {
                    throw new AbortException(r.Outcome, r.Code, r.Message, r.Errors);
                }

                return r.Value!;
            }, ct);
        }
        catch (AbortException abort)
        {
            db.ChangeTracker.Clear();
            logger.LogWarning(
                "verification.rescan.reopen_denied instanceId={InstanceId} outcome={Outcome} code={Code}",
                info.Id, abort.Outcome, abort.Code);
            return abort.ToResult<RescanResultDto>();
        }

        logger.LogInformation(
            "verification.rescan instanceId={InstanceId} status={Status} reopened=true", info.Id, response.Status);

        await NotifyResponsibleAsync(
            responsible, userId, "VERIFICATION_REOPENED", "ช่องโหว่ถูกเปิดกลับ",
            $"Rescan ยังพบช่องโหว่ \"{TextSanitizer.Truncate(info.Title, 200)}\" — เปิดรายการกลับเพื่อแก้ไขต่อ",
            info.Id, ct);

        return WorkflowResult<RescanResultDto>.Ok(new RescanResultDto(
            response.RequestId, response.Status.ToString(), true, record.Id,
            transition.FromStatus, transition.ToStatus, transition.RowVersion));
    }

    // ------------------------------------------------------------------ record result (US-028 / US-029)

    public async Task<WorkflowResult<VerificationRecordedDto>> RecordAsync(
        Guid instanceId, RecordVerificationRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUser();

        var (parsed, errors) = Validate(request);
        if (parsed is null)
        {
            return WorkflowResult<VerificationRecordedDto>.Invalid(errors);
        }

        var (result, method, notes) = parsed.Value;

        var (info, failure) = await LoadAsync(instanceId, cancellationToken);
        if (info is null)
        {
            return WorkflowResult<VerificationRecordedDto>.Fail(failure!.Value, WorkflowErrorCodes.InstanceNotFound);
        }

        var responsible = await LoadResponsibleAsync(info, cancellationToken);
        var isIndependent = VerificationRules.IsIndependent(userId, responsible);

        var decision = VerificationRules.Decide(result, info.Status, info.RiskLevel, isIndependent);
        if (!decision.Allowed)
        {
            logger.LogWarning(
                "verification.denied instanceId={InstanceId} userId={UserId} result={Result} code={Code}",
                instanceId, userId, result, decision.Code);

            return WorkflowResult<VerificationRecordedDto>.Fail(
                decision.Code == VerificationErrorCodes.IndependentVerificationRequired
                    ? WorkflowOutcome.IndependentVerificationRequired
                    : WorkflowOutcome.Unprocessable,
                decision.Code!, decision.Message);
        }

        var next = decision.NextStatus!.Value;
        var record = NewRecord(instanceId, result, method, isIndependent, userId, notes);
        var reason = next == VulnerabilityStatus.Reopened
            ? VerificationRules.VerificationReopenReason(result)
            : $"Verified ({method}).";

        TransitionResultDto transition;
        try
        {
            transition = await unitOfWork.ExecuteAsync<TransitionResultDto>(async c =>
            {
                db.VerificationRecords.Add(record);
                await audit.AppendAsync(RecordAudit(userId, ActionRecord, record, info.Status, next, null), c);

                // guard ของ workflow: สิทธิ์ verification.record, evidence Clean, rowVersion (EX-57), BR-16 (gate ใน workflow)
                var r = await workflow.TransitionAsync(
                    instanceId, new TransitionRequest(next.ToString(), reason, request.RowVersion), c);
                if (r.Outcome != WorkflowOutcome.Success)
                {
                    throw new AbortException(r.Outcome, r.Code, r.Message, r.Errors);
                }

                return r.Value!;
            }, cancellationToken);
        }
        catch (AbortException abort)
        {
            db.ChangeTracker.Clear();
            logger.LogWarning(
                "verification.record.rejected instanceId={InstanceId} outcome={Outcome} code={Code}",
                instanceId, abort.Outcome, abort.Code);
            return abort.ToResult<VerificationRecordedDto>();
        }

        logger.LogInformation(
            "verification.recorded instanceId={InstanceId} verificationId={VerificationId} result={Result} independent={Independent} to={To}",
            instanceId, record.Id, result, isIndependent, next);

        // EX-76..78: ไม่ผ่าน = คงสถานะไม่ปิด (Reopened) และแจ้งผู้รับผิดชอบ
        if (next == VulnerabilityStatus.Reopened)
        {
            await NotifyResponsibleAsync(
                responsible, userId, "VERIFICATION_REOPENED", "ผลตรวจสอบไม่ผ่าน — เปิดรายการกลับ",
                $"ช่องโหว่ \"{TextSanitizer.Truncate(info.Title, 200)}\" ไม่ผ่านการตรวจสอบ ({result}) — โปรดแก้ไขต่อ",
                instanceId, cancellationToken);
        }

        return WorkflowResult<VerificationRecordedDto>.Ok(new VerificationRecordedDto(
            record.Id, instanceId, result.ToString(), method.ToString(), isIndependent,
            transition.FromStatus, transition.ToStatus, transition.RowVersion));
    }

    // ------------------------------------------------------------------ helpers

    private static (( VerificationResult Result, VerificationMethod Method, string? Notes)? Value, IReadOnlyDictionary<string, string[]> Errors) Validate(
        RecordVerificationRequest? request)
    {
        var errors = new Dictionary<string, string[]>();

        VerificationResult? result = null;
        var rawResult = request?.Result?.Trim();
        if (string.IsNullOrEmpty(rawResult))
        {
            errors["result"] = [VerificationErrorCodes.ResultRequired];
        }
        else if (!int.TryParse(rawResult, out _)
                 && Enum.TryParse<VerificationResult>(rawResult, ignoreCase: true, out var parsedResult)
                 && Enum.IsDefined(parsedResult))
        {
            result = parsedResult;
        }
        else
        {
            errors["result"] = [VerificationErrorCodes.InvalidResult];
        }

        // method ไม่ส่ง = Manual (ไม่อ้างว่ามี rescan)
        var method = VerificationMethod.Manual;
        var rawMethod = request?.Method?.Trim();
        if (!string.IsNullOrEmpty(rawMethod))
        {
            if (!int.TryParse(rawMethod, out _)
                && Enum.TryParse<VerificationMethod>(rawMethod, ignoreCase: true, out var parsedMethod)
                && Enum.IsDefined(parsedMethod))
            {
                method = parsedMethod;
            }
            else
            {
                errors["method"] = [VerificationErrorCodes.InvalidMethod];
            }
        }

        var notes = TextSanitizer.Clean(request?.Notes, allowLineBreaks: true);
        if (notes.Length > VerificationLimits.MaxNotesLength)
        {
            errors["notes"] = [VerificationErrorCodes.NotesTooLong];
        }

        if (string.IsNullOrWhiteSpace(request?.RowVersion))
        {
            errors["rowVersion"] = [VerificationErrorCodes.RowVersionRequired];
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        return ((result!.Value, method, notes.Length == 0 ? null : notes), errors);
    }

    private VerificationRecord NewRecord(
        Guid instanceId, VerificationResult result, VerificationMethod method, bool isIndependent, Guid verifierId, string? notes)
        => new()
        {
            Id = Guid.NewGuid(),
            InstanceId = instanceId,
            Result = result,
            Method = method,
            IsIndependent = isIndependent,
            VerifiedBy = verifierId,
            VerifiedAt = clock.GetUtcNow().UtcDateTime,
            Notes = notes,
        };

    private static AuditEntry RecordAudit(
        Guid userId, string action, VerificationRecord record, VulnerabilityStatus from, VulnerabilityStatus to, string? scannerRequestId)
        => new(
            userId, action, "VerificationRecord", record.Id.ToString(),
            JsonSerializer.Serialize(new { status = from.ToString() }, Json),
            JsonSerializer.Serialize(
                new
                {
                    instanceId = record.InstanceId,
                    result = record.Result.ToString(),
                    method = record.Method.ToString(),
                    isIndependent = record.IsIndependent,
                    status = to.ToString(),
                    scannerRequestId,
                },
                Json),
            "Success", AuditSource);

    private async Task AuditRescanAsync(
        InstanceInfo info, RescanResponse response, Guid userId, string result, bool reopened, CancellationToken ct)
    {
        await audit.RecordAsync(
            new AuditEntry(
                userId, ActionRescan, "VulnerabilityInstance", info.Id.ToString(), null,
                JsonSerializer.Serialize(
                    new { scannerRequestId = response.RequestId, scanStatus = response.Status.ToString(), reopened },
                    Json),
                result, AuditSource),
            ct);
    }

    private async Task<(InstanceInfo? Info, WorkflowOutcome? Failure)> LoadAsync(Guid instanceId, CancellationToken ct)
    {
        var info = await db.VulnerabilityInstances.AsNoTracking()
            .ApplyDataScope(user)
            .Where(i => i.Id == instanceId)
            .Select(i => new InstanceInfo(
                i.Id, i.AssetId, i.Asset.AssetCode, i.Title, i.CveId, i.Location, i.Port, i.Component,
                i.Status, i.RiskLevel, i.AssignedToUserId, i.RowVersion))
            .FirstOrDefaultAsync(ct);
        if (info is not null)
        {
            return (info, null);
        }

        var exists = await db.VulnerabilityInstances.AsNoTracking().AnyAsync(i => i.Id == instanceId, ct);
        return (null, exists ? WorkflowOutcome.OutOfScope : WorkflowOutcome.NotFound);
    }

    /// <summary>ผู้รับผิดชอบการแก้ไข: assignee, เจ้าของ task, ผู้ส่งเข้า Pending Verification ล่าสุด (ใช้ตัดสิน independent + แจ้งเตือน).</summary>
    private async Task<IReadOnlyList<Guid?>> LoadResponsibleAsync(InstanceInfo info, CancellationToken ct)
    {
        var owners = await db.RemediationTasks.AsNoTracking()
            .Where(t => t.InstanceId == info.Id && t.OwnerUserId != null)
            .Select(t => t.OwnerUserId)
            .Distinct()
            .ToListAsync(ct);

        var submitter = await db.StatusHistories.AsNoTracking()
            .Where(h => h.InstanceId == info.Id && h.ToStatus == VulnerabilityStatus.PendingVerification)
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => (Guid?)h.ChangedBy)
            .FirstOrDefaultAsync(ct);

        var all = new List<Guid?>(owners) { info.AssignedToUserId, submitter };
        return all;
    }

    private async Task NotifyResponsibleAsync(
        IReadOnlyList<Guid?> responsible, Guid actorId, string type, string title, string message, Guid instanceId, CancellationToken ct)
    {
        var recipients = NotificationRules.DistinctRecipients(
            responsible.Where(id => id is not null).Select(id => id!.Value), exclude: actorId);
        if (recipients.Count == 0)
        {
            return;
        }

        await TryNotifyAsync(recipients.ToList(), type, title, message, instanceId, ct);
    }

    private async Task TryNotifyAsync(
        IReadOnlyCollection<Guid> recipients, string type, string title, string message, Guid instanceId, CancellationToken ct)
    {
        try
        {
            await notifications.NotifyManyAsync(recipients, type, title, message, instanceId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                "verification.notify.failed instanceId={InstanceId} type={Type} reason={Reason}", instanceId, type, ex.GetType().Name);
        }
    }

    private Guid RequireUser()
        => user.UserId ?? throw new InvalidOperationException("Verification requires an authenticated user.");
}
