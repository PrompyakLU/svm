using SvmApi.Domain.Common;

namespace SvmApi.Domain.Assets;

public enum AssetType
{
    Application,
    Server,
    Database,
    NetworkDevice,
    CloudResource,
    Container,
}

public enum AssetCriticality
{
    Critical,
    High,
    Medium,
    Low,
}

public enum AssetEnvironment
{
    Prod,
    Staging,
    Dev,
}

public enum AssetStatus
{
    Active,
    Decommissioned,
}

/// <summary>master_asset — ทรัพย์สินภายใต้การบริหารช่องโหว่. FR-01, BR-05/38, EX-28.</summary>
public class Asset : AuditableEntity
{
    public string AssetCode { get; set; } = default!;
    public string Name { get; set; } = default!;
    public AssetType AssetType { get; set; }
    public string? IpAddress { get; set; }
    public string? Hostname { get; set; }
    public string? Url { get; set; }
    public AssetEnvironment Environment { get; set; }
    public string? BusinessUnit { get; set; }
    public AssetCriticality? Criticality { get; set; }
    public bool InternetExposure { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.Active;
    public string? CloudResourceId { get; set; }
    public string? RepositoryId { get; set; }

    public ICollection<AssetOwnership> Ownerships { get; set; } = new List<AssetOwnership>();
}
