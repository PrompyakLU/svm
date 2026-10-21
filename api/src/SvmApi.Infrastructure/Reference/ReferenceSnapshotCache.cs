using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SvmApi.Application.Reference;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Reference;

/// <summary>
/// cache ของ <see cref="ReferenceSnapshot"/> (singleton). Reference data เป็น seed ที่แทบไม่เปลี่ยน จึงโหลดทั้งชุด
/// แล้วถือไว้ตาม TTL เพื่อให้ IsKev/Enrich เป็น in-memory lookup. ถ้าโหลดใหม่ล้มเหลวและมี snapshot เก่า
/// จะใช้ของเก่าต่อ (ไม่ทำให้ intake ล้ม); ถ้ายังไม่เคยโหลดสำเร็จจะ throw ตามปกติ.
/// </summary>
public sealed class ReferenceSnapshotCache(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Entry? _entry;

    public async ValueTask<ReferenceSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var entry = _entry;
        if (entry is not null && timeProvider.GetUtcNow() < entry.ExpiresAt)
        {
            return entry.Snapshot;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            entry = _entry;
            if (entry is not null && timeProvider.GetUtcNow() < entry.ExpiresAt)
            {
                return entry.Snapshot;
            }

            try
            {
                var snapshot = await LoadAsync(cancellationToken);
                _entry = new Entry(snapshot, timeProvider.GetUtcNow() + DefaultTtl);
                return snapshot;
            }
            catch (Exception) when (entry is not null && !cancellationToken.IsCancellationRequested)
            {
                // stale-while-error: ลองใหม่ใน 30 วินาที
                _entry = entry with { ExpiresAt = timeProvider.GetUtcNow() + TimeSpan.FromSeconds(30) };
                return entry.Snapshot;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>ล้าง cache (เช่น หลัง seed ใหม่ / ใช้ในการทดสอบ).</summary>
    public void Invalidate() => _entry = null;

    private async Task<ReferenceSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SvmDbContext>();

        var cves = await db.Cves.AsNoTracking()
            .Select(c => new CveDto(c.CveId, c.Description, c.PublishedDate, c.IsKev, c.KevDateAdded))
            .ToListAsync(cancellationToken);

        var cwes = await db.Cwes.AsNoTracking()
            .Select(c => new CweDto(c.CweId, c.Name, c.Description))
            .ToListAsync(cancellationToken);

        var versions = await db.CvssVersions.AsNoTracking()
            .ToDictionaryAsync(v => v.Version, v => v.IsSupported, cancellationToken);

        return new ReferenceSnapshot(cves, cwes, versions);
    }

    private sealed record Entry(ReferenceSnapshot Snapshot, DateTimeOffset ExpiresAt);
}
