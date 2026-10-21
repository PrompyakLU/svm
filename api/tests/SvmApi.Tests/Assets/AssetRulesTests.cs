using SvmApi.Application.Assets;
using SvmApi.Domain.Assets;

namespace SvmApi.Tests.Assets;

/// <summary>ทดสอบกติกา AssetModule แบบ pure (US-004/006 · FR-01/03/04 · BR-05/EX-28) — ไม่ใช้ DB.</summary>
public class AssetRulesTests
{
    private static CreateAssetRequest ValidCreate(
        string? assetCode = "SVM-APP-900",
        string? name = "Test App",
        string? assetType = "Application",
        string? environment = "Prod",
        bool? internetExposure = true,
        string? ipAddress = null,
        string? hostname = null,
        string? url = null,
        IReadOnlyList<AssetOwnerInput>? owners = null)
        => new(assetCode, name, assetType, ipAddress, hostname, url, environment, "Digital Channels",
            internetExposure, null, null, owners);

    // --- create ---

    [Fact]
    public void ValidateCreate_Valid_NormalizesValues()
    {
        var r = AssetRules.ValidateCreate(ValidCreate(assetCode: "  SVM-APP-900 ", name: " Test App ", assetType: "application", environment: "prod"));

        Assert.True(r.IsValid);
        Assert.Equal("SVM-APP-900", r.Value!.AssetCode);
        Assert.Equal("Test App", r.Value.Fields.Name);
        Assert.Equal(AssetType.Application, r.Value.Fields.AssetType);
        Assert.Equal(AssetEnvironment.Prod, r.Value.Fields.Environment);
        Assert.Empty(r.Value.Owners);
    }

    [Fact]
    public void ValidateCreate_NullBody_Invalid()
    {
        var r = AssetRules.ValidateCreate(null);

        Assert.False(r.IsValid);
        Assert.Contains("body", r.Errors.Keys);
    }

    [Fact]
    public void ValidateCreate_MissingMandatory_ReportsEachField()
    {
        var r = AssetRules.ValidateCreate(ValidCreate(assetCode: null, name: " ", assetType: null, environment: null, internetExposure: null));

        Assert.False(r.IsValid);
        Assert.Contains("assetCode", r.Errors.Keys);
        Assert.Contains("name", r.Errors.Keys);
        Assert.Contains("assetType", r.Errors.Keys);
        Assert.Contains("environment", r.Errors.Keys);
        Assert.Contains("internetExposure", r.Errors.Keys);
    }

    [Theory]
    [InlineData("SVM APP")]          // space
    [InlineData("-SVM")]             // ต้องขึ้นต้นด้วยตัวอักษร/ตัวเลข
    [InlineData("SVM;DROP")]
    [InlineData("<script>")]
    public void ValidateCreate_BadAssetCode_Invalid(string code)
    {
        var r = AssetRules.ValidateCreate(ValidCreate(assetCode: code));

        Assert.Contains("assetCode", r.Errors.Keys);
    }

    [Theory]
    [InlineData("1")]                // ตัวเลขห้ามใช้แทนชื่อ enum
    [InlineData("Mainframe")]
    public void ValidateCreate_UnknownAssetType_Invalid(string type)
    {
        var r = AssetRules.ValidateCreate(ValidCreate(assetType: type));

        Assert.Contains("assetType", r.Errors.Keys);
    }

    [Theory]
    [InlineData("10.0.0", "ipAddress")]
    [InlineData("999.1.1.1", "ipAddress")]
    [InlineData("bad host!", "hostname")]
    [InlineData("ftp://x.example", "url")]
    [InlineData("javascript:alert(1)", "url")]
    public void ValidateCreate_BadNetworkFields_Invalid(string value, string field)
    {
        var request = field switch
        {
            "ipAddress" => ValidCreate(ipAddress: value),
            "hostname" => ValidCreate(hostname: value),
            _ => ValidCreate(url: value),
        };

        Assert.Contains(field, AssetRules.ValidateCreate(request).Errors.Keys);
    }

    [Fact]
    public void ValidateCreate_ValidNetworkFields_Accepted()
    {
        var r = AssetRules.ValidateCreate(ValidCreate(ipAddress: "10.0.0.1", hostname: "app01.internal", url: "https://app.example"));

        Assert.True(r.IsValid);
    }

    // --- ownership (FR-03) ---

    [Fact]
    public void ValidateCreate_Owners_UserOwnerNeedsUserId_TeamNeedsName()
    {
        var r = AssetRules.ValidateCreate(ValidCreate(owners:
        [
            new AssetOwnerInput("AssetOwner", null, null),
            new AssetOwnerInput("RemediationTeam", null, null),
        ]));

        Assert.Contains("owners[0].ownerUserId", r.Errors.Keys);
        Assert.Contains("owners[1].teamName", r.Errors.Keys);
    }

    [Fact]
    public void ValidateCreate_Owners_DuplicateRejected()
    {
        var id = Guid.NewGuid();
        var r = AssetRules.ValidateCreate(ValidCreate(owners:
        [
            new AssetOwnerInput("SystemOwner", id, null),
            new AssetOwnerInput("systemowner", id, null),
        ]));

        Assert.Contains("owners[1]", r.Errors.Keys);
    }

    [Fact]
    public void ValidateCreate_Owners_MultipleTypesAccepted()
    {
        var r = AssetRules.ValidateCreate(ValidCreate(owners:
        [
            new AssetOwnerInput("AssetOwner", Guid.NewGuid(), null),
            new AssetOwnerInput("ApplicationOwner", Guid.NewGuid(), null),
            new AssetOwnerInput("RemediationTeam", null, "Infra Ops"),
        ]));

        Assert.True(r.IsValid);
        Assert.Equal(3, r.Value!.Owners.Count);
    }

    [Fact]
    public void ValidateCreate_TooManyOwners_Invalid()
    {
        var owners = Enumerable.Range(0, AssetRules.MaxOwners + 1)
            .Select(_ => new AssetOwnerInput("AssetOwner", Guid.NewGuid(), null))
            .ToList();

        Assert.Contains("owners", AssetRules.ValidateCreate(ValidCreate(owners: owners)).Errors.Keys);
    }

    // --- update ---

    [Fact]
    public void ValidateUpdate_NullOwnersKeepsExisting_StatusParsed()
    {
        var r = AssetRules.ValidateUpdate(new UpdateAssetRequest(
            "Name", "Server", null, null, null, "Dev", null, false, null, null, "decommissioned", null));

        Assert.True(r.IsValid);
        Assert.Null(r.Value!.Owners);
        Assert.Equal(AssetStatus.Decommissioned, r.Value.Status);
    }

    [Fact]
    public void ValidateUpdate_BadStatus_Invalid()
    {
        var r = AssetRules.ValidateUpdate(new UpdateAssetRequest(
            "Name", "Server", null, null, null, "Dev", null, false, null, null, "Deleted", null));

        Assert.Contains("status", r.Errors.Keys);
    }

    // --- criticality (FR-04) ---

    [Theory]
    [InlineData("Critical", AssetCriticality.Critical)]
    [InlineData("high", AssetCriticality.High)]
    [InlineData("Medium", AssetCriticality.Medium)]
    [InlineData("LOW", AssetCriticality.Low)]
    public void ValidateCriticality_SupportedLevels(string value, AssetCriticality expected)
    {
        var r = AssetRules.ValidateCriticality(new CriticalityRequest(value, "business impact review"), null);

        Assert.True(r.IsValid);
        Assert.Equal(expected, r.Value!.New);
        Assert.True(r.Value.Changed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateCriticality_ReasonRequired(string? reason)
    {
        var r = AssetRules.ValidateCriticality(new CriticalityRequest("High", reason), null);

        Assert.Contains("reason", r.Errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Unknown")]
    [InlineData("0")]
    public void ValidateCriticality_InvalidOrMissingLevel(string? value)
    {
        var r = AssetRules.ValidateCriticality(new CriticalityRequest(value, "reason"), null);

        Assert.Contains("criticality", r.Errors.Keys);
    }

    [Fact]
    public void ValidateCriticality_ReasonTooLongOrControlChars_Invalid()
    {
        Assert.Contains("reason", AssetRules.ValidateCriticality(
            new CriticalityRequest("High", new string('x', AssetRules.MaxReasonLength + 1)), null).Errors.Keys);
        Assert.Contains("reason", AssetRules.ValidateCriticality(
            new CriticalityRequest("High", "bad\u0000reason"), null).Errors.Keys);
    }

    [Fact]
    public void ValidateCriticality_SameValue_IsConfirmation()
    {
        var r = AssetRules.ValidateCriticality(new CriticalityRequest("High", "confirmed"), AssetCriticality.High);

        Assert.True(r.IsValid);
        Assert.False(r.Value!.Changed);
        Assert.Equal(AssetCriticality.High, r.Value.Previous);
    }

    // --- query ---

    [Theory]
    [InlineData(0, 25, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, AssetRules.MaxPageSize + 1, "pageSize")]
    public void ValidateQuery_BadPaging_Invalid(int page, int pageSize, string field)
    {
        var r = AssetRules.ValidateQuery(new AssetQuery(null, null, null, null, null, null, null, null, page, pageSize));

        Assert.Contains(field, r.Errors.Keys);
    }

    [Fact]
    public void ValidateQuery_CriticalityMissingWithCriticality_Conflicts()
    {
        var r = AssetRules.ValidateQuery(new AssetQuery(null, null, null, null, "High", null, null, true));

        Assert.Contains("criticalityMissing", r.Errors.Keys);
    }

    [Fact]
    public void ValidateQuery_NullQuery_Defaults()
    {
        var r = AssetRules.ValidateQuery(null);

        Assert.True(r.IsValid);
        Assert.Equal(1, r.Value!.Page);
        Assert.Equal(25, r.Value.PageSize);
    }

    // --- BR-05 / EX-28 ---

    [Theory]
    [InlineData(AssetEnvironment.Prod, AssetStatus.Active, null, true)]
    [InlineData(AssetEnvironment.Prod, AssetStatus.Active, AssetCriticality.Low, false)]
    [InlineData(AssetEnvironment.Staging, AssetStatus.Active, null, false)]
    [InlineData(AssetEnvironment.Prod, AssetStatus.Decommissioned, null, false)]
    public void IsCriticalityMissing_OnlyActiveProdWithoutCriticality(
        AssetEnvironment env, AssetStatus status, AssetCriticality? criticality, bool expected)
        => Assert.Equal(expected, AssetRules.IsCriticalityMissing(env, status, criticality));

    [Fact]
    public void GetWarnings_ProdWithoutCriticalityAndOwner_ReturnsBoth()
    {
        var w = AssetRules.GetWarnings(AssetEnvironment.Prod, AssetStatus.Active, null, 0);

        Assert.Equal([AssetWarnings.CriticalityMissing, AssetWarnings.NoOwner], w);
    }

    [Fact]
    public void GetWarnings_CompleteAsset_None()
        => Assert.Empty(AssetRules.GetWarnings(AssetEnvironment.Prod, AssetStatus.Active, AssetCriticality.High, 1));
}
