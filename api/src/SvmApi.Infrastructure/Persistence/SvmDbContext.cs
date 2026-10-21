using Microsoft.EntityFrameworkCore;
using SvmApi.Domain.Access;
using SvmApi.Domain.Assets;
using SvmApi.Domain.Audit;
using SvmApi.Domain.Notifications;
using SvmApi.Domain.Reference;
using SvmApi.Domain.Remediation;
using SvmApi.Domain.Risk;
using SvmApi.Domain.Sla;
using SvmApi.Domain.Verification;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Domain.Workflow;

namespace SvmApi.Infrastructure.Persistence;

/// <summary>EF Core DbContext หลักของ SVM — map ~28 entities ตาม data-model.md.
/// Naming: ToTable("master_"/"config_"/"trans_" + lowerCamelCase) ตาม implementation.md.</summary>
public class SvmDbContext(DbContextOptions<SvmDbContext> options) : DbContext(options)
{
    // Access (RBAC)
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserScope> UserScopes => Set<UserScope>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    // Asset
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetOwnership> AssetOwnerships => Set<AssetOwnership>();

    // Reference Data
    public DbSet<Cve> Cves => Set<Cve>();
    public DbSet<Cwe> Cwes => Set<Cwe>();
    public DbSet<CvssVersion> CvssVersions => Set<CvssVersion>();

    // Vulnerability
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<VulnerabilityFinding> VulnerabilityFindings => Set<VulnerabilityFinding>();
    public DbSet<VulnerabilityInstance> VulnerabilityInstances => Set<VulnerabilityInstance>();
    public DbSet<Comment> Comments => Set<Comment>();

    // Risk
    public DbSet<RiskWeight> RiskWeights => Set<RiskWeight>();
    public DbSet<RiskThreshold> RiskThresholds => Set<RiskThreshold>();
    public DbSet<RiskScoreHistory> RiskScoreHistories => Set<RiskScoreHistory>();

    // Workflow
    public DbSet<StatusTransition> StatusTransitions => Set<StatusTransition>();
    public DbSet<StatusHistory> StatusHistories => Set<StatusHistory>();

    // Assignment & Remediation
    public DbSet<AssignmentRule> AssignmentRules => Set<AssignmentRule>();
    public DbSet<RemediationTask> RemediationTasks => Set<RemediationTask>();
    public DbSet<Evidence> Evidences => Set<Evidence>();

    // SLA
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<BusinessCalendar> BusinessCalendars => Set<BusinessCalendar>();
    public DbSet<SlaTimer> SlaTimers => Set<SlaTimer>();

    // Verification
    public DbSet<VerificationRecord> VerificationRecords => Set<VerificationRecord>();

    // Notification
    public DbSet<Notification> Notifications => Set<Notification>();

    // Audit (append-only — ดู AuditLogConfiguration สำหรับ constraint)
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SvmDbContext).Assembly);
    }
}
