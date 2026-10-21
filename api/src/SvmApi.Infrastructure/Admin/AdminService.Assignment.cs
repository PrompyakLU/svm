using Microsoft.EntityFrameworkCore;
using SvmApi.Application.Admin;
using SvmApi.Domain.Remediation;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Infrastructure.Admin;

public sealed partial class AdminService
{
    public async Task<AdminResult<IReadOnlyList<AssignmentRuleDto>>> ListAssignmentRulesAsync(CancellationToken cancellationToken)
    {
        if (Deny<IReadOnlyList<AssignmentRuleDto>>(PermissionCatalog.AdminAssignment) is { } denied) return denied;
        var rows = await db.AssignmentRules.AsNoTracking().ToListAsync(cancellationToken);
        var byId = rows.ToDictionary(x => x.Id);
        return AdminResult<IReadOnlyList<AssignmentRuleDto>>.Ok(AssignmentRuleAdminRules.Order(rows.Select(ToAssignmentRow))
            .Select(x => ToAssignmentDto(byId[x.Id])).ToList());
    }

    public Task<AdminResult<AssignmentRuleDto>> CreateAssignmentRuleAsync(AssignmentRuleRequest? request, CancellationToken cancellationToken)
        => SaveAssignmentRuleAsync(null, request, cancellationToken);

    public Task<AdminResult<AssignmentRuleDto>> UpdateAssignmentRuleAsync(Guid id, AssignmentRuleRequest? request, CancellationToken cancellationToken)
        => SaveAssignmentRuleAsync(id, request, cancellationToken);

    private async Task<AdminResult<AssignmentRuleDto>> SaveAssignmentRuleAsync(Guid? id, AssignmentRuleRequest? request, CancellationToken cancellationToken)
    {
        if (Deny<AssignmentRuleDto>(PermissionCatalog.AdminAssignment) is { } denied) return denied;
        var parsed = AssignmentRuleAdminRules.Parse(request, id.HasValue);
        if (!parsed.Validation.IsValid) return AdminResult<AssignmentRuleDto>.Invalid(parsed.Validation, AdminErrorCodes.InvalidAssignmentRule);
        var draft = parsed.Value!;
        return await WriteAsync<AssignmentRuleDto>(async ct =>
        {
            var rows = await db.AssignmentRules.ToListAsync(ct);
            var row = id.HasValue ? rows.SingleOrDefault(x => x.Id == id.Value) : new AssignmentRule { Id = Guid.NewGuid() };
            if (row is null) return Missing<AssignmentRuleDto>(AdminErrorCodes.AssignmentRuleNotFound);
            var before = id.HasValue ? ToAssignmentDto(row) : null;
            if (before is not null && !ConfigVersion.IsCurrent(request!.Version, before.Version)) return VersionConflict<AssignmentRuleDto>();
            if (draft.AssignToUserId is { } assignee && !await db.Users.AsNoTracking()
                .AnyAsync(x => x.Id == assignee && x.IsActive && (x.ExpiresAt == null || x.ExpiresAt > UtcNow), ct))
                return AdminResult<AssignmentRuleDto>.Fail(AdminOutcome.Unprocessable, AdminErrorCodes.AssigneeNotFound);
            row.MatchType = draft.MatchType;
            row.MatchValue = draft.MatchValue;
            row.AssignToUserId = draft.AssignToUserId;
            row.AssignToTeam = draft.AssignToTeam;
            row.Priority = draft.Priority;
            row.IsActive = draft.IsActive;
            if (!id.HasValue) rows.Add(row);
            var validation = AssignmentRuleAdminRules.ValidateSet(rows.Select(ToAssignmentRow).ToList());
            if (!validation.IsValid) return AdminResult<AssignmentRuleDto>.Invalid(validation, AdminErrorCodes.InvalidAssignmentRule);
            if (!id.HasValue) db.AssignmentRules.Add(row);
            var after = ToAssignmentDto(row);
            await AppendAuditAsync(id.HasValue ? ActionAssignmentRuleUpdate : ActionAssignmentRuleCreate, "AssignmentRule", row.Id.ToString(), before, after, ct);
            return AdminResult<AssignmentRuleDto>.Ok(after);
        }, cancellationToken);
    }

    private static AssignmentRuleRow ToAssignmentRow(AssignmentRule row)
        => new(row.Id, row.MatchType, row.MatchValue, row.AssignToUserId, row.AssignToTeam, row.Priority, row.IsActive);
    private static AssignmentRuleDto ToAssignmentDto(AssignmentRule row)
        => new(row.Id, row.MatchType.ToString(), row.MatchValue, row.AssignToUserId, row.AssignToTeam, row.Priority, row.IsActive,
            ConfigVersion.Of(row.Id, row.MatchType, row.MatchValue, row.AssignToUserId, row.AssignToTeam, row.Priority, row.IsActive));
}
