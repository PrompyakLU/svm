using SvmApi.Application.Assets;
using SvmApi.Domain.Assets;

namespace SvmApi.Tests.Assets;

/// <summary>ทดสอบตรรกะ CMDB sync แบบ pure (US-005 · FR-02 · EX-30/31 · BR-38) — ไม่ใช้ DB.</summary>
public class AssetSyncPlannerTests
{
    private static CmdbAssetRecord Record(
        string? externalId = "SVM-SRV-900",
        string? name = "Server 900",
        string? assetType = "Server",
        string? environment = "Prod",
        string? criticality = null,
        string? status = null,
        string? ip = "10.0.0.9")
        => new(externalId, name, assetType, environment, "IT Infrastructure", criticality, false,
            IpAddress: ip, Status: status);

    private static Asset Existing(AssetCriticality? criticality = null) => new()
    {
        Id = Guid.NewGuid(),
        AssetCode = "SVM-SRV-900",
        Name = "Server 900",
        AssetType = AssetType.Server,
        Environment = AssetEnvironment.Prod,
        BusinessUnit = "IT Infrastructure",
        IpAddress = "10.0.0.9",
        InternetExposure = false,
        Status = AssetStatus.Active,
        Criticality = criticality,
    };

    [Fact]
    public void Decide_NoExisting_Create()
    {
        var d = AssetSyncPlanner.Decide(null, Record(criticality: "High"));

        Assert.Equal(SyncAction.Create, d.Action);
        Assert.Equal("SVM-SRV-900", d.ExternalId);
        Assert.Equal(AssetCriticality.High, d.Mapped!.Criticality);
        Assert.Equal(OwnerType.AssetOwner, d.Mapped.OwnerType);
    }

    [Fact]
    public void Decide_SameData_Unchanged_Idempotent()
    {
        var d = AssetSyncPlanner.Decide(Existing(), Record());

        Assert.Equal(SyncAction.Unchanged, d.Action);
        Assert.Empty(d.ChangedFields);
    }

    [Fact]
    public void Decide_ChangedFields_UpdateListsThem()
    {
        var d = AssetSyncPlanner.Decide(Existing(), Record(name: "Server 900 renamed", ip: "10.0.0.10"));

        Assert.Equal(SyncAction.Update, d.Action);
        Assert.Equal(["name", "ipAddress"], d.ChangedFields);
    }

    [Theory]
    [InlineData(null, "Server", "Prod")]        // externalId ว่าง
    [InlineData("SVM-X", "Mainframe", "Prod")]  // assetType ไม่รู้จัก
    [InlineData("SVM-X", "Server", "QA")]       // environment ไม่รู้จัก
    [InlineData("bad id", "Server", "Prod")]    // assetCode ผิดรูปแบบ
    public void Decide_Unmappable_Unmapped_WithProblems(string? id, string type, string env)
    {
        var d = AssetSyncPlanner.Decide(null, Record(externalId: id, assetType: type, environment: env));

        Assert.Equal(SyncAction.Unmapped, d.Action);
        Assert.Null(d.Mapped);
        Assert.NotEmpty(d.Problems);
    }

    [Fact]
    public void Decide_MissingExternalId_UsesPlaceholder()
        => Assert.Equal(AssetSyncPlanner.UnknownExternalId, AssetSyncPlanner.Decide(null, Record(externalId: "  ")).ExternalId);

    [Fact]
    public void Criticality_FilledOnlyWhenEmpty()
    {
        var filled = AssetSyncPlanner.Decide(Existing(criticality: null), Record(criticality: "Low"));
        Assert.Equal(SyncAction.Update, filled.Action);
        Assert.Contains("criticality", filled.ChangedFields);

        // FR-04: ค่าที่ผู้รับผิดชอบกำหนดแล้วต้องไม่ถูก CMDB เขียนทับ
        var kept = AssetSyncPlanner.Decide(Existing(criticality: AssetCriticality.Critical), Record(criticality: "Low"));
        Assert.Equal(SyncAction.Unchanged, kept.Action);

        var asset = Existing(criticality: AssetCriticality.Critical);
        AssetSyncPlanner.ApplyUpdate(asset, kept.Mapped!);
        Assert.Equal(AssetCriticality.Critical, asset.Criticality);
    }

    [Fact]
    public void Status_NullInFeed_KeepsExisting()
    {
        var asset = Existing();
        asset.Status = AssetStatus.Decommissioned;

        var d = AssetSyncPlanner.Decide(asset, Record(status: null));
        AssetSyncPlanner.ApplyUpdate(asset, d.Mapped!);

        Assert.Equal(AssetStatus.Decommissioned, asset.Status);
        Assert.DoesNotContain("status", d.ChangedFields);
    }

    [Fact]
    public void ApplyUpdate_DoesNotTouchIdentityOrOwnership()
    {
        var asset = Existing();
        var id = asset.Id;
        asset.Ownerships.Add(new AssetOwnership { Id = Guid.NewGuid(), AssetId = id, OwnerType = OwnerType.AssetOwner });

        var d = AssetSyncPlanner.Decide(asset, Record(name: "New Name"));
        AssetSyncPlanner.ApplyUpdate(asset, d.Mapped!);

        Assert.Equal(id, asset.Id);
        Assert.Equal("SVM-SRV-900", asset.AssetCode);
        Assert.Single(asset.Ownerships);
        Assert.Equal("New Name", asset.Name);
    }

    [Fact]
    public void NewAsset_DefaultsActive()
    {
        var d = AssetSyncPlanner.Decide(null, Record());
        var asset = AssetSyncPlanner.NewAsset(d.Mapped!);

        Assert.Equal(AssetStatus.Active, asset.Status);
        Assert.Equal("SVM-SRV-900", asset.AssetCode);
    }

    // --- EX-31 / BR-38 ---

    [Fact]
    public void FindMissing_ReturnsActiveCodesNotInFeed_CaseInsensitive_Sorted()
    {
        var missing = AssetSyncPlanner.FindMissing(
            ["SVM-B", "SVM-A", "SVM-C"],
            ["svm-a", null, "  ", "SVM-X"]);

        Assert.Equal(["SVM-B", "SVM-C"], missing);
    }

    [Fact]
    public void FindMissing_UnmappableButPresentId_CountsAsPresent()
    {
        var feed = new[] { Record(externalId: "SVM-A", assetType: "Mainframe") };

        Assert.Empty(AssetSyncPlanner.FindMissing(["SVM-A"], feed.Select(r => r.ExternalId)));
    }
}
