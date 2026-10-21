using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SvmApi.Application.Assets;
using SvmApi.Domain.Assets;
using SvmApi.Infrastructure.Integration.Mock;

namespace SvmApi.Tests.Assets;

/// <summary>
/// ทดสอบ CMDB mock adapter (US-005 · FR-02 · EX-28/30 · integration.md): deterministic, ไม่มี IO จริง,
/// และข้อมูล seed ทุกรายการ map ผ่าน <see cref="AssetSyncPlanner"/> ได้ (ยกเว้นรายการสาธิต unmapped ที่ตั้งใจไว้).
/// </summary>
public class MockCmdbAdapterTests
{
    private const string UnmappableDemoId = "SVM-UNK-001";

    private static MockCmdbAdapter Create(bool simulateUnavailable = false, bool seedOnly = false)
        => new(
            Options.Create(new CmdbMockOptions { SimulateUnavailable = simulateUnavailable, SeedOnly = seedOnly }),
            NullLogger<MockCmdbAdapter>.Instance);

    [Fact]
    public async Task GetAssets_IsDeterministic_AndExternalIdsUnique()
    {
        var first = await Create().GetAssetsAsync(CancellationToken.None);
        var second = await Create().GetAssetsAsync(CancellationToken.None);

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
        Assert.Equal(first.Count, first.Select(r => r.ExternalId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task GetAssets_AllRecordsMap_ExceptIntentionalUnmappedDemo()
    {
        var feed = await Create().GetAssetsAsync(CancellationToken.None);

        var unmapped = feed
            .Select(r => AssetSyncPlanner.Decide(null, r))
            .Where(d => d.Action == SyncAction.Unmapped)
            .Select(d => d.ExternalId)
            .ToList();

        Assert.Equal(new[] { UnmappableDemoId }, unmapped);
    }

    [Fact]
    public async Task GetAssets_IncludesProdRecordWithoutCriticality_ForEx28Demo()
    {
        var feed = await Create().GetAssetsAsync(CancellationToken.None);

        var missing = feed
            .Select(r => AssetSyncPlanner.Decide(null, r).Mapped)
            .Where(m => m is not null)
            .Where(m => AssetRules.IsCriticalityMissing(m!.Fields.Environment, m.Status ?? AssetStatus.Active, m.Criticality))
            .ToList();

        Assert.NotEmpty(missing);
    }

    [Fact]
    public async Task SeedOnly_ExcludesDemoRecords_AndAllMap()
    {
        var feed = await Create(seedOnly: true).GetAssetsAsync(CancellationToken.None);

        Assert.DoesNotContain(feed, r => r.ExternalId == UnmappableDemoId);
        Assert.All(feed, r => Assert.NotEqual(SyncAction.Unmapped, AssetSyncPlanner.Decide(null, r).Action));
    }

    [Fact]
    public async Task SimulateUnavailable_ThrowsCmdbUnavailable()
    {
        var adapter = Create(simulateUnavailable: true);

        await Assert.ThrowsAsync<CmdbUnavailableException>(() => adapter.GetAssetsAsync(CancellationToken.None));
        await Assert.ThrowsAsync<CmdbUnavailableException>(() => adapter.GetAssetByIdAsync("SVM-APP-001", CancellationToken.None));
    }

    [Fact]
    public async Task GetAssetById_CaseInsensitive_UnknownReturnsNull()
    {
        var adapter = Create();

        var found = await adapter.GetAssetByIdAsync("svm-app-001", CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal("SVM-APP-001", found!.ExternalId);

        Assert.Null(await adapter.GetAssetByIdAsync("SVM-NOPE-999", CancellationToken.None));
    }

    [Fact]
    public async Task UnavailableAdapter_AlwaysThrows_NoNetwork()
    {
        var adapter = new UnavailableCmdbAdapter(NullLogger<UnavailableCmdbAdapter>.Instance);

        await Assert.ThrowsAsync<CmdbUnavailableException>(() => adapter.GetAssetsAsync(CancellationToken.None));
        await Assert.ThrowsAsync<CmdbUnavailableException>(() => adapter.GetAssetByIdAsync("SVM-APP-001", CancellationToken.None));
    }
}
