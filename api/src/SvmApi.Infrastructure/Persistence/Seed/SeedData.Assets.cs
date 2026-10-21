using SvmApi.Domain.Assets;

namespace SvmApi.Infrastructure.Persistence.Seed;

/// <summary>ชื่อ business unit ของข้อมูลตัวอย่าง (สมมติ) — ใช้เป็น <c>Asset.BusinessUnit</c> และ scope value.</summary>
internal static class SeedBusinessUnits
{
    public const string ItInfrastructure = "IT Infrastructure";
    public const string DigitalChannels = "Digital Channels";
    public const string CorporateFinance = "Corporate Finance";
    public const string HumanResources = "Human Resources";
}

/// <summary>ชื่อทีมตัวอย่าง (สมมติ) สำหรับ assignment rule และ remediation team.</summary>
internal static class SeedTeams
{
    public const string InfrastructureOperations = "Infrastructure Operations Team";
    public const string DigitalChannelsDev = "Digital Channels Dev Team";
    public const string FinanceSystems = "Finance Systems Team";
    public const string HrSystems = "HR Systems Team";
    public const string DatabaseAdministration = "Database Administration Team";
    public const string NetworkOperations = "Network Operations Team";
    public const string CloudPlatform = "Cloud Platform Team";
}

internal static partial class SeedData
{
    internal sealed record AssetSeed(
        string AssetCode,
        string Name,
        AssetType AssetType,
        AssetEnvironment Environment,
        string BusinessUnit,
        AssetCriticality? Criticality,
        bool InternetExposure,
        string? IpAddress = null,
        string? Hostname = null,
        string? Url = null,
        string? CloudResourceId = null,
        string? RepositoryId = null);

    internal sealed record AssetOwnershipSeed(
        string AssetCode,
        OwnerType OwnerType,
        string Username,
        string? TeamName);

    /// <summary>
    /// asset ตัวอย่าง 12 รายการ (master_asset) · FR-01, BR-05/38, EX-28:
    /// ครอบคลุมทุก <see cref="AssetType"/>, 4 BU, ทุก criticality, ทั้ง internet-facing และ internal,
    /// Prod/Staging/Dev, และ 2 รายการที่ไม่มี criticality (CLD-001, CTR-001) สำหรับทดสอบ EX-28.
    /// ที่อยู่ IP/โฮสต์เป็นค่าสมมติ (ช่วง private + โดเมน .example) ไม่ใช่ระบบจริง.
    /// </summary>
    internal static IReadOnlyList<AssetSeed> Assets { get; } =
    [
        new("SVM-APP-001", "Customer Portal", AssetType.Application, AssetEnvironment.Prod,
            SeedBusinessUnits.DigitalChannels, AssetCriticality.Critical, true,
            Url: "https://portal.svm.example", RepositoryId: "repo/customer-portal"),
        new("SVM-APP-002", "Mobile Banking API", AssetType.Application, AssetEnvironment.Prod,
            SeedBusinessUnits.DigitalChannels, AssetCriticality.High, true,
            Url: "https://api.svm.example", RepositoryId: "repo/mobile-api"),
        new("SVM-APP-003", "Internal HR Portal", AssetType.Application, AssetEnvironment.Prod,
            SeedBusinessUnits.HumanResources, AssetCriticality.Medium, false,
            Url: "https://hr.internal.svm.example"),

        new("SVM-SRV-001", "Core Web Server 01", AssetType.Server, AssetEnvironment.Prod,
            SeedBusinessUnits.ItInfrastructure, AssetCriticality.High, true,
            IpAddress: "10.10.1.11", Hostname: "web01.svm.example"),
        new("SVM-SRV-002", "File Server 01", AssetType.Server, AssetEnvironment.Prod,
            SeedBusinessUnits.ItInfrastructure, AssetCriticality.Medium, false,
            IpAddress: "10.10.1.21", Hostname: "file01.internal.svm.example"),
        new("SVM-SRV-003", "Staging App Server", AssetType.Server, AssetEnvironment.Staging,
            SeedBusinessUnits.DigitalChannels, AssetCriticality.Low, false,
            IpAddress: "10.20.1.31", Hostname: "stg-app01.internal.svm.example"),

        new("SVM-DB-001", "Finance Database", AssetType.Database, AssetEnvironment.Prod,
            SeedBusinessUnits.CorporateFinance, AssetCriticality.Critical, false,
            IpAddress: "10.10.3.11", Hostname: "fin-db01.internal.svm.example"),
        new("SVM-DB-002", "HR Database", AssetType.Database, AssetEnvironment.Prod,
            SeedBusinessUnits.HumanResources, AssetCriticality.High, false,
            IpAddress: "10.10.3.12", Hostname: "hr-db01.internal.svm.example"),

        new("SVM-NET-001", "Perimeter Firewall", AssetType.NetworkDevice, AssetEnvironment.Prod,
            SeedBusinessUnits.ItInfrastructure, AssetCriticality.Critical, true,
            IpAddress: "10.10.0.1", Hostname: "fw01.svm.example"),
        new("SVM-NET-002", "Lab Core Switch", AssetType.NetworkDevice, AssetEnvironment.Dev,
            SeedBusinessUnits.ItInfrastructure, AssetCriticality.Low, false,
            IpAddress: "10.99.0.2", Hostname: "lab-sw01.internal.svm.example"),

        // ไม่มี criticality — ทดสอบ EX-28
        new("SVM-CLD-001", "Cloud Object Storage (Reports)", AssetType.CloudResource, AssetEnvironment.Prod,
            SeedBusinessUnits.CorporateFinance, null, true,
            CloudResourceId: "cloud-res-reports-001"),
        new("SVM-CTR-001", "Payment Service Container", AssetType.Container, AssetEnvironment.Staging,
            SeedBusinessUnits.DigitalChannels, null, false,
            RepositoryId: "registry.svm.example/payments/payment-service"),
    ];

    /// <summary>
    /// เจ้าของ asset (master_assetOwnership) ผูกกับ demo users · FR-03, BR-06, EX-25.
    /// ข้ามทั้งหมดถ้าไม่มี user ตาม username (เช่น ไม่ได้ตั้ง <c>Seed:DemoPassword</c>).
    /// </summary>
    internal static IReadOnlyList<AssetOwnershipSeed> AssetOwnerships { get; } =
    [
        new("SVM-APP-001", OwnerType.AssetOwner, "assetowner1", null),
        new("SVM-APP-001", OwnerType.RemediationTeam, "remediation2", SeedTeams.DigitalChannelsDev),
        new("SVM-APP-002", OwnerType.AssetOwner, "assetowner1", null),
        new("SVM-APP-002", OwnerType.RemediationTeam, "remediation2", SeedTeams.DigitalChannelsDev),
        new("SVM-APP-003", OwnerType.AssetOwner, "assetowner2", null),
        new("SVM-APP-003", OwnerType.RemediationTeam, "remediation1", SeedTeams.HrSystems),

        new("SVM-SRV-001", OwnerType.SystemOwner, "assetowner1", null),
        new("SVM-SRV-001", OwnerType.RemediationTeam, "remediation1", SeedTeams.InfrastructureOperations),
        new("SVM-SRV-002", OwnerType.SystemOwner, "assetowner1", null),
        new("SVM-SRV-002", OwnerType.RemediationTeam, "remediation1", SeedTeams.InfrastructureOperations),
        new("SVM-SRV-003", OwnerType.AssetOwner, "assetowner1", null),
        new("SVM-SRV-003", OwnerType.RemediationTeam, "remediation2", SeedTeams.DigitalChannelsDev),

        new("SVM-DB-001", OwnerType.AssetOwner, "assetowner2", null),
        new("SVM-DB-001", OwnerType.RemediationTeam, "remediation1", SeedTeams.DatabaseAdministration),
        new("SVM-DB-002", OwnerType.AssetOwner, "assetowner2", null),
        new("SVM-DB-002", OwnerType.RemediationTeam, "remediation1", SeedTeams.DatabaseAdministration),

        new("SVM-NET-001", OwnerType.SystemOwner, "assetowner1", null),
        new("SVM-NET-001", OwnerType.RemediationTeam, "remediation1", SeedTeams.NetworkOperations),
        new("SVM-NET-002", OwnerType.SystemOwner, "assetowner1", null),
        new("SVM-NET-002", OwnerType.RemediationTeam, "remediation1", SeedTeams.NetworkOperations),

        new("SVM-CLD-001", OwnerType.AssetOwner, "assetowner2", null),
        new("SVM-CLD-001", OwnerType.RemediationTeam, "remediation1", SeedTeams.CloudPlatform),
        new("SVM-CTR-001", OwnerType.ApplicationOwner, "assetowner1", null),
        new("SVM-CTR-001", OwnerType.RemediationTeam, "remediation2", SeedTeams.DigitalChannelsDev),
    ];
}
