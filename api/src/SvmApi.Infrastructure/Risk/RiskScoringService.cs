using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SvmApi.Application.Audit;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Application.Reference;
using SvmApi.Application.Risk;
using SvmApi.Application.Sla;
using SvmApi.Domain.Risk;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Infrastructure.Auth.DataScope;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Risk;

/// <summary>
/// <see cref="IRiskScoringService"/> (C6 · US-015/016/017): โหลด weight/threshold จาก DB ตอน runtime (NFR-31),
/// เรียก <see cref="RiskScoringEngine"/> (pure) แล้วบันทึก instance + <c>trans_riskScoreHistory</c> + audit ใน transaction เดียว.
/// <list type="bullet">
/// <item>instance กรองด้วย data scope (fail-closed); มีอยู่แต่นอก scope = <see cref="RiskOutcome.OutOfScope"/> (ตอบ 404).</item>
/// <item>rowVersion ไม่ตรง หรือชนกันตอนบันทึก = <see cref="RiskOutcome.Conflict"/> (409, EX-57).</item>
/// <item>config/ข้อมูลผิด (EX-50/51/54) = <see cref="RiskOutcome.Unprocessable"/> — ไม่แตะ score เดิม.</item>
/// <item>audit ไม่เก็บข้อความเหตุผล (อาจมี PII) — เก็บเฉพาะ id + ค่าเดิม/ใหม่; เหตุผลอยู่ใน history.</item>
/// </list>
/// config ถูก cache ต่อ scope (หนึ่ง request / หนึ่ง import) เพื่อไม่ยิง DB ทุกแถว.
/// </summary>
public sealed class RiskScoringService(
    SvmDbContext db,
    IUnitOfWork unitOfWork,
    IAuditService audit,
    IReferenceDataService reference,
    ICurrentUser user,
    TimeProvider clock,
    ILogger<RiskScoringService> logger,
    ISlaService? sla = null) : IRiskScoringService
{
    public const string AuditSource = "RiskScoringEngine";
    public const string ActionRecalculate = "instance.risk.recalculate";
    public const string ActionOverride = "instance.risk.adjust";

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private (IReadOnlyList<RiskWeightConfig> Weights, IReadOnlyList<RiskThresholdConfig> Thresholds)? _config;

    // ------------------------------------------------------------------ recalc

    public async Task<RiskResult<InstanceRiskDto>> RecalculateAsync(
        Guid instanceId, RecalculateRiskRequest? request, CancellationToken cancellationToken)
    {
        var userId = user.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

        var expected = RiskScoringEngine.ParseRowVersion(request?.RowVersion, required: false, out var rvError);
        if (rvError is not null)
        {
            return RiskResult<InstanceRiskDto>.Invalid(new Dictionary<string, string[]> { ["rowVersion"] = [rvError] });
        }

        var (instance, notFound) = await LoadScopedAsync(instanceId, cancellationToken);
        if (instance is null)
        {
            return notFound!;
        }

        if (expected is not null && !expected.AsSpan().SequenceEqual(instance.RowVersion))
        {
            return ConcurrencyConflict();
        }

        // KEV อาจเปลี่ยน (FR-21) — ติดแล้วไม่ถอด เหมือน intake
        var isKev = instance.IsKev || await reference.IsKevAsync(instance.CveId, cancellationToken);

        var inputs = await LoadInputsAsync(instance, isKev, cancellationToken);
        var config = await LoadConfigAsync(cancellationToken);
        var now = Now();
        var calc = inputs is null
            ? RiskCalculation.Fail(RiskErrorCodes.RiskInputIncomplete, ["ASSET_NOT_FOUND"])
            : RiskScoringEngine.Calculate(inputs, config.Weights, config.Thresholds, now);

        if (!calc.Succeeded)
        {
            logger.LogWarning(
                "risk.recalculate.failed instanceId={InstanceId} code={Code} issues={Issues}",
                instance.Id, calc.ErrorCode, string.Join(",", calc.Issues));
            return Unprocessable(calc);
        }

        var previousScore = instance.RiskScore;
        var previousLevel = instance.RiskLevel;

        instance.IsKev = isKev;
        instance.RiskScore = calc.Score;
        instance.RiskLevel = calc.Level;
        instance.UpdatedAt = now;
        instance.UpdatedBy = userId;

        var history = NewHistory(
            instance.Id, calc.Score, calc.Level, RiskCalculationType.Recalc,
            RiskScoringEngine.SerializeBreakdown(calc.Breakdown!), reason: null, previousScore, now, userId);
        db.RiskScoreHistories.Add(history);

        var saved = await SaveWithAuditAsync(
            instance, ActionRecalculate, previousScore, previousLevel, history, cancellationToken);
        if (saved is not null)
        {
            return saved;
        }

        await TriggerSlaAsync(instance.Id, cancellationToken);

        return RiskResult<InstanceRiskDto>.Ok(ToDto(instance, history, previousScore, previousLevel, calc.Breakdown));
    }

    // ------------------------------------------------------------------ manual override

    public async Task<RiskResult<InstanceRiskDto>> OverrideAsync(
        Guid instanceId, OverrideRiskRequest request, CancellationToken cancellationToken)
    {
        var userId = user.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

        var (value, errors) = RiskScoringEngine.ValidateOverride(request);
        if (value is null)
        {
            return RiskResult<InstanceRiskDto>.Invalid(errors);
        }

        var (instance, notFound) = await LoadScopedAsync(instanceId, cancellationToken);
        if (instance is null)
        {
            return notFound!;
        }

        if (!value.RowVersion.AsSpan().SequenceEqual(instance.RowVersion))
        {
            return ConcurrencyConflict();
        }

        // score อย่างเดียว → จัดระดับจาก threshold ที่อนุมัติ; level อย่างเดียว → คง score เดิม (ต้องเคยมี score)
        RiskLevel level;
        var levelDerived = false;
        if (value.Level is { } explicitLevel)
        {
            level = explicitLevel;
        }
        else
        {
            var config = await LoadConfigAsync(cancellationToken);
            var thresholdCheck = RiskScoringEngine.ValidateThresholds(config.Thresholds);
            var classified = thresholdCheck.IsValid ? RiskScoringEngine.Classify(value.Score!.Value, config.Thresholds) : null;
            if (classified is null)
            {
                return Unprocessable(RiskCalculation.Fail(RiskErrorCodes.InvalidRiskThreshold, thresholdCheck.Issues));
            }

            level = classified.Value;
            levelDerived = true;
        }

        var score = value.Score ?? instance.RiskScore;
        if (score is null)
        {
            return RiskResult<InstanceRiskDto>.Invalid(
                new Dictionary<string, string[]> { ["score"] = [RiskErrorCodes.OverrideValueRequired] },
                RiskErrorCodes.OverrideValueRequired);
        }

        var finalScore = RiskScoringEngine.ClampScore(score.Value);
        var previousScore = instance.RiskScore;
        var previousLevel = instance.RiskLevel;
        var now = Now();

        instance.RiskScore = finalScore;
        instance.RiskLevel = level;
        instance.UpdatedAt = now;
        instance.UpdatedBy = userId;

        var breakdown = new RiskManualBreakdown(
            RiskScoringEngine.ManualModelName, previousScore, previousLevel?.ToString(), finalScore, level.ToString(), levelDerived);
        var history = NewHistory(
            instance.Id, finalScore, level, RiskCalculationType.Manual,
            RiskScoringEngine.SerializeBreakdown(breakdown), value.Reason, previousScore, now, userId);
        db.RiskScoreHistories.Add(history);

        var saved = await SaveWithAuditAsync(instance, ActionOverride, previousScore, previousLevel, history, cancellationToken);
        if (saved is not null)
        {
            return saved;
        }

        await TriggerSlaAsync(instance.Id, cancellationToken);

        return RiskResult<InstanceRiskDto>.Ok(ToDto(instance, history, previousScore, previousLevel, breakdown: null));
    }

    // ------------------------------------------------------------------ intake hook

    /// <summary>
    /// คะแนนเริ่มต้นของ instance ใหม่จาก intake (US-015). ไม่ throw (ยกเว้นยกเลิก): คำนวณไม่ได้ = คง instance ไว้แบบยังไม่มี score
    /// (riskScore/riskLevel = null) + log warning — ไม่ให้ score ที่ผิดถูกใช้ (EX-50/51/54).
    /// เพิ่ม history (Auto) ลง DbContext เท่านั้น; ผู้เรียกเป็นผู้ SaveChanges ภายใน unit of work ของตน.
    /// </summary>
    public async Task<bool> TryScoreNewInstanceAsync(
        VulnerabilityInstance instance, decimal? cvssBaseScore, string? severity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        try
        {
            var asset = await db.Assets.AsNoTracking()
                .Where(a => a.Id == instance.AssetId)
                .Select(a => new { a.Criticality, a.Environment, a.InternetExposure })
                .FirstOrDefaultAsync(cancellationToken);
            if (asset is null)
            {
                logger.LogWarning("risk.initial.unscored instanceId={InstanceId} code={Code}", instance.Id, RiskErrorCodes.RiskInputIncomplete);
                return false;
            }

            var config = await LoadConfigAsync(cancellationToken);
            var now = Now();
            var calc = RiskScoringEngine.Calculate(
                new RiskInputs(cvssBaseScore, severity, asset.Criticality, asset.Environment, asset.InternetExposure, instance.IsKev, instance.FirstDetectedAt),
                config.Weights, config.Thresholds, now);

            if (!calc.Succeeded)
            {
                logger.LogWarning(
                    "risk.initial.unscored instanceId={InstanceId} code={Code} issues={Issues}",
                    instance.Id, calc.ErrorCode, string.Join(",", calc.Issues));
                return false;
            }

            instance.RiskScore = calc.Score;
            instance.RiskLevel = calc.Level;
            db.RiskScoreHistories.Add(NewHistory(
                instance.Id, calc.Score, calc.Level, RiskCalculationType.Auto,
                RiskScoringEngine.SerializeBreakdown(calc.Breakdown!), reason: null, previousScore: null, now, user.UserId));
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            instance.RiskScore = null;
            instance.RiskLevel = null;
            logger.LogWarning("risk.initial.unscored instanceId={InstanceId} reason={Reason}", instance.Id, ex.GetType().Name);
            return false;
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>US-025: สร้าง/คำนวณ SLA timer ใหม่หลัง risk เปลี่ยน. ไม่ throw (ล้มเหลวของ SLA ต้องไม่ทำให้ risk update ล้ม); SlaMonitorJob เป็น safety net.</summary>
    private async Task TriggerSlaAsync(Guid instanceId, CancellationToken ct)
    {
        if (sla is not null)
        {
            await sla.TryCalculateAsync(instanceId, SlaTrigger.RiskLevelChanged, ct);
        }
    }

    private async Task<(VulnerabilityInstance? Instance, RiskResult<InstanceRiskDto>? NotFound)> LoadScopedAsync(
        Guid instanceId, CancellationToken ct)
    {
        var instance = await db.VulnerabilityInstances.ApplyDataScope(user).FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is not null)
        {
            return (instance, null);
        }

        var exists = await db.VulnerabilityInstances.AsNoTracking().AnyAsync(i => i.Id == instanceId, ct);
        return (null, RiskResult<InstanceRiskDto>.Fail(
            exists ? RiskOutcome.OutOfScope : RiskOutcome.NotFound, RiskErrorCodes.InstanceNotFound));
    }

    /// <summary>ปัจจัยของ instance เดิม: asset ปัจจุบัน + CVSS ล่าสุดที่มีจาก finding + severity ล่าสุด.</summary>
    private async Task<RiskInputs?> LoadInputsAsync(VulnerabilityInstance instance, bool isKev, CancellationToken ct)
    {
        var asset = await db.Assets.AsNoTracking()
            .Where(a => a.Id == instance.AssetId)
            .Select(a => new { a.Criticality, a.Environment, a.InternetExposure })
            .FirstOrDefaultAsync(ct);
        if (asset is null)
        {
            return null;
        }

        var findings = db.VulnerabilityFindings.AsNoTracking().Where(f => f.InstanceId == instance.Id);
        var cvss = await findings
            .Where(f => f.CvssBaseScore != null)
            .OrderByDescending(f => f.DetectedAt).ThenByDescending(f => f.CreatedAt)
            .Select(f => f.CvssBaseScore)
            .FirstOrDefaultAsync(ct);
        var severity = await findings
            .Where(f => f.RawSeverity != null)
            .OrderByDescending(f => f.DetectedAt).ThenByDescending(f => f.CreatedAt)
            .Select(f => f.RawSeverity)
            .FirstOrDefaultAsync(ct);

        return new RiskInputs(cvss, severity, asset.Criticality, asset.Environment, asset.InternetExposure, isKev, instance.FirstDetectedAt);
    }

    private async Task<(IReadOnlyList<RiskWeightConfig> Weights, IReadOnlyList<RiskThresholdConfig> Thresholds)> LoadConfigAsync(
        CancellationToken ct)
    {
        if (_config is { } cached)
        {
            return cached;
        }

        var weights = await db.RiskWeights.AsNoTracking()
            .Select(w => new RiskWeightConfig(w.Factor, w.Weight, w.IsActive))
            .ToListAsync(ct);
        var thresholds = await db.RiskThresholds.AsNoTracking()
            .Select(t => new RiskThresholdConfig(t.RiskLevel, t.MinScore, t.MaxScore))
            .ToListAsync(ct);

        var config = ((IReadOnlyList<RiskWeightConfig>)weights, (IReadOnlyList<RiskThresholdConfig>)thresholds);
        _config = config;
        return config;
    }

    /// <summary>บันทึก instance + history + audit ใน transaction เดียว; คืน null เมื่อสำเร็จ, หรือผล 409 เมื่อชนกัน.</summary>
    private async Task<RiskResult<InstanceRiskDto>?> SaveWithAuditAsync(
        VulnerabilityInstance instance,
        string action,
        decimal? previousScore,
        RiskLevel? previousLevel,
        RiskScoreHistory history,
        CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.ExecuteAsync(async ct =>
            {
                await audit.AppendAsync(
                    new AuditEntry(
                        history.CreatedBy, action, "VulnerabilityInstance", instance.Id.ToString(),
                        JsonSerializer.Serialize(new { riskScore = previousScore, riskLevel = previousLevel?.ToString() }, Json),
                        JsonSerializer.Serialize(
                            new
                            {
                                riskScore = history.Score,
                                riskLevel = history.RiskLevel.ToString(),
                                calculationType = history.CalculationType.ToString(),
                                historyId = history.Id,
                                hasReason = history.Reason is not null,
                            },
                            Json),
                        "Success", AuditSource),
                    ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return ConcurrencyConflict();
        }

        logger.LogInformation(
            "risk.updated instanceId={InstanceId} type={Type} score={Score} level={Level}",
            instance.Id, history.CalculationType, history.Score, history.RiskLevel);
        return null;
    }

    private static RiskScoreHistory NewHistory(
        Guid instanceId,
        decimal score,
        RiskLevel level,
        RiskCalculationType type,
        string breakdownJson,
        string? reason,
        decimal? previousScore,
        DateTime now,
        Guid? userId) => new()
    {
        Id = Guid.NewGuid(),
        InstanceId = instanceId,
        Score = score,
        RiskLevel = level,
        CalculationType = type,
        FactorBreakdown = breakdownJson,
        Reason = reason,
        PreviousScore = previousScore,
        CreatedAt = now,
        CreatedBy = userId,
    };

    private static InstanceRiskDto ToDto(
        VulnerabilityInstance instance,
        RiskScoreHistory history,
        decimal? previousScore,
        RiskLevel? previousLevel,
        RiskBreakdown? breakdown)
        => new(
            instance.Id,
            history.Score,
            history.RiskLevel.ToString(),
            history.CalculationType.ToString(),
            previousScore,
            previousLevel?.ToString(),
            history.Id,
            Convert.ToBase64String(instance.RowVersion),
            breakdown);

    private static RiskResult<InstanceRiskDto> ConcurrencyConflict()
        => RiskResult<InstanceRiskDto>.Fail(
            RiskOutcome.Conflict, RiskErrorCodes.ConcurrencyConflict, "The record was changed by another user. Reload and retry.");

    private static RiskResult<InstanceRiskDto> Unprocessable(RiskCalculation calc)
        => new(
            RiskOutcome.Unprocessable,
            default,
            new Dictionary<string, string[]> { ["risk"] = calc.Issues.ToArray() },
            calc.ErrorCode,
            "The risk score cannot be calculated with the current data or configuration.");

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
