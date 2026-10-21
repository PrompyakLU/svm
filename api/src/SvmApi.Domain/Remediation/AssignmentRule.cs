namespace SvmApi.Domain.Remediation;

public enum AssignmentMatchType
{
    BusinessUnit,
    AssetType,
    Technology,
}

/// <summary>config_assignmentRule — กฎมอบหมายอัตโนมัติ. FR-24, BR-07, EX-59.</summary>
public class AssignmentRule
{
    public Guid Id { get; set; }
    public AssignmentMatchType MatchType { get; set; }
    public string MatchValue { get; set; } = default!;

    public Guid? AssignToUserId { get; set; }
    public string? AssignToTeam { get; set; }

    /// <summary>ลำดับความสำคัญของกฎ (ค่าน้อย = ตรวจก่อน).</summary>
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
}
