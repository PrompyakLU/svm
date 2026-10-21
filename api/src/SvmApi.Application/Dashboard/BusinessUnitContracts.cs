namespace SvmApi.Application.Dashboard;

public sealed record BusinessUnitsDto(IReadOnlyList<string> Items);

public interface IBusinessUnitQueryService
{
    Task<BusinessUnitsDto> GetAsync(CancellationToken cancellationToken);
}
