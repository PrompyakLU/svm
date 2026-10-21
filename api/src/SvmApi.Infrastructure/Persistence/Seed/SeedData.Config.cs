using SvmApi.Domain.Assets;
using SvmApi.Domain.Remediation;
using SvmApi.Domain.Risk;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Infrastructure.Persistence.Seed;

internal static partial class SeedData
{
    internal sealed record RiskWeightSeed(RiskFactor Factor, decimal Weight);

    internal sealed record RiskThresholdSeed(RiskLevel RiskLevel, decimal MinScore, decimal MaxScore);

    internal sealed record StatusTransitionSeed(
        VulnerabilityStatus From,
        VulnerabilityStatus To,
        string RequiredPermission,
        bool RequiresEvidence,
        bool RequiresApproval);

    internal sealed record SlaPolicySeed(
        RiskLevel RiskLevel,
        AssetCriticality? AssetCriticality,
        bool? InternetExposure,
        bool KevOnly,
        int AcknowledgeHours,
        int StartHours,
        int ResolveHours);

    internal sealed record AssignmentRuleSeed(
        AssignmentMatchType MatchType,
        string MatchValue,
        string AssignToTeam,
        int Priority);

    /// <summary>
    /// น้ำหนัก risk factor ครบ 9 ตัว ผลรวม = 1.0000 พอดี (FR-16, NFR-31, EX-54).
    /// Cvss .25 · AssetCriticality .15 · BusinessImpact .10 · InternetExposure .10 · ExploitAvailability .10 ·
    /// Kev .10 · ThreatIntel .05 · VulnAge .05 · ExistingControl .10
    /// </summary>
    internal static IReadOnlyList<RiskWeightSeed> RiskWeights { get; } =
    [
        new(RiskFactor.Cvss, 0.2500m),
        new(RiskFactor.AssetCriticality, 0.1500m),
        new(RiskFactor.BusinessImpact, 0.1000m),
        new(RiskFactor.InternetExposure, 0.1000m),
        new(RiskFactor.ExploitAvailability, 0.1000m),
        new(RiskFactor.Kev, 0.1000m),
        new(RiskFactor.ThreatIntel, 0.0500m),
        new(RiskFactor.VulnAge, 0.0500m),
        new(RiskFactor.ExistingControl, 0.1000m),
    ];

    /// <summary>
    /// ช่วงคะแนน risk level ครอบ 0–100 ต่อเนื่อง ไม่มีช่องว่าง/ทับ (BR-01).
    /// ข้อตกลง: จัดระดับด้วย <c>MinScore &lt;= score &lt; MaxScore</c> ยกเว้นระดับบนสุด (Critical)
    /// ที่ <c>MaxScore = 100</c> รวมค่าปลายทาง (<c>score &lt;= 100</c>).
    /// </summary>
    internal static IReadOnlyList<RiskThresholdSeed> RiskThresholds { get; } =
    [
        new(RiskLevel.Informational, 0m, 20m),
        new(RiskLevel.Low, 20m, 40m),
        new(RiskLevel.Medium, 40m, 60m),
        new(RiskLevel.High, 60m, 80m),
        new(RiskLevel.Critical, 80m, 100m),
    ];

    private const string TransitionPerm = PermissionCatalog.InstanceTransition;
    private const string AssignPerm = PermissionCatalog.InstanceAssign;
    private const string SubmitPerm = PermissionCatalog.VerificationSubmit;
    private const string RecordPerm = PermissionCatalog.VerificationRecord;
    private const string ApprovePerm = PermissionCatalog.InstanceApprove;

    private static StatusTransitionSeed Tr(
        VulnerabilityStatus from,
        VulnerabilityStatus to,
        string permission,
        bool evidence = false,
        bool approval = false)
        => new(from, to, permission, evidence, approval);

    /// <summary>
    /// state machine ของ 13 สถานะ (US-018/019 · FR-22/23 · BR-27/14/15).
    /// - เข้า PendingVerification และ Closed ต้องมี evidence (EX-56, BR-14)
    /// - FalsePositive / RiskAccepted / ExceptionApproved ต้องมี evidence + approval และใช้ <c>instance.approve</c>
    /// - Resolved/Closed/Reopened-จาก-verification ใช้ <c>verification.record</c> (remediation owner ปิดงานตนเองไม่ได้)
    /// - Archived เป็นสถานะปลายทาง (ไม่มี transition ออก)
    /// - กติกา Critical ต้อง independent verification (BR-16) ตรวจในชั้น business logic ไม่ใช่ในตารางนี้
    /// </summary>
    internal static IReadOnlyList<StatusTransitionSeed> StatusTransitions { get; } =
    [
        // Triage
        Tr(VulnerabilityStatus.New, VulnerabilityStatus.UnderReview, TransitionPerm),
        Tr(VulnerabilityStatus.UnderReview, VulnerabilityStatus.Confirmed, TransitionPerm),
        Tr(VulnerabilityStatus.UnderReview, VulnerabilityStatus.FalsePositive, ApprovePerm, evidence: true, approval: true),
        Tr(VulnerabilityStatus.Confirmed, VulnerabilityStatus.FalsePositive, ApprovePerm, evidence: true, approval: true),

        // Assignment
        Tr(VulnerabilityStatus.Confirmed, VulnerabilityStatus.Assigned, AssignPerm),
        Tr(VulnerabilityStatus.Assigned, VulnerabilityStatus.Confirmed, AssignPerm),
        Tr(VulnerabilityStatus.Reopened, VulnerabilityStatus.Assigned, AssignPerm),
        Tr(VulnerabilityStatus.Reopened, VulnerabilityStatus.UnderReview, TransitionPerm),

        // Remediation
        Tr(VulnerabilityStatus.Assigned, VulnerabilityStatus.InProgress, TransitionPerm),
        Tr(VulnerabilityStatus.InProgress, VulnerabilityStatus.Assigned, AssignPerm),
        Tr(VulnerabilityStatus.InProgress, VulnerabilityStatus.PendingVerification, SubmitPerm, evidence: true),

        // Verification & closure
        Tr(VulnerabilityStatus.PendingVerification, VulnerabilityStatus.Resolved, RecordPerm, evidence: true),
        Tr(VulnerabilityStatus.PendingVerification, VulnerabilityStatus.Reopened, RecordPerm, evidence: true),
        Tr(VulnerabilityStatus.Resolved, VulnerabilityStatus.Closed, RecordPerm, evidence: true),
        Tr(VulnerabilityStatus.Resolved, VulnerabilityStatus.Reopened, TransitionPerm),
        Tr(VulnerabilityStatus.Closed, VulnerabilityStatus.Reopened, TransitionPerm),
        Tr(VulnerabilityStatus.Closed, VulnerabilityStatus.Archived, TransitionPerm),

        // Governance (full approval matrix อยู่นอก core slice)
        Tr(VulnerabilityStatus.Confirmed, VulnerabilityStatus.RiskAccepted, ApprovePerm, evidence: true, approval: true),
        Tr(VulnerabilityStatus.Assigned, VulnerabilityStatus.RiskAccepted, ApprovePerm, evidence: true, approval: true),
        Tr(VulnerabilityStatus.InProgress, VulnerabilityStatus.RiskAccepted, ApprovePerm, evidence: true, approval: true),
        Tr(VulnerabilityStatus.Confirmed, VulnerabilityStatus.ExceptionApproved, ApprovePerm, evidence: true, approval: true),
        Tr(VulnerabilityStatus.Assigned, VulnerabilityStatus.ExceptionApproved, ApprovePerm, evidence: true, approval: true),
        Tr(VulnerabilityStatus.InProgress, VulnerabilityStatus.ExceptionApproved, ApprovePerm, evidence: true, approval: true),
        Tr(VulnerabilityStatus.RiskAccepted, VulnerabilityStatus.Reopened, TransitionPerm),
        Tr(VulnerabilityStatus.ExceptionApproved, VulnerabilityStatus.Reopened, TransitionPerm),
        Tr(VulnerabilityStatus.FalsePositive, VulnerabilityStatus.Reopened, TransitionPerm),
        Tr(VulnerabilityStatus.FalsePositive, VulnerabilityStatus.Archived, TransitionPerm),
    ];

    private static SlaPolicySeed Sla(
        RiskLevel level,
        int ackHours,
        int startHours,
        int resolveHours,
        bool? internetExposure = null,
        bool kevOnly = false)
        => new(level, null, internetExposure, kevOnly, ackHours, startHours, resolveHours);

    /// <summary>
    /// SLA policy (FR-35/66 · BR-08 · US-024):
    /// ฐานต่อ risk level — Critical 7 วัน (168 ชม.), High 14 วัน, Medium 30 วัน, Low 90 วัน
    /// (Informational 180 วัน เป็นสมมติฐานของ seed เพื่อไม่ให้เกิด EX-66) และแถวที่เข้มกว่าสำหรับ KEV / internet-facing.
    /// การเลือกแถวที่ตรงที่สุด (most specific match) เป็นหน้าที่ของ SLA engine.
    /// </summary>
    internal static IReadOnlyList<SlaPolicySeed> SlaPolicies { get; } =
    [
        // ฐาน (ไม่ระบุ criticality/exposure, ไม่ใช่ KEV)
        Sla(RiskLevel.Critical, 4, 24, 168),
        Sla(RiskLevel.High, 8, 48, 336),
        Sla(RiskLevel.Medium, 24, 120, 720),
        Sla(RiskLevel.Low, 72, 240, 2160),
        Sla(RiskLevel.Informational, 168, 720, 4320),

        // Internet-facing เข้มกว่าฐาน
        Sla(RiskLevel.Critical, 2, 12, 120, internetExposure: true),
        Sla(RiskLevel.High, 4, 24, 240, internetExposure: true),
        Sla(RiskLevel.Medium, 12, 72, 504, internetExposure: true),

        // KEV เข้มกว่าฐาน
        Sla(RiskLevel.Critical, 2, 12, 72, kevOnly: true),
        Sla(RiskLevel.High, 4, 24, 168, kevOnly: true),
        Sla(RiskLevel.Medium, 8, 48, 336, kevOnly: true),

        // KEV + internet-facing เข้มที่สุด
        Sla(RiskLevel.Critical, 1, 4, 48, internetExposure: true, kevOnly: true),
        Sla(RiskLevel.High, 2, 12, 120, internetExposure: true, kevOnly: true),
    ];

    /// <summary>
    /// กฎมอบหมายตัวอย่าง (FR-24 · BR-07): AssetType (priority 10, เฉพาะเจาะจงกว่า) ก่อน BusinessUnit (priority 20).
    /// ใช้ชื่อทีมเท่านั้น (ไม่ผูก user) เพื่อให้ทำงานได้แม้ไม่ได้สร้าง demo users.
    /// </summary>
    internal static IReadOnlyList<AssignmentRuleSeed> AssignmentRules { get; } =
    [
        new(AssignmentMatchType.AssetType, nameof(AssetType.Database), SeedTeams.DatabaseAdministration, 10),
        new(AssignmentMatchType.AssetType, nameof(AssetType.NetworkDevice), SeedTeams.NetworkOperations, 10),
        new(AssignmentMatchType.AssetType, nameof(AssetType.CloudResource), SeedTeams.CloudPlatform, 10),

        new(AssignmentMatchType.BusinessUnit, SeedBusinessUnits.ItInfrastructure, SeedTeams.InfrastructureOperations, 20),
        new(AssignmentMatchType.BusinessUnit, SeedBusinessUnits.DigitalChannels, SeedTeams.DigitalChannelsDev, 20),
        new(AssignmentMatchType.BusinessUnit, SeedBusinessUnits.CorporateFinance, SeedTeams.FinanceSystems, 20),
        new(AssignmentMatchType.BusinessUnit, SeedBusinessUnits.HumanResources, SeedTeams.HrSystems, 20),
    ];
}
