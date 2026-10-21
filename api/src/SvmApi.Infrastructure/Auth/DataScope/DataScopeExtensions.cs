using System.Linq.Expressions;
using SvmApi.Application.Auth;
using SvmApi.Domain.Assets;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Infrastructure.Auth.DataScope;

/// <summary>
/// Data scope filter กลาง (US-002, FR-63, BR-30): ใช้ก่อน count/pagination และก่อนคืน detail.
/// grant หลายตัวรวมแบบ union; ไม่มี grant = ไม่เห็นอะไรเลย; ต้องแปลเป็น SQL ได้ (ไม่มี client evaluation).
/// <list type="bullet">
/// <item><c>Organization:*</c> = ทั้งหมด (Organization ที่ค่าอื่น ๆ ไม่ให้สิทธิ์)</item>
/// <item><c>BusinessUnit</c> = asset.BusinessUnit ตรงค่า</item>
/// <item><c>Asset</c> = asset.AssetCode ตรงค่า</item>
/// <item><c>Responsibility</c> = ผู้ใช้เป็น owner ของ asset หรือถูก assign vulnerability นั้น</item>
/// </list>
/// </summary>
public static class DataScopeExtensions
{
    public const string OrganizationType = "Organization";
    public const string BusinessUnitType = "BusinessUnit";
    public const string AssetScopeType = "Asset";
    public const string ResponsibilityType = "Responsibility";
    public const string OrganizationAll = "*";

    /// <summary>กรอง asset ตาม scope ของผู้ใช้.</summary>
    public static IQueryable<Asset> ApplyDataScope(this IQueryable<Asset> source, ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(user);

        var grants = Grants.From(user);
        if (grants.GrantsNothing)
        {
            return source.Where(a => false);
        }

        if (grants.GrantsEverything)
        {
            return source;
        }

        var predicates = new List<Expression<Func<Asset, bool>>>();

        if (grants.BusinessUnits.Count > 0)
        {
            var businessUnits = grants.BusinessUnits;
            predicates.Add(a => a.BusinessUnit != null && businessUnits.Contains(a.BusinessUnit));
        }

        if (grants.AssetCodes.Count > 0)
        {
            var assetCodes = grants.AssetCodes;
            predicates.Add(a => assetCodes.Contains(a.AssetCode));
        }

        if (grants.UserIdForResponsibility is { } responsibleUserId)
        {
            predicates.Add(a => a.Ownerships.Any(o => o.OwnerUserId == responsibleUserId));
        }

        return source.Where(Or(predicates));
    }

    /// <summary>กรอง vulnerability instance ตาม scope ของผู้ใช้ (ผ่าน asset ของมัน).</summary>
    public static IQueryable<VulnerabilityInstance> ApplyDataScope(
        this IQueryable<VulnerabilityInstance> source, ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(user);

        var grants = Grants.From(user);
        if (grants.GrantsNothing)
        {
            return source.Where(v => false);
        }

        if (grants.GrantsEverything)
        {
            return source;
        }

        var predicates = new List<Expression<Func<VulnerabilityInstance, bool>>>();

        if (grants.BusinessUnits.Count > 0)
        {
            var businessUnits = grants.BusinessUnits;
            predicates.Add(v => v.Asset.BusinessUnit != null && businessUnits.Contains(v.Asset.BusinessUnit));
        }

        if (grants.AssetCodes.Count > 0)
        {
            var assetCodes = grants.AssetCodes;
            predicates.Add(v => assetCodes.Contains(v.Asset.AssetCode));
        }

        if (grants.UserIdForResponsibility is { } responsibleUserId)
        {
            predicates.Add(v => v.AssignedToUserId == responsibleUserId
                                || v.Asset.Ownerships.Any(o => o.OwnerUserId == responsibleUserId));
        }

        return source.Where(Or(predicates));
    }

    /// <summary>
    /// กรอง vulnerability finding ตาม scope ผ่าน asset ที่ finding อ้างถึง (<see cref="VulnerabilityFinding.Asset"/>).
    /// finding ที่ไม่มี asset (ข้อมูลเก่า) มองเห็นได้เฉพาะ <c>Organization:*</c> (fail-closed).
    /// Responsibility = เป็น owner ของ asset หรือถูก assign instance ที่ finding ผูกอยู่.
    /// </summary>
    public static IQueryable<VulnerabilityFinding> ApplyDataScope(
        this IQueryable<VulnerabilityFinding> source, ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(user);

        var grants = Grants.From(user);
        if (grants.GrantsNothing)
        {
            return source.Where(f => false);
        }

        if (grants.GrantsEverything)
        {
            return source;
        }

        var predicates = new List<Expression<Func<VulnerabilityFinding, bool>>>();

        if (grants.BusinessUnits.Count > 0)
        {
            var businessUnits = grants.BusinessUnits;
            predicates.Add(f => f.Asset != null && f.Asset.BusinessUnit != null && businessUnits.Contains(f.Asset.BusinessUnit));
        }

        if (grants.AssetCodes.Count > 0)
        {
            var assetCodes = grants.AssetCodes;
            predicates.Add(f => f.Asset != null && assetCodes.Contains(f.Asset.AssetCode));
        }

        if (grants.UserIdForResponsibility is { } responsibleUserId)
        {
            predicates.Add(f => (f.Asset != null && f.Asset.Ownerships.Any(o => o.OwnerUserId == responsibleUserId))
                                || (f.Instance != null && f.Instance.AssignedToUserId == responsibleUserId));
        }

        return source.Where(Or(predicates));
    }

    /// <summary>
    /// กรอง import batch: batch ไม่ผูก asset เดียว (รวมหลาย BU) จึงเห็นทุก batch ได้เฉพาะ <c>Organization:*</c>;
    /// ผู้ใช้ scope แคบเห็นเฉพาะ batch ที่ตนนำเข้าเอง. ไม่ authenticated/ไม่มี grant = ไม่เห็นอะไร (fail-closed).
    /// </summary>
    public static IQueryable<ImportBatch> ApplyDataScope(this IQueryable<ImportBatch> source, ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(user);

        var grants = Grants.From(user);
        if (grants.GrantsNothing || user.UserId is not { } userId)
        {
            return source.Where(b => false);
        }

        return grants.GrantsEverything ? source : source.Where(b => b.ImportedBy == userId);
    }

    /// <summary>true เมื่อผู้ใช้มี <c>Organization:*</c> (ใช้กับงานที่ไม่สามารถกรองตาม scope ได้อย่างมีความหมาย).</summary>
    public static bool IsOrganizationWide(ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Grants.From(user).GrantsEverything;
    }

    private static Expression<Func<T, bool>> Or<T>(IReadOnlyList<Expression<Func<T, bool>>> predicates)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        Expression? body = null;

        foreach (var predicate in predicates)
        {
            var rewritten = new ParameterReplacer(predicate.Parameters[0], parameter).Visit(predicate.Body)!;
            body = body is null ? rewritten : Expression.OrElse(body, rewritten);
        }

        return Expression.Lambda<Func<T, bool>>(body ?? Expression.Constant(false), parameter);
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }

    private sealed class Grants
    {
        public bool GrantsEverything { get; private init; }
        public List<string> BusinessUnits { get; private init; } = [];
        public List<string> AssetCodes { get; private init; } = [];
        public Guid? UserIdForResponsibility { get; private init; }

        public bool GrantsNothing
            => !GrantsEverything
               && BusinessUnits.Count == 0
               && AssetCodes.Count == 0
               && UserIdForResponsibility is null;

        public static Grants From(ICurrentUser user)
        {
            if (!user.IsAuthenticated)
            {
                return new Grants();
            }

            var everything = false;
            var businessUnits = new List<string>();
            var assetCodes = new List<string>();
            var responsibility = false;

            foreach (var scope in user.Scopes)
            {
                var value = scope.Value?.Trim();
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (Is(scope.Type, OrganizationType))
                {
                    // ต้องเป็น Organization:* อย่างชัดเจนเท่านั้น
                    everything |= value == OrganizationAll;
                }
                else if (Is(scope.Type, BusinessUnitType))
                {
                    businessUnits.Add(value);
                }
                else if (Is(scope.Type, AssetScopeType))
                {
                    assetCodes.Add(value);
                }
                else if (Is(scope.Type, ResponsibilityType))
                {
                    responsibility = true;
                }
            }

            return new Grants
            {
                GrantsEverything = everything,
                BusinessUnits = businessUnits.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                AssetCodes = assetCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                UserIdForResponsibility = responsibility ? user.UserId : null,
            };
        }

        private static bool Is(string? actual, string expected)
            => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}
