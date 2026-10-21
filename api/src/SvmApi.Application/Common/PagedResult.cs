namespace SvmApi.Application.Common;

/// <summary>ผลลัพธ์แบบ offset pagination (D3-7).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total)
{
    // Keep Total for existing callers while exposing the documented HTTP pagination fields.
    public long TotalCount => Total;
    public long TotalPages => PageSize > 0 ? Total / PageSize + (Total % PageSize == 0 ? 0 : 1) : 0;
}
