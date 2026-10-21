namespace SvmApi.Domain.Risk;

public enum RiskFactor
{
    Cvss,
    AssetCriticality,
    BusinessImpact,
    InternetExposure,
    ExploitAvailability,
    Kev,
    ThreatIntel,
    VulnAge,
    ExistingControl,
}

/// <summary>config_riskWeight — น้ำหนักปัจจัย risk scoring (configurable). FR-16, NFR-31, EX-54, BR-40.</summary>
public class RiskWeight
{
    public Guid Id { get; set; }
    public RiskFactor Factor { get; set; }
    public decimal Weight { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
