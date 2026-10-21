using SvmApi.Domain.Access;

namespace SvmApi.Domain.Assignments;

public enum AssignmentMatchType
{
    BusinessUnit,
    AssetType,
    Technology,
}

/// <summary>config_assignmentRule — กฎมอบหมายอัตโนมัติ. FR-24, BR-07, EX-59, US-020.</summary>
public class AssignmentRule
{
    public Guid Id { get; set; }
    public AssignmentMatchType MatchType { get; set; }
    public string MatchValue { get; set; } = string.Empty;

    public Guid? AssignToUserId { get; set; }
    public User? AssignToUser { get; set; }
    public string? AssignToTeam { get; set; }

    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
}
