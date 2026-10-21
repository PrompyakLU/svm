using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SvmApi.Application.Assets;
using SvmApi.Domain.Assets;
using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Infrastructure.Integration.Mock;

/// <summary>
/// Config ของ CMDB mock (section <c>Integration:CmdbMock</c>). ใช้จำลอง EX-30 (CMDB ส่งข้อมูลไม่ได้) ตอนทดสอบ.
/// </summary>
public sealed class CmdbMockOptions
{
    public const string SectionName = "Integration:CmdbMock";

    /// <summary>true = <see cref="MockCmdbAdapter"/> throw <see cref="CmdbUnavailableException"/> (จำลอง CMDB ล่ม).</summary>
    public bool SimulateUnavailable { get; set; }

    /// <summary>true = คืน asset จาก seed อย่างเดียว (ไม่รวมรายการ CMDB-only และรายการ map ไม่ได้ สำหรับสาธิต).</summary>
    public bool SeedOnly { get; set; }
}

/// <summary>
/// ICmdbAdapter แบบ mock (workshop: ห้ามเรียกระบบจริง · integration.md). อ่านข้อมูลจาก seed ในหน่วยความจำ
/// (<see cref="SeedData.Assets"/> / <see cref="SeedData.AssetOwnerships"/>) — deterministic ทุกครั้ง.
/// นอกจาก asset ที่ตรงกับ seed ใน DB (-> Unchanged) ยังมีรายการสาธิต 3 รายการ:
/// 2 รายการใหม่ (-> Created) และ 1 รายการ map ไม่ได้ (assetType ไม่รู้จัก -> Unmapped).
/// </summary>
public sealed class MockCmdbAdapter(IOptions<CmdbMockOptions> options, ILogger<MockCmdbAdapter> logger) : ICmdbAdapter
{
    private static readonly IReadOnlyList<CmdbAssetRecord> CmdbOnlyRecords =
    [
        new("SVM-SRV-101", "Reporting Server 01", nameof(AssetType.Server), nameof(AssetEnvironment.Prod),
            SeedBusinessUnits.ItInfrastructure, nameof(AssetCriticality.Medium), false,
            IpAddress: "10.10.1.101", Hostname: "rpt01.internal.svm.example",
            OwnerUsername: "assetowner1", OwnerType: nameof(OwnerType.SystemOwner),
            TeamName: SeedTeams.InfrastructureOperations),
        new("SVM-APP-101", "Partner Gateway", nameof(AssetType.Application), nameof(AssetEnvironment.Prod),
            SeedBusinessUnits.DigitalChannels, null, true,
            Url: "https://partner.svm.example", RepositoryId: "repo/partner-gateway",
            OwnerUsername: "assetowner1", OwnerType: nameof(OwnerType.ApplicationOwner),
            TeamName: SeedTeams.DigitalChannelsDev),

        // map ไม่ได้: assetType ไม่อยู่ใน master (EX-30/FR-02 unmapped)
        new("SVM-UNK-001", "Legacy Mainframe", "Mainframe", nameof(AssetEnvironment.Prod),
            SeedBusinessUnits.ItInfrastructure, null, false),
    ];

    public Task<IReadOnlyList<CmdbAssetRecord>> GetAssetsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAvailable();

        var records = BuildSeedRecords().ToList();
        if (!options.Value.SeedOnly)
        {
            records.AddRange(CmdbOnlyRecords);
        }

        logger.LogInformation("cmdb.mock.get_assets count={Count}", records.Count);
        return Task.FromResult<IReadOnlyList<CmdbAssetRecord>>(records);
    }

    public Task<CmdbAssetRecord?> GetAssetByIdAsync(string assetCode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAvailable();

        var records = BuildSeedRecords();
        if (!options.Value.SeedOnly)
        {
            records = records.Concat(CmdbOnlyRecords);
        }

        var found = records.FirstOrDefault(r => string.Equals(r.ExternalId, assetCode, StringComparison.OrdinalIgnoreCase));
        logger.LogInformation("cmdb.mock.get_asset_by_id found={Found}", found is not null);
        return Task.FromResult(found);
    }

    private void EnsureAvailable()
    {
        if (options.Value.SimulateUnavailable)
        {
            logger.LogWarning("cmdb.mock.unavailable simulated=true");
            throw new CmdbUnavailableException("CMDB mock is configured to simulate an outage.");
        }
    }

    private static IEnumerable<CmdbAssetRecord> BuildSeedRecords()
    {
        foreach (var seed in SeedData.Assets)
        {
            var primaryOwner = SeedData.AssetOwnerships.FirstOrDefault(o =>
                o.AssetCode == seed.AssetCode && o.OwnerType != OwnerType.RemediationTeam);
            var team = SeedData.AssetOwnerships.FirstOrDefault(o =>
                o.AssetCode == seed.AssetCode && o.OwnerType == OwnerType.RemediationTeam);

            yield return new CmdbAssetRecord(
                seed.AssetCode,
                seed.Name,
                seed.AssetType.ToString(),
                seed.Environment.ToString(),
                seed.BusinessUnit,
                seed.Criticality?.ToString(),
                seed.InternetExposure,
                seed.IpAddress,
                seed.Hostname,
                seed.Url,
                seed.CloudResourceId,
                seed.RepositoryId,
                nameof(AssetStatus.Active),
                primaryOwner?.Username,
                primaryOwner?.OwnerType.ToString(),
                team?.TeamName);
        }
    }
}

/// <summary>
/// ใช้เมื่อ <c>Integration:UseMock=false</c>: ยังไม่มี real adapter ใน workshop และห้ามเรียกระบบจริง
/// จึงรายงานว่า CMDB ไม่พร้อม (sync จะบันทึก integration error และคงข้อมูลเดิม — EX-30).
/// </summary>
public sealed class UnavailableCmdbAdapter(ILogger<UnavailableCmdbAdapter> logger) : ICmdbAdapter
{
    public Task<IReadOnlyList<CmdbAssetRecord>> GetAssetsAsync(CancellationToken cancellationToken)
        => throw NotConfigured();

    public Task<CmdbAssetRecord?> GetAssetByIdAsync(string assetCode, CancellationToken cancellationToken)
        => throw NotConfigured();

    private CmdbUnavailableException NotConfigured()
    {
        logger.LogWarning("cmdb.real_adapter.not_configured");
        return new CmdbUnavailableException("No real CMDB adapter is configured in this environment.");
    }
}
