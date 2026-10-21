namespace SvmApi.Domain.Assets;

public enum OwnerType
{
    AssetOwner,
    SystemOwner,
    ApplicationOwner,
    RemediationTeam,
}

/// <summary>master_assetOwnership — เชื่อม asset &lt;-&gt; owner หลายบทบาท. FR-03, BR-06, EX-25.</summary>
public class AssetOwnership
{
    public Guid Id { get; set; }

    public Guid AssetId { get; set; }
    public Asset Asset { get; set; } = default!;

    public Guid? OwnerUserId { get; set; }
    public OwnerType OwnerType { get; set; }
    public string? TeamName { get; set; }
}
