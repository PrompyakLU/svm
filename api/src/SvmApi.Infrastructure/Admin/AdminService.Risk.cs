using Microsoft.EntityFrameworkCore;
using SvmApi.Application.Admin;
using SvmApi.Application.Risk;
using SvmApi.Domain.Risk;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Infrastructure.Admin;

public sealed partial class AdminService
{
    public async Task<AdminResult<RiskWeightsDto>> GetRiskWeightsAsync(CancellationToken cancellationToken)
    {
        if (Deny<RiskWeightsDto>(PermissionCatalog.AdminRisk) is { } denied) return denied;
        return AdminResult<RiskWeightsDto>.Ok(ToWeightsDto(await db.RiskWeights.AsNoTracking().ToListAsync(cancellationToken)));
    }

    public Task<AdminResult<RiskWeightsValidationDto>> ValidateRiskWeightsAsync(ReplaceRiskWeightsRequest? request, CancellationToken cancellationToken)
    {
        if (Deny<RiskWeightsValidationDto>(PermissionCatalog.AdminRisk) is { } denied) return Task.FromResult(denied);
        var parsed = RiskConfigAdminRules.ParseWeights(request);
        if (parsed.Validation.HasFormatIssues)
            return Task.FromResult(AdminResult<RiskWeightsValidationDto>.Invalid(parsed.Validation, AdminErrorCodes.InvalidRiskWeight));
        try
        {
            return Task.FromResult(AdminResult<RiskWeightsValidationDto>.Ok(new RiskWeightsValidationDto(
                parsed.Validation.IsValid, RiskConfigAdminRules.ActiveSum(parsed.Value!), parsed.Validation.Issues.Select(x => x.Code).Distinct().ToList())));
        }
        catch (OverflowException)
        {
            return Task.FromResult(AdminResult<RiskWeightsValidationDto>.Invalid(
                AdminValidation.From([new AdminIssue("weights", "WEIGHT_SUM_OVERFLOW", IsBusinessRule: true)]), AdminErrorCodes.InvalidRiskWeight));
        }
    }

    public async Task<AdminResult<RiskWeightsDto>> ReplaceRiskWeightsAsync(ReplaceRiskWeightsRequest? request, CancellationToken cancellationToken)
    {
        if (Deny<RiskWeightsDto>(PermissionCatalog.AdminRisk) is { } denied) return denied;
        var parsed = RiskConfigAdminRules.ParseWeights(request);
        if (!parsed.Validation.IsValid) return AdminResult<RiskWeightsDto>.Invalid(parsed.Validation, AdminErrorCodes.InvalidRiskWeight);
        return await WriteAsync<RiskWeightsDto>(async ct =>
        {
            var rows = await db.RiskWeights.ToListAsync(ct);
            var before = ToWeightsDto(rows);
            if (!ConfigVersion.IsCurrent(request!.Version, before.Version)) return VersionConflict<RiskWeightsDto>();
            foreach (var item in parsed.Value!)
            {
                var row = rows.SingleOrDefault(x => x.Factor == item.Factor);
                if (row is null)
                {
                    row = new RiskWeight { Id = Guid.NewGuid(), Factor = item.Factor };
                    db.RiskWeights.Add(row);
                    rows.Add(row);
                }
                row.Weight = item.Weight;
                row.IsActive = item.IsActive;
                row.UpdatedAt = UtcNow;
                row.UpdatedBy = user.UserId;
            }
            var after = ToWeightsDto(rows);
            await AppendAuditAsync(ActionRiskWeightsReplace, "RiskWeights", "all", before, after, ct);
            return AdminResult<RiskWeightsDto>.Ok(after);
        }, cancellationToken);
    }

    public async Task<AdminResult<RiskThresholdsDto>> GetRiskThresholdsAsync(CancellationToken cancellationToken)
    {
        if (Deny<RiskThresholdsDto>(PermissionCatalog.AdminRisk) is { } denied) return denied;
        return AdminResult<RiskThresholdsDto>.Ok(ToThresholdsDto(await db.RiskThresholds.AsNoTracking().ToListAsync(cancellationToken)));
    }

    public async Task<AdminResult<RiskThresholdsDto>> ReplaceRiskThresholdsAsync(ReplaceRiskThresholdsRequest? request, CancellationToken cancellationToken)
    {
        if (Deny<RiskThresholdsDto>(PermissionCatalog.AdminRisk) is { } denied) return denied;
        var parsed = RiskConfigAdminRules.ParseThresholds(request);
        if (!parsed.Validation.IsValid) return AdminResult<RiskThresholdsDto>.Invalid(parsed.Validation, AdminErrorCodes.InvalidRiskThreshold);
        return await WriteAsync<RiskThresholdsDto>(async ct =>
        {
            var rows = await db.RiskThresholds.ToListAsync(ct);
            var before = ToThresholdsDto(rows);
            if (!ConfigVersion.IsCurrent(request!.Version, before.Version)) return VersionConflict<RiskThresholdsDto>();
            foreach (var item in parsed.Value!)
            {
                var row = rows.SingleOrDefault(x => x.RiskLevel == item.Level);
                if (row is null)
                {
                    row = new RiskThreshold { Id = Guid.NewGuid(), RiskLevel = item.Level };
                    db.RiskThresholds.Add(row);
                    rows.Add(row);
                }
                row.MinScore = item.MinScore;
                row.MaxScore = item.MaxScore;
            }
            var after = ToThresholdsDto(rows);
            await AppendAuditAsync(ActionRiskThresholdsReplace, "RiskThresholds", "all", before, after, ct);
            return AdminResult<RiskThresholdsDto>.Ok(after);
        }, cancellationToken);
    }

    private static RiskWeightsDto ToWeightsDto(IEnumerable<RiskWeight> rows)
    {
        var items = rows.OrderBy(x => x.Factor).Select(x => new RiskWeightDto(x.Factor.ToString(), x.Weight, x.IsActive, x.UpdatedAt)).ToList();
        return new(items, items.Where(x => x.IsActive).Sum(x => x.Weight),
            ConfigVersion.Of(items.Select(x => ConfigVersion.Of(x.Factor, x.Weight, x.IsActive, x.UpdatedAt)).Cast<object?>().ToArray()));
    }

    private static RiskThresholdsDto ToThresholdsDto(IEnumerable<RiskThreshold> rows)
    {
        var items = rows.OrderBy(x => x.RiskLevel).Select(x => new RiskThresholdDto(x.RiskLevel.ToString(), x.MinScore, x.MaxScore)).ToList();
        return new(items, ConfigVersion.Of(items.Select(x => ConfigVersion.Of(x.RiskLevel, x.MinScore, x.MaxScore)).Cast<object?>().ToArray()));
    }
}
