using Microsoft.EntityFrameworkCore;
using SvmApi.Application.Admin;
using SvmApi.Application.Sla;
using SvmApi.Domain.Sla;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Infrastructure.Admin;

public sealed partial class AdminService
{
    public async Task<AdminResult<IReadOnlyList<SlaPolicyDto>>> ListSlaPoliciesAsync(CancellationToken cancellationToken)
    {
        if (Deny<IReadOnlyList<SlaPolicyDto>>(PermissionCatalog.AdminSla) is { } denied) return denied;
        var rows = await db.SlaPolicies.AsNoTracking().OrderBy(x => x.RiskLevel).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        return AdminResult<IReadOnlyList<SlaPolicyDto>>.Ok(rows.Select(ToSlaDto).ToList());
    }

    public Task<AdminResult<SlaPolicyDto>> CreateSlaPolicyAsync(SlaPolicyRequest? request, CancellationToken cancellationToken)
        => SaveSlaPolicyAsync(null, request, cancellationToken);

    public Task<AdminResult<SlaPolicyDto>> UpdateSlaPolicyAsync(Guid id, SlaPolicyRequest? request, CancellationToken cancellationToken)
        => SaveSlaPolicyAsync(id, request, cancellationToken);

    private async Task<AdminResult<SlaPolicyDto>> SaveSlaPolicyAsync(Guid? id, SlaPolicyRequest? request, CancellationToken cancellationToken)
    {
        if (Deny<SlaPolicyDto>(PermissionCatalog.AdminSla) is { } denied) return denied;
        var parsed = SlaPolicyAdminRules.Parse(request, id ?? Guid.NewGuid(), id.HasValue);
        if (!parsed.Validation.IsValid) return AdminResult<SlaPolicyDto>.Invalid(parsed.Validation, AdminErrorCodes.InvalidSlaPolicy);
        var candidate = parsed.Value!;
        IReadOnlyCollection<RiskLevel> affected = [];
        var result = await WriteAsync<SlaPolicyDto>(async ct =>
        {
            affected = [];
            var rows = await db.SlaPolicies.ToListAsync(ct);
            var row = id.HasValue ? rows.SingleOrDefault(x => x.Id == id.Value) : new SlaPolicy { Id = candidate.Id };
            if (row is null) return Missing<SlaPolicyDto>(AdminErrorCodes.SlaPolicyNotFound);
            var before = id.HasValue ? ToSlaDto(row) : null;
            if (before is not null && !ConfigVersion.IsCurrent(request!.Version, before.Version)) return VersionConflict<SlaPolicyDto>();
            var previous = id.HasValue ? ToSlaCandidate(row) : null;
            var old = rows.Select(ToSlaCandidate).ToList();
            var updated = old.Where(x => x.Id != candidate.Id).Append(candidate).ToList();
            var validation = SlaPolicyAdminRules.ValidateChange(old, updated, candidate);
            if (!validation.IsValid) return AdminResult<SlaPolicyDto>.Invalid(validation, AdminErrorCodes.InvalidSlaPolicy);
            row.RiskLevel = candidate.RiskLevel;
            row.AssetCriticality = candidate.AssetCriticality;
            row.InternetExposure = candidate.InternetExposure;
            row.KevOnly = candidate.KevOnly;
            row.AcknowledgeHours = candidate.AcknowledgeHours;
            row.StartHours = candidate.StartHours;
            row.ResolveHours = candidate.ResolveHours;
            row.IsActive = candidate.IsActive;
            if (!id.HasValue) db.SlaPolicies.Add(row);
            var after = ToSlaDto(row);
            await AppendAuditAsync(id.HasValue ? ActionSlaPolicyUpdate : ActionSlaPolicyCreate, "SlaPolicy", row.Id.ToString(), before, after, ct);
            if (SlaPolicyAdminRules.AffectsTimers(previous, candidate)) affected = SlaPolicyAdminRules.AffectedRiskLevels(previous, candidate);
            return AdminResult<SlaPolicyDto>.Ok(after);
        }, cancellationToken);
        if (result.Succeeded) EnqueueSlaRecalculation(affected, "SLA_POLICY_CHANGED");
        return result;
    }

    private static SlaPolicyCandidate ToSlaCandidate(SlaPolicy row)
        => new(row.Id, row.RiskLevel, row.AssetCriticality, row.InternetExposure, row.KevOnly, row.AcknowledgeHours, row.StartHours, row.ResolveHours, row.IsActive);
    private static SlaPolicyDto ToSlaDto(SlaPolicy row)
        => new(row.Id, row.RiskLevel.ToString(), row.AssetCriticality?.ToString(), row.InternetExposure, row.KevOnly,
            row.AcknowledgeHours, row.StartHours, row.ResolveHours, row.IsActive,
            ConfigVersion.Of(row.Id, row.RiskLevel, row.AssetCriticality, row.InternetExposure, row.KevOnly, row.AcknowledgeHours, row.StartHours, row.ResolveHours, row.IsActive));
}
