using Microsoft.EntityFrameworkCore;
using SvmApi.Application.Admin;
using SvmApi.Application.Workflow;
using SvmApi.Domain.Workflow;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Infrastructure.Admin;

public sealed partial class AdminService
{
    public async Task<AdminResult<IReadOnlyList<StatusTransitionDto>>> ListStatusTransitionsAsync(CancellationToken cancellationToken)
    {
        if (Deny<IReadOnlyList<StatusTransitionDto>>(PermissionCatalog.AdminWorkflow) is { } denied) return denied;
        var rows = await db.StatusTransitions.AsNoTracking().OrderBy(x => x.FromStatus).ThenBy(x => x.ToStatus).ToListAsync(cancellationToken);
        return AdminResult<IReadOnlyList<StatusTransitionDto>>.Ok(rows.Select(ToTransitionDto).ToList());
    }

    public async Task<AdminResult<StatusTransitionDto>> CreateStatusTransitionAsync(CreateStatusTransitionRequest? request, CancellationToken cancellationToken)
    {
        if (Deny<StatusTransitionDto>(PermissionCatalog.AdminWorkflow) is { } denied) return denied;
        var parsed = StatusTransitionAdminRules.ParseCreate(request);
        if (!parsed.Validation.IsValid) return AdminResult<StatusTransitionDto>.Invalid(parsed.Validation, AdminErrorCodes.InvalidStatusTransition);
        var value = parsed.Value!;
        return await WriteAsync<StatusTransitionDto>(async ct =>
        {
            var rows = await db.StatusTransitions.ToListAsync(ct);
            if (rows.Any(x => x.FromStatus == value.From && x.ToStatus == value.To)) return Duplicate<StatusTransitionDto>();
            var row = new StatusTransition { Id = Guid.NewGuid(), FromStatus = value.From, ToStatus = value.To,
                RequiredPermission = value.RequiredPermission, RequiresEvidence = value.RequiresEvidence,
                RequiresApproval = value.RequiresApproval, IsActive = value.IsActive };
            var validation = StatusTransitionAdminRules.ValidateChange(rows.Select(ToTransitionRule).ToList(),
                rows.Append(row).Select(ToTransitionRule).ToList(), await KnownPermissionsAsync(ct));
            if (!validation.IsValid) return AdminResult<StatusTransitionDto>.Invalid(validation, AdminErrorCodes.InvalidStatusTransition);
            db.StatusTransitions.Add(row);
            var after = ToTransitionDto(row);
            await AppendAuditAsync(ActionTransitionCreate, "StatusTransition", row.Id.ToString(), null, after, ct);
            return AdminResult<StatusTransitionDto>.Ok(after);
        }, cancellationToken);
    }

    public async Task<AdminResult<StatusTransitionDto>> UpdateStatusTransitionAsync(Guid id, UpdateStatusTransitionRequest? request, CancellationToken cancellationToken)
    {
        if (Deny<StatusTransitionDto>(PermissionCatalog.AdminWorkflow) is { } denied) return denied;
        var parsed = StatusTransitionAdminRules.ParseUpdate(request, out var permission);
        if (!parsed.Validation.IsValid) return AdminResult<StatusTransitionDto>.Invalid(parsed.Validation, AdminErrorCodes.InvalidStatusTransition);
        return await WriteAsync<StatusTransitionDto>(async ct =>
        {
            var rows = await db.StatusTransitions.ToListAsync(ct);
            var row = rows.SingleOrDefault(x => x.Id == id);
            if (row is null) return Missing<StatusTransitionDto>(AdminErrorCodes.StatusTransitionNotFound);
            var before = ToTransitionDto(row);
            if (!ConfigVersion.IsCurrent(request!.Version, before.Version)) return VersionConflict<StatusTransitionDto>();
            var previousRules = rows.Select(ToTransitionRule).ToList();
            row.RequiredPermission = permission;
            row.RequiresEvidence = request.RequiresEvidence!.Value;
            row.RequiresApproval = request.RequiresApproval!.Value;
            row.IsActive = request.IsActive!.Value;
            var validation = StatusTransitionAdminRules.ValidateChange(previousRules, rows.Select(ToTransitionRule).ToList(), await KnownPermissionsAsync(ct));
            if (!validation.IsValid) return AdminResult<StatusTransitionDto>.Invalid(validation, AdminErrorCodes.InvalidStatusTransition);
            var after = ToTransitionDto(row);
            await AppendAuditAsync(ActionTransitionUpdate, "StatusTransition", row.Id.ToString(), before, after, ct);
            return AdminResult<StatusTransitionDto>.Ok(after);
        }, cancellationToken);
    }

    private async Task<HashSet<string>> KnownPermissionsAsync(CancellationToken ct)
        => (await db.Permissions.AsNoTracking().Select(x => x.Code).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

    private static TransitionRule ToTransitionRule(StatusTransition row)
        => new(row.FromStatus, row.ToStatus, row.RequiredPermission, row.RequiresEvidence, row.RequiresApproval, row.IsActive);

    private static StatusTransitionDto ToTransitionDto(StatusTransition row)
        => new(row.Id, row.FromStatus.ToString(), row.ToStatus.ToString(), row.RequiredPermission,
            row.RequiresEvidence, row.RequiresApproval, row.IsActive,
            ConfigVersion.Of(row.Id, row.FromStatus, row.ToStatus, row.RequiredPermission, row.RequiresEvidence, row.RequiresApproval, row.IsActive));
}
