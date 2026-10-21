using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Domain.Risk;

public enum RiskCalculationType
{
    Auto,
    Manual,
    Recalc,
}

/// <summary>trans_riskScoreHistory — ประวัติการคำนวณ/ปรับ risk score ของ instance. FR-20, FR-21.</summary>
public class RiskScoreHistory
{
    public Guid Id { get; set; }

    public Guid InstanceId { get; set; }
    public VulnerabilityInstance Instance { get; set; } = default!;

    public decimal Score { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public RiskCalculationType CalculationType { get; set; }

    /// <summary>JSON แจกแจงคะแนนตามปัจจัย (nvarchar(max)).</summary>
    public string FactorBreakdown { get; set; } = default!;

    public string? Reason { get; set; }
    public decimal? PreviousScore { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
