using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SvmApi.Application.Common;
using SvmApi.Application.Reference;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Reference;

/// <summary>
/// Reference data service (C5 · US-013/014). Enrich/IsKev/detail ใช้ snapshot ใน memory;
/// list/search อ่านจาก DB แบบ paged (AsNoTracking) — read-only ทั้งหมด.
/// </summary>
public sealed class ReferenceDataService(
    SvmDbContext db,
    ReferenceSnapshotCache cache,
    ILogger<ReferenceDataService> logger) : IReferenceDataService
{
    public const int MaxPageSize = 100;

    public async Task<ReferenceEnrichment> EnrichAsync(
        ReferenceEnrichmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var snapshot = await cache.GetAsync(cancellationToken);
        var result = ReferenceEnricher.Enrich(snapshot, request);

        if (result.NeedsReview)
        {
            // operations.md (C5): Warn พร้อม cveId/cvssVersion; requestId มาจาก log scope ของ RequestIdMiddleware.
            // ค่ามาจาก scanner (ไม่น่าเชื่อถือ) — log เฉพาะค่าที่ผ่านรูปแบบ เพื่อกัน log injection.
            logger.LogWarning(
                "reference.enrich.needs_review cveId={CveId} cvssVersion={CvssVersion} issues={Issues}",
                ReferenceIds.IsValidCveId(result.CveId) ? result.CveId : "<invalid>",
                SafeVersion(result.CvssVersion),
                string.Join(',', result.Issues));
        }

        return result;
    }

    private static string? SafeVersion(string? version)
        => version is null
            ? null
            : version.Length <= ReferenceIds.MaxCvssVersionLength && version.All(c => char.IsAsciiDigit(c) || c == '.')
                ? version
                : "<invalid>";

    public async Task<bool> IsKevAsync(string? cveId, CancellationToken cancellationToken)
    {
        var snapshot = await cache.GetAsync(cancellationToken);
        return ReferenceEnricher.IsKev(snapshot, cveId);
    }

    public async Task<CveDto?> GetCveAsync(string cveId, CancellationToken cancellationToken)
    {
        var snapshot = await cache.GetAsync(cancellationToken);
        return snapshot.Cves.TryGetValue(cveId.Trim(), out var cve) ? cve : null;
    }

    public async Task<CweDto?> GetCweAsync(string cweId, CancellationToken cancellationToken)
    {
        var snapshot = await cache.GetAsync(cancellationToken);
        return snapshot.Cwes.TryGetValue(cweId.Trim(), out var cwe) ? cwe : null;
    }

    public async Task<PagedResult<CveDto>> SearchCvesAsync(CveQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Clamp(query.Page, query.PageSize);

        var q = db.Cves.AsNoTracking();

        if (query.IsKev is { } isKev)
        {
            q = q.Where(c => c.IsKev == isKev);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(c => c.CveId.Contains(term) || (c.Description != null && c.Description.Contains(term)));
        }

        var total = await q.LongCountAsync(cancellationToken);
        var items = await q
            .OrderBy(c => c.CveId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CveDto(c.CveId, c.Description, c.PublishedDate, c.IsKev, c.KevDateAdded))
            .ToListAsync(cancellationToken);

        return new PagedResult<CveDto>(items, page, pageSize, total);
    }

    public async Task<PagedResult<CweDto>> SearchCwesAsync(CweQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Clamp(query.Page, query.PageSize);

        var q = db.Cwes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(c => c.CweId.Contains(term) || c.Name.Contains(term));
        }

        var total = await q.LongCountAsync(cancellationToken);
        var items = await q
            .OrderBy(c => c.CweId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CweDto(c.CweId, c.Name, c.Description))
            .ToListAsync(cancellationToken);

        return new PagedResult<CweDto>(items, page, pageSize, total);
    }

    private static (int Page, int PageSize) Clamp(int page, int pageSize)
        => (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));
}
