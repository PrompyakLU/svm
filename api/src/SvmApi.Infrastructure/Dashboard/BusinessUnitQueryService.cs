using Microsoft.EntityFrameworkCore;
using SvmApi.Application.Auth;
using SvmApi.Application.Dashboard;
using SvmApi.Infrastructure.Auth.DataScope;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Dashboard;

public sealed class BusinessUnitQueryService(SvmDbContext db, ICurrentUser user) : IBusinessUnitQueryService
{
    public async Task<BusinessUnitsDto> GetAsync(CancellationToken cancellationToken)
    {
        // Include assigned-instance grants as well as directly visible assets.
        var visibleAssets = db.Assets.AsNoTracking().ApplyDataScope(user)
            .Select(a => a.BusinessUnit);
        var visibleInstances = db.VulnerabilityInstances.AsNoTracking().ApplyDataScope(user)
            .Select(i => i.Asset.BusinessUnit);
        var items = await visibleAssets.Union(visibleInstances)
            .Where(b => b != null && b.Trim() != "")
            .Select(b => b!)
            .Distinct().OrderBy(b => b)
            .ToListAsync(cancellationToken);
        return new BusinessUnitsDto(items);
    }
}
