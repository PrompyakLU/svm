using SvmApi.Domain.Assets;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Domain.Sla;

/// <summary>config_slaPolicy — นิยาม SLA ตาม risk level/criticality/exposure/KEV. FR-66, BR-08, US-024.</summary>
public class SlaPolicy
{
    public Guid Id { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public AssetCriticality? AssetCriticality { get; set; }
    public bool? InternetExposure { get; set; }
    public bool KevOnly { get; set; }

    public int AcknowledgeHours { get; set; }
    public int StartHours { get; set; }
    public int ResolveHours { get; set; }

    public bool IsActive { get; set; } = true;
}
