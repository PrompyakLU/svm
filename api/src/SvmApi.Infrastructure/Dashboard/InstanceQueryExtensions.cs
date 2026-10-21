using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SvmApi.Application.Dashboard;
using SvmApi.Domain.Sla;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Infrastructure.Dashboard;

/// <summary>
/// predicate/ORDER BY ของ instance ที่ใช้ร่วมกันระหว่างรายการ (<c>GET /instances</c>) และ dashboard — แปลงเป็น SQL ได้ทั้งหมด.
/// การเรียงลำดับสร้างจาก allowlist (<see cref="InstanceSortField"/>) เท่านั้น ไม่เคยมาจากสตริงของผู้ใช้.
/// นิยาม SLA ตรงกับ <c>SlaCalculator.EvaluateBreach</c>/<c>InstanceQueryRules.BuildSla</c> (คำนวณสดจากเวลา ณ ตอนอ่าน):
/// Running = สถานะยังเปิด; Breached = due &lt; now; NearDue = เหลือ &lt;= fraction ของช่วง started→due; Met = หยุดนับและไม่เคยเกิน.
/// </summary>
internal static class InstanceQueryExtensions
{
    private static readonly Regex CodePattern =
        new("^VUL-([0-9A-Fa-f]{8})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // ------------------------------------------------------------------ open / SLA predicates

    public static IQueryable<VulnerabilityInstance> WhereOpen(this IQueryable<VulnerabilityInstance> source)
    {
        var closed = InstanceQueryRules.ClosedStatuses.ToList();
        return source.Where(i => !closed.Contains(i.Status));
    }

    /// <summary>เกิน SLA: กำลังนับและ due &lt; now หรือหยุดนับโดยเคยเกิน.</summary>
    public static Expression<Func<VulnerabilityInstance, bool>> Breached(DateTime now)
    {
        var closed = InstanceQueryRules.ClosedStatuses.ToList();
        return i => i.SlaTimer != null
                    && ((!closed.Contains(i.Status) && i.SlaTimer.ResolutionDue < now)
                        || (closed.Contains(i.Status) && i.SlaTimer.BreachStatus == SlaBreachStatus.Breached));
    }

    /// <summary>เกิน SLA เฉพาะที่ยังเปิด (ใช้นับงานค้าง/aging).</summary>
    public static Expression<Func<VulnerabilityInstance, bool>> OpenOverdue(DateTime now)
    {
        var closed = InstanceQueryRules.ClosedStatuses.ToList();
        return i => i.SlaTimer != null && !closed.Contains(i.Status) && i.SlaTimer.ResolutionDue < now;
    }

    /// <summary>ใกล้ครบกำหนด: ยังเปิด, ยังไม่เกิน, เหลือ &lt;= fraction ของช่วง started→due.</summary>
    public static Expression<Func<VulnerabilityInstance, bool>> NearDue(DateTime now, double fraction)
    {
        var closed = InstanceQueryRules.ClosedStatuses.ToList();
        return i => i.SlaTimer != null
                    && !closed.Contains(i.Status)
                    && i.SlaTimer.ResolutionDue >= now
                    && EF.Functions.DateDiffSecond(i.SlaTimer.StartedAt, i.SlaTimer.ResolutionDue) > 0
                    && EF.Functions.DateDiffSecond(now, i.SlaTimer.ResolutionDue)
                       <= EF.Functions.DateDiffSecond(i.SlaTimer.StartedAt, i.SlaTimer.ResolutionDue) * fraction;
    }

    public static Expression<Func<VulnerabilityInstance, bool>> OnTrack(DateTime now, double fraction)
    {
        var closed = InstanceQueryRules.ClosedStatuses.ToList();
        return i => i.SlaTimer != null
                    && !closed.Contains(i.Status)
                    && i.SlaTimer.ResolutionDue >= now
                    && !(EF.Functions.DateDiffSecond(i.SlaTimer.StartedAt, i.SlaTimer.ResolutionDue) > 0
                         && EF.Functions.DateDiffSecond(now, i.SlaTimer.ResolutionDue)
                            <= EF.Functions.DateDiffSecond(i.SlaTimer.StartedAt, i.SlaTimer.ResolutionDue) * fraction);
    }

    /// <summary>หยุดนับ SLA และไม่เคยเกินกำหนด.</summary>
    public static Expression<Func<VulnerabilityInstance, bool>> Met()
    {
        var closed = InstanceQueryRules.ClosedStatuses.ToList();
        return i => i.SlaTimer != null && closed.Contains(i.Status) && i.SlaTimer.BreachStatus != SlaBreachStatus.Breached;
    }

    /// <summary>กำลังนับ SLA และอยู่ในกำหนด (OnTrack หรือ NearDue) — ตัวตั้งของ compliance.</summary>
    public static Expression<Func<VulnerabilityInstance, bool>> WithinSla(DateTime now)
    {
        var closed = InstanceQueryRules.ClosedStatuses.ToList();
        return i => i.SlaTimer != null
                    && ((!closed.Contains(i.Status) && i.SlaTimer.ResolutionDue >= now)
                        || (closed.Contains(i.Status) && i.SlaTimer.BreachStatus != SlaBreachStatus.Breached));
    }

    // ------------------------------------------------------------------ filters

    /// <summary>
    /// ใช้ filter ทั้งหมดของ <paramref name="p"/>. ผู้เรียกต้องเรียก <c>ApplyDataScope</c> ก่อนเสมอ (scope มาก่อน filter/count/paging).
    /// </summary>
    public static IQueryable<VulnerabilityInstance> ApplyFilters(
        this IQueryable<VulnerabilityInstance> source,
        ParsedInstanceQuery p,
        Guid? currentUserId,
        DateTime now,
        double nearDueFraction)
    {
        var q = source;

        if (p.OpenOnly)
        {
            q = q.WhereOpen();
        }

        if (p.Statuses.Count > 0)
        {
            var statuses = p.Statuses.ToList();
            q = q.Where(i => statuses.Contains(i.Status));
        }

        if (p.RiskLevels.Count > 0)
        {
            var levels = p.RiskLevels.Select(l => (RiskLevel?)l).ToList();
            q = q.Where(i => levels.Contains(i.RiskLevel));
        }

        if (p.Severity is { } severity)
        {
            q = q.Where(i => i.Findings.Any(f => f.RawSeverity == severity));
        }

        if (p.BusinessUnit is { } businessUnit)
        {
            q = q.Where(i => i.Asset.BusinessUnit == businessUnit);
        }

        if (p.AssetCode is { } assetCode)
        {
            q = q.Where(i => i.Asset.AssetCode == assetCode);
        }

        if (p.AssetId is { } assetId)
        {
            q = q.Where(i => i.AssetId == assetId);
        }

        if (p.AssignedToUserId is { } assignee)
        {
            q = q.Where(i => i.AssignedToUserId == assignee);
        }

        if (p.AssignedToMe)
        {
            // ผู้ใช้ไม่มี id (ไม่ควรเกิดหลัง authenticate) = ไม่เห็นอะไร (fail-closed)
            q = currentUserId is { } me ? q.Where(i => i.AssignedToUserId == me) : q.Where(i => false);
        }

        if (p.Unassigned is { } unassigned)
        {
            q = unassigned ? q.Where(i => i.AssignedToUserId == null) : q.Where(i => i.AssignedToUserId != null);
        }

        if (p.CveId is { } cve)
        {
            q = q.Where(i => i.CveId == cve);
        }

        if (p.IsKev is { } isKev)
        {
            q = q.Where(i => i.IsKev == isKev);
        }

        if (p.InternetExposure is { } exposure)
        {
            q = q.Where(i => i.Asset.InternetExposure == exposure);
        }

        if (p.DetectedFrom is { } detectedFrom)
        {
            q = q.Where(i => i.FirstDetectedAt >= detectedFrom);
        }

        if (p.DetectedToExclusive is { } detectedTo)
        {
            q = q.Where(i => i.FirstDetectedAt < detectedTo);
        }

        if (p.DueFrom is { } dueFrom)
        {
            q = q.Where(i => i.SlaTimer != null && i.SlaTimer.ResolutionDue >= dueFrom);
        }

        if (p.DueToExclusive is { } dueTo)
        {
            q = q.Where(i => i.SlaTimer != null && i.SlaTimer.ResolutionDue < dueTo);
        }

        if (p.AgeDaysMin is { } ageMin)
        {
            var newest = now.AddDays(-ageMin);
            q = q.Where(i => i.FirstDetectedAt <= newest);
        }

        if (p.AgeDaysMax is { } ageMax)
        {
            var oldest = now.AddDays(-(ageMax + 1.0));
            q = q.Where(i => i.FirstDetectedAt > oldest);
        }

        if (p.SlaStatuses.Count > 0)
        {
            var predicates = p.SlaStatuses.Select(s => s switch
            {
                SlaFilter.Breached => Breached(now),
                SlaFilter.NearDue => NearDue(now, nearDueFraction),
                SlaFilter.OnTrack => OnTrack(now, nearDueFraction),
                _ => Met(),
            }).ToList();
            q = q.Where(AnyOf(predicates));
        }

        if (p.Search is { } search)
        {
            q = ApplySearch(q, search);
        }

        return q;
    }

    private static IQueryable<VulnerabilityInstance> ApplySearch(IQueryable<VulnerabilityInstance> q, string search)
    {
        // รหัสที่แสดง (VUL-XXXXXXXX) = 8 hex แรกของ id; GUID เต็ม = ตรงทั้งค่า
        var codeMatch = CodePattern.Match(search);
        if (codeMatch.Success)
        {
            var prefix = codeMatch.Groups[1].Value;
            return q.Where(i => i.Id.ToString().StartsWith(prefix));
        }

        if (Guid.TryParse(search, out var id))
        {
            return q.Where(i => i.Id == id);
        }

        return q.Where(i => i.Title.Contains(search)
                            || i.Asset.AssetCode.Contains(search)
                            || i.Asset.Name.Contains(search)
                            || (i.CveId != null && i.CveId.Contains(search))
                            || (i.Location != null && i.Location.Contains(search))
                            || (i.Component != null && i.Component.Contains(search)));
    }

    // ------------------------------------------------------------------ sorting (allowlist only)

    /// <summary>
    /// ORDER BY จาก allowlist + tie-breaker <c>Id</c> เสมอ (ผลคงที่ระหว่างหน้า). SlaDueDate: instance ไม่มี timer อยู่ท้ายสุดเสมอ.
    /// </summary>
    public static IOrderedQueryable<VulnerabilityInstance> ApplySort(
        this IQueryable<VulnerabilityInstance> source, IReadOnlyList<InstanceSortKey> keys)
    {
        IOrderedQueryable<VulnerabilityInstance>? ordered = null;

        foreach (var key in keys)
        {
            var d = key.Descending;
            ordered = key.Field switch
            {
                InstanceSortField.RiskScore => Order(source, ordered, i => i.RiskScore, d),
                InstanceSortField.FirstDetectedAt => Order(source, ordered, i => i.FirstDetectedAt, d),
                InstanceSortField.LastDetectedAt => Order(source, ordered, i => i.LastDetectedAt, d),
                InstanceSortField.Status => Order(source, ordered, i => i.Status, d),
                InstanceSortField.Title => Order(source, ordered, i => i.Title, d),
                InstanceSortField.AssetCode => Order(source, ordered, i => i.Asset.AssetCode, d),
                InstanceSortField.BusinessUnit => Order(source, ordered, i => i.Asset.BusinessUnit, d),
                InstanceSortField.SlaDueDate => Order(
                    source, Order(source, ordered, i => i.SlaTimer == null, false), i => i.SlaTimer!.ResolutionDue, d),
                _ => ordered ?? source.OrderBy(i => i.Id),
            };
        }

        return ordered is null ? source.OrderBy(i => i.Id) : ordered.ThenBy(i => i.Id);
    }

    private static IOrderedQueryable<VulnerabilityInstance> Order<TKey>(
        IQueryable<VulnerabilityInstance> source,
        IOrderedQueryable<VulnerabilityInstance>? current,
        Expression<Func<VulnerabilityInstance, TKey>> key,
        bool descending)
    {
        if (current is null)
        {
            return descending ? source.OrderByDescending(key) : source.OrderBy(key);
        }

        return descending ? current.ThenByDescending(key) : current.ThenBy(key);
    }

    // ------------------------------------------------------------------ expression helpers

    /// <summary>OR ของหลาย predicate บน parameter ตัวเดียว (ไม่ใช้ Expression.Invoke เพราะ EF แปลงไม่ได้).</summary>
    private static Expression<Func<VulnerabilityInstance, bool>> AnyOf(
        IReadOnlyList<Expression<Func<VulnerabilityInstance, bool>>> predicates)
    {
        var parameter = Expression.Parameter(typeof(VulnerabilityInstance), "x");
        Expression? body = null;

        foreach (var predicate in predicates)
        {
            var rewritten = new ParameterReplacer(predicate.Parameters[0], parameter).Visit(predicate.Body)!;
            body = body is null ? rewritten : Expression.OrElse(body, rewritten);
        }

        return Expression.Lambda<Func<VulnerabilityInstance, bool>>(body ?? Expression.Constant(false), parameter);
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
