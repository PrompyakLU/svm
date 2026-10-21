using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Domain.Risk;

/// <summary>config_riskThreshold — ช่วงคะแนนต่อระดับความเสี่ยง. BR-01.</summary>
public class RiskThreshold
{
    public Guid Id { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public decimal MinScore { get; set; }
    public decimal MaxScore { get; set; }
}
