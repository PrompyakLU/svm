using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SvmApi.Application.Admin;
using SvmApi.Domain.Reference;
using SvmApi.Domain.Sla;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Infrastructure.Admin;

public sealed partial class AdminService
{
    public Task<AdminResult<IReadOnlyList<MasterDataTypeDto>>> ListMasterDataTypesAsync(CancellationToken cancellationToken)
        => Task.FromResult(Deny<IReadOnlyList<MasterDataTypeDto>>(PermissionCatalog.AdminMasterData)
            ?? AdminResult<IReadOnlyList<MasterDataTypeDto>>.Ok(MasterDataTypes.All));

    public async Task<AdminResult<IReadOnlyList<MasterDataItemDto>>> ListMasterDataAsync(
        string type, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (Deny<IReadOnlyList<MasterDataItemDto>>(PermissionCatalog.AdminMasterData) is { } denied) return denied;
        if (!MasterDataAdminRules.TryNormalizeType(type, out var kind)) return UnknownType<IReadOnlyList<MasterDataItemDto>>();
        if (MasterDataAdminRules.IsReadOnly(kind))
            return AdminResult<IReadOnlyList<MasterDataItemDto>>.Ok(MasterDataAdminRules.EnumValues(kind)
                .Select(code => new MasterDataItemDto(null, code, null, true, true, ConfigVersion.Of(kind, code))).ToList());
        if (kind == MasterDataTypes.CvssVersions)
            return AdminResult<IReadOnlyList<MasterDataItemDto>>.Ok((await db.CvssVersions.AsNoTracking()
                .OrderBy(x => x.Version).ToListAsync(cancellationToken)).Select(ToMasterDto).ToList());
        if (from is { Year: < MasterDataAdminRules.MinYear or > MasterDataAdminRules.MaxYear }
            || to is { Year: < MasterDataAdminRules.MinYear or > MasterDataAdminRules.MaxYear })
            return AdminResult<IReadOnlyList<MasterDataItemDto>>.Invalid(
                AdminValidation.From([new AdminIssue("from", "DATE_OUT_OF_RANGE")]), AdminErrorCodes.InvalidMasterData);
        var range = MasterDataAdminRules.ResolveCalendarRange(from, to, DateOnly.FromDateTime(UtcNow));
        if (!range.Validation.IsValid)
            return AdminResult<IReadOnlyList<MasterDataItemDto>>.Invalid(range.Validation, AdminErrorCodes.InvalidMasterData);
        return AdminResult<IReadOnlyList<MasterDataItemDto>>.Ok((await db.BusinessCalendars.AsNoTracking()
            .Where(x => x.Date >= range.From && x.Date <= range.To).OrderBy(x => x.Date)
            .ToListAsync(cancellationToken)).Select(ToMasterDto).ToList());
    }

    public Task<AdminResult<MasterDataItemDto>> CreateMasterDataAsync(string type, MasterDataRequest? request, CancellationToken cancellationToken)
        => SaveMasterDataAsync(type, null, request, cancellationToken);

    public Task<AdminResult<MasterDataItemDto>> UpdateMasterDataAsync(string type, Guid id, MasterDataRequest? request, CancellationToken cancellationToken)
        => SaveMasterDataAsync(type, id, request, cancellationToken);

    private async Task<AdminResult<MasterDataItemDto>> SaveMasterDataAsync(string type, Guid? id, MasterDataRequest? request, CancellationToken cancellationToken)
    {
        if (Deny<MasterDataItemDto>(PermissionCatalog.AdminMasterData) is { } denied) return denied;
        if (!MasterDataAdminRules.TryNormalizeType(type, out var kind)) return UnknownType<MasterDataItemDto>();
        if (MasterDataAdminRules.IsReadOnly(kind))
            return AdminResult<MasterDataItemDto>.Fail(AdminOutcome.Unprocessable, AdminErrorCodes.MasterDataReadOnly);
        var parsed = MasterDataAdminRules.Parse(kind, request, id.HasValue);
        if (!parsed.Validation.IsValid) return AdminResult<MasterDataItemDto>.Invalid(parsed.Validation, AdminErrorCodes.InvalidMasterData);
        // CVSS rows have no description column: reject values that cannot be persisted.
        if (kind == MasterDataTypes.CvssVersions && parsed.Value!.Description is not null)
            return AdminResult<MasterDataItemDto>.Invalid(AdminValidation.From([new AdminIssue("description", "NOT_SUPPORTED")]), AdminErrorCodes.InvalidMasterData);
        var result = await WriteAsync<MasterDataItemDto>(async ct =>
        {
            MasterDataItemDto? before = null;
            MasterDataItemDto after;
            if (kind == MasterDataTypes.CvssVersions)
            {
                var row = id.HasValue ? await db.CvssVersions.SingleOrDefaultAsync(x => x.Id == id.Value, ct) : new CvssVersion { Id = Guid.NewGuid() };
                if (row is null) return Missing<MasterDataItemDto>(AdminErrorCodes.MasterDataNotFound);
                if (id.HasValue) before = ToMasterDto(row);
                if (before is not null && !ConfigVersion.IsCurrent(request!.Version, before.Version)) return VersionConflict<MasterDataItemDto>();
                var code = string.IsNullOrEmpty(parsed.Value!.Code) ? row.Version : parsed.Value.Code;
                if (id.HasValue && code != row.Version) return ImmutableCode();
                if (await db.CvssVersions.AnyAsync(x => x.Id != row.Id && x.Version == code, ct)) return Duplicate<MasterDataItemDto>();
                row.Version = code;
                row.IsSupported = parsed.Value.IsActive;
                if (!id.HasValue) db.CvssVersions.Add(row);
                after = ToMasterDto(row);
            }
            else
            {
                var row = id.HasValue ? await db.BusinessCalendars.SingleOrDefaultAsync(x => x.Id == id.Value, ct) : new BusinessCalendar { Id = Guid.NewGuid() };
                if (row is null) return Missing<MasterDataItemDto>(AdminErrorCodes.MasterDataNotFound);
                if (id.HasValue) before = ToMasterDto(row);
                if (before is not null && !ConfigVersion.IsCurrent(request!.Version, before.Version)) return VersionConflict<MasterDataItemDto>();
                var date = string.IsNullOrEmpty(parsed.Value!.Code) ? row.Date : DateOnly.ParseExact(parsed.Value.Code, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (id.HasValue && date != row.Date) return ImmutableCode();
                if (await db.BusinessCalendars.AnyAsync(x => x.Id != row.Id && x.Date == date, ct)) return Duplicate<MasterDataItemDto>();
                row.Date = date;
                row.IsWorkingDay = parsed.Value.IsActive;
                row.Description = parsed.Value.Description;
                if (!id.HasValue) db.BusinessCalendars.Add(row);
                after = ToMasterDto(row);
            }
            await AppendAuditAsync(id.HasValue ? ActionMasterDataUpdate : ActionMasterDataCreate, kind, after.Id!.Value.ToString(), before, after, ct);
            return AdminResult<MasterDataItemDto>.Ok(after);
        }, cancellationToken);
        if (result.Succeeded)
        {
            if (kind == MasterDataTypes.CvssVersions) referenceCache.Invalidate();
            else EnqueueSlaRecalculation(Enum.GetValues<RiskLevel>(), "BUSINESS_CALENDAR_CHANGED");
        }
        return result;
    }

    private static MasterDataItemDto ToMasterDto(CvssVersion row)
        => new(row.Id, row.Version, null, row.IsSupported, false, ConfigVersion.Of(row.Id, row.Version, row.IsSupported));
    private static MasterDataItemDto ToMasterDto(BusinessCalendar row)
        => new(row.Id, row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row.Description, row.IsWorkingDay, false,
            ConfigVersion.Of(row.Id, row.Date, row.Description, row.IsWorkingDay));
    private static AdminResult<T> UnknownType<T>() => Missing<T>(AdminErrorCodes.MasterDataTypeNotFound);
    private static AdminResult<MasterDataItemDto> ImmutableCode() => AdminResult<MasterDataItemDto>.Invalid(
        AdminValidation.From([new AdminIssue("code", "CODE_IMMUTABLE", IsBusinessRule: true)]), AdminErrorCodes.InvalidMasterData);
    private static AdminResult<T> Missing<T>(string code) => AdminResult<T>.Fail(AdminOutcome.NotFound, code);
    private static AdminResult<T> Duplicate<T>() => AdminResult<T>.Fail(AdminOutcome.Conflict, AdminErrorCodes.DuplicateConfig);
}
