using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SvmApi.Application.Auth;
using SvmApi.Application.Dashboard;
using SvmApi.Application.Sla;
using SvmApi.Domain.Notifications;
using SvmApi.Domain.Remediation;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Infrastructure.Auth.DataScope;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Dashboard;

/// <summary>Read-only, SQL-aggregated dashboards. Scope is applied before every instance query.</summary>
public sealed class DashboardService(
    SvmDbContext db,
    ICurrentUser user,
    TimeProvider clock,
    IOptions<SlaOptions> slaOptions) : IDashboardService
{
    private IQueryable<VulnerabilityInstance> Scoped()
        => db.VulnerabilityInstances.AsNoTracking().ApplyDataScope(user);

    public async Task<DashboardResult<OperationsDashboardDto>> GetOperationsAsync(
        int listSize, CancellationToken cancellationToken)
    {
        if (!DashboardMath.IsValidListSize(listSize))
            return DashboardResult<OperationsDashboardDto>.Invalid(new Dictionary<string, string[]>
            {
                ["listSize"] = [$"Must be between 1 and {DashboardMath.MaxListSize}."],
            });

        var now = clock.GetUtcNow().UtcDateTime;
        var fraction = slaOptions.Value.NearDueFraction;
        var organization = DataScopeExtensions.IsOrganizationWide(user);
        var open = Scoped().WhereOpen();
        var unassigned = open.Where(i => i.AssignedToUserId == null);
        var overdue = open.Where(InstanceQueryExtensions.OpenOverdue(now));
        var pending = open.Where(i => i.Status == VulnerabilityStatus.PendingVerification);
        var mine = open.Where(i => i.RemediationTasks.Any(t =>
            user.UserId != null && t.OwnerUserId == user.UserId && t.Status != RemediationTaskStatus.Completed));

        // Failed import rows have no asset scope. Narrow users see only their own batches,
        // matching intake's batch-visibility boundary; no global failures leak through totals.
        var batches = db.ImportBatches.AsNoTracking().ApplyDataScope(user);
        var importErrors = await batches.Where(b => b.Status == ImportBatchStatus.Failed
                || b.Status == ImportBatchStatus.Partial).LongCountAsync(cancellationToken);
        // No integration error queue is persisted. Failed external notification deliveries
        // are the recorded integration failures available in the current schema.
        long? integrationErrors = organization
            ? await db.Notifications.AsNoTracking().Where(n => n.Channel != NotificationChannel.InApp
                && n.DeliveryStatus == DeliveryStatus.Failed).LongCountAsync(cancellationToken)
            : null;

        var summary = new OperationsSummaryDto(
            await open.LongCountAsync(cancellationToken),
            await unassigned.LongCountAsync(cancellationToken),
            await overdue.LongCountAsync(cancellationToken),
            await open.Where(InstanceQueryExtensions.NearDue(now, fraction)).LongCountAsync(cancellationToken),
            await pending.LongCountAsync(cancellationToken),
            await open.Where(i => i.Status == VulnerabilityStatus.Reopened).LongCountAsync(cancellationToken),
            await open.Where(i => i.RiskLevel == RiskLevel.Critical).LongCountAsync(cancellationToken),
            await open.Where(i => i.IsKev).LongCountAsync(cancellationToken),
            await mine.LongCountAsync(cancellationToken), importErrors, integrationErrors)
        {
            HighOpen = await open.Where(i => i.RiskLevel == RiskLevel.High).LongCountAsync(cancellationToken),
        };

        var lists = new OperationsLists(
            await Load(unassigned.OrderByDescending(i => i.RiskScore).ThenBy(i => i.Id)),
            await Load(overdue.OrderBy(i => i.SlaTimer!.ResolutionDue).ThenBy(i => i.Id)),
            await Load(pending.OrderByDescending(i => i.RiskScore).ThenBy(i => i.Id)),
            await Load(mine.OrderBy(i => i.RemediationTasks
                .Where(t => t.OwnerUserId == user.UserId && t.Status != RemediationTaskStatus.Completed)
                .Min(t => t.DueDate)).ThenBy(i => i.Id)));

        var risk = await open.Where(i => i.RiskLevel != null).GroupBy(i => i.RiskLevel!.Value)
            .Select(g => new { Key = g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var riskCounts = DashboardMath.ZeroFill(risk).ToList();
        riskCounts.Add(new CountDto("Unscored", await open.Where(i => i.RiskLevel == null).LongCountAsync(cancellationToken)));
        var statuses = await Scoped().GroupBy(i => i.Status).Select(g => new { Key = g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var units = await open.GroupBy(i => i.Asset.BusinessUnit ?? "Unspecified")
            .Select(g => new { Key = g.Key, Count = g.LongCount() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.Key).ToListAsync(cancellationToken);

        return DashboardResult<OperationsDashboardDto>.Ok(new(now, organization, summary, lists)
        {
            ByRiskLevel = riskCounts,
            ByStatus = DashboardMath.ZeroFill(statuses),
            ByBusinessUnit = units.Select(g => new CountDto(g.Key, g.Count)).ToList(),
            SlaByStatus = await LoadSlaCountsAsync(Scoped(), now, fraction, cancellationToken),
            MeanTimeToRemediateDays = await LoadMttrAsync(Scoped(), now, cancellationToken),
            Trend = await LoadTrendAsync(Scoped(), now, DashboardMath.DefaultWeeks, cancellationToken),
        });

        Task<IReadOnlyList<InstanceListItemDto>> Load(IQueryable<VulnerabilityInstance> query)
            => InstanceListProjection.LoadAsync(db, query.Take(listSize), now, fraction, cancellationToken);
    }

    public async Task<DashboardResult<ExecutiveDashboardDto>> GetExecutiveAsync(
        int weeks, int topAssets, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (!DashboardMath.IsValidWeeks(weeks))
            errors["weeks"] = [$"Must be between {DashboardMath.MinWeeks} and {DashboardMath.MaxWeeks}."];
        if (!DashboardMath.IsValidTopAssets(topAssets))
            errors["topAssets"] = [$"Must be between 1 and {DashboardMath.MaxTopAssets}."];
        if (errors.Count > 0)
            return DashboardResult<ExecutiveDashboardDto>.Invalid(errors);

        var now = clock.GetUtcNow().UtcDateTime;
        var fraction = slaOptions.Value.NearDueFraction;
        var scoped = Scoped();
        var open = scoped.WhereOpen();
        var overdue = open.Where(InstanceQueryExtensions.OpenOverdue(now));
        var nearDue = open.Where(InstanceQueryExtensions.NearDue(now, fraction));
        var withTimer = await scoped.Where(i => i.SlaTimer != null).LongCountAsync(cancellationToken);
        var within = await scoped.Where(InstanceQueryExtensions.WithinSla(now)).LongCountAsync(cancellationToken);

        // A resolved-then-reopened instance is excluded until it is resolved again.
        // Use the latest resolution event for each currently resolved/closed instance.
        var mttr = await LoadMttrAsync(scoped, now, cancellationToken);

        var totals = new ExecutiveTotalsDto(
            await scoped.LongCountAsync(cancellationToken),
            await open.LongCountAsync(cancellationToken),
            await open.Where(i => i.RiskLevel == RiskLevel.Critical).LongCountAsync(cancellationToken),
            await open.Where(i => i.RiskLevel == RiskLevel.High).LongCountAsync(cancellationToken),
            await open.Where(i => i.IsKev).LongCountAsync(cancellationToken),
            await open.Where(i => i.Asset.InternetExposure).LongCountAsync(cancellationToken),
            await open.Where(i => i.AssignedToUserId == null).LongCountAsync(cancellationToken),
            await overdue.LongCountAsync(cancellationToken),
            await nearDue.LongCountAsync(cancellationToken),
            DashboardMath.CompliancePercent(within, withTimer), mttr);

        var risk = await open.Where(i => i.RiskLevel != null).GroupBy(i => i.RiskLevel!.Value)
            .Select(g => new { Key = g.Key, Count = g.LongCount() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var riskCounts = DashboardMath.ZeroFill(risk).ToList();
        riskCounts.Add(new CountDto("Unscored", await open.Where(i => i.RiskLevel == null).LongCountAsync(cancellationToken)));
        var statuses = await scoped.GroupBy(i => i.Status).Select(g => new { Key = g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var units = await open.GroupBy(i => i.Asset.BusinessUnit ?? "Unspecified")
            .Select(g => new { Key = g.Key, Count = g.LongCount() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.Key).ToListAsync(cancellationToken);

        var sla = await LoadSlaCountsAsync(scoped, now, fraction, cancellationToken);

        // Duration buckets use elapsed full days, matching DashboardMath, rather than
        // midnight boundaries (DateDiffDay can promote a just-overdue timer early).
        var age8 = now.AddDays(-8);
        var age31 = now.AddDays(-31);
        var age91 = now.AddDays(-91);
        var aging = await overdue.GroupBy(i => i.SlaTimer!.ResolutionDue > age8 ? 0
                : i.SlaTimer.ResolutionDue > age31 ? 1 : i.SlaTimer.ResolutionDue > age91 ? 2 : 3)
            .Select(g => new { Key = g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        var trend = await LoadTrendAsync(scoped, now, weeks, cancellationToken);

        var assets = await open.GroupBy(i => new { i.AssetId, i.Asset.AssetCode, i.Asset.Name, i.Asset.BusinessUnit })
            .Select(g => new
            {
                g.Key.AssetId, g.Key.AssetCode, g.Key.Name, g.Key.BusinessUnit,
                OpenCount = g.LongCount(), CriticalCount = g.LongCount(i => i.RiskLevel == RiskLevel.Critical),
                HighCount = g.LongCount(i => i.RiskLevel == RiskLevel.High), MaxRiskScore = g.Max(i => i.RiskScore),
                KevCount = g.LongCount(i => i.IsKev),
            })
            .OrderByDescending(a => a.MaxRiskScore).ThenByDescending(a => a.CriticalCount)
            .ThenByDescending(a => a.OpenCount).ThenBy(a => a.AssetId)
            .Take(topAssets).ToListAsync(cancellationToken);

        return DashboardResult<ExecutiveDashboardDto>.Ok(new(now, DataScopeExtensions.IsOrganizationWide(user), weeks,
            totals, riskCounts, DashboardMath.ZeroFill(statuses), units.Select(g => new CountDto(g.Key, g.Count)).ToList(),
            sla, DashboardMath.BuildOverdueAging(aging),
            trend,
            assets.Select(a => new TopRiskAssetDto(a.AssetId, a.AssetCode, a.Name, a.BusinessUnit,
                a.OpenCount, a.CriticalCount, a.HighCount, a.MaxRiskScore, a.KevCount)).ToList()));
    }

    private static async Task<IReadOnlyList<CountDto>> LoadSlaCountsAsync(
        IQueryable<VulnerabilityInstance> scoped, DateTime now, double fraction, CancellationToken ct)
        => new List<CountDto>
        {
            new("OnTrack", await scoped.Where(InstanceQueryExtensions.OnTrack(now, fraction)).LongCountAsync(ct)),
            new("NearDue", await scoped.Where(InstanceQueryExtensions.NearDue(now, fraction)).LongCountAsync(ct)),
            new("Breached", await scoped.Where(InstanceQueryExtensions.Breached(now)).LongCountAsync(ct)),
            new("Met", await scoped.Where(InstanceQueryExtensions.Met()).LongCountAsync(ct)),
        };

    private static Task<double?> LoadMttrAsync(IQueryable<VulnerabilityInstance> scoped, DateTime now, CancellationToken ct)
        => scoped.Where(i => i.Status == VulnerabilityStatus.Resolved || i.Status == VulnerabilityStatus.Closed)
            .Select(i => new
            {
                i.FirstDetectedAt,
                ResolvedAt = i.StatusHistories.Where(h => h.ToStatus == VulnerabilityStatus.Resolved && h.ChangedAt <= now)
                    .Max(h => (DateTime?)h.ChangedAt),
            })
            .Where(i => i.ResolvedAt != null && i.ResolvedAt >= i.FirstDetectedAt)
            .Select(i => (double?)EF.Functions.DateDiffSecond(i.FirstDetectedAt, i.ResolvedAt!.Value) / 86400d)
            .AverageAsync(ct);

    private async Task<IReadOnlyList<WeeklyTrendPoint>> LoadTrendAsync(
        IQueryable<VulnerabilityInstance> scoped, DateTime now, int weeks, CancellationToken ct)
    {
        var start = DashboardMath.TrendWindowStart(now, weeks);
        var created = await scoped.Where(i => i.FirstDetectedAt >= start && i.FirstDetectedAt <= now)
            .GroupBy(i => EF.Functions.DateDiffDay(start, i.FirstDetectedAt) / 7)
            .Select(g => new { Week = g.Key, Total = g.LongCount(), Critical = g.LongCount(i => i.RiskLevel == RiskLevel.Critical) })
            .ToListAsync(ct);
        var resolved = await db.StatusHistories.AsNoTracking()
            .Where(h => scoped.Any(i => i.Id == h.InstanceId) && h.ToStatus == VulnerabilityStatus.Resolved
                && h.ChangedAt >= start && h.ChangedAt <= now)
            .Select(h => new { h.InstanceId, Week = EF.Functions.DateDiffDay(start, h.ChangedAt) / 7 })
            .Distinct().GroupBy(h => h.Week)
            .Select(g => new { Week = g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(g => g.Week, g => g.Count, ct);
        return DashboardMath.BuildWeeklyTrend(start, weeks,
            created.ToDictionary(g => g.Week, g => (g.Total, g.Critical)), resolved);
    }
}
