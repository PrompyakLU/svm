using System.Globalization;
using SvmApi.Application.Common;
using SvmApi.Application.Sla;
using SvmApi.Domain.Sla;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Application.Dashboard;

/// <summary>ตัวกรองสถานะ SLA ของรายการ (Met = SLA หยุดนับแล้วและไม่เกินกำหนด).</summary>
public enum SlaFilter
{
    OnTrack,
    NearDue,
    Breached,
    Met,
}

/// <summary>field ที่อนุญาตให้เรียงลำดับ (allowlist — ห้ามสร้าง ORDER BY จากสตริงของผู้ใช้).</summary>
public enum InstanceSortField
{
    RiskScore,
    FirstDetectedAt,
    LastDetectedAt,
    Status,
    Title,
    SlaDueDate,
    AssetCode,
    BusinessUnit,
}

public sealed record InstanceSortKey(InstanceSortField Field, bool Descending);

/// <summary>query ที่ validate/normalize แล้ว. วันที่ "To" เป็น exclusive upper bound (UTC).</summary>
public sealed record ParsedInstanceQuery(
    string? Search,
    IReadOnlyList<VulnerabilityStatus> Statuses,
    IReadOnlyList<RiskLevel> RiskLevels,
    string? Severity,
    IReadOnlyList<SlaFilter> SlaStatuses,
    string? BusinessUnit,
    string? AssetCode,
    Guid? AssetId,
    Guid? AssignedToUserId,
    string? CveId,
    bool? IsKev,
    bool? InternetExposure,
    bool OpenOnly,
    bool? Unassigned,
    bool AssignedToMe,
    DateTime? DetectedFrom,
    DateTime? DetectedToExclusive,
    DateTime? DueFrom,
    DateTime? DueToExclusive,
    int? AgeDaysMin,
    int? AgeDaysMax,
    IReadOnlyList<InstanceSortKey> Sort,
    int Page,
    int PageSize);

/// <summary>
/// กติกาล้วน (ไม่แตะ DB) ของ <c>GET /instances</c>: parse + validate filter, allowlist การเรียงลำดับ, ขอบเขต paging.
/// ทุกค่าที่ผิดรูปแบบเก็บเป็น field error (400) แทนการเดา/ตัดทิ้งเงียบ ๆ.
/// </summary>
public static class InstanceQueryRules
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
    public const int MaxPage = 100_000;
    public const int MaxSearchLength = 100;
    public const int MaxTextFilterLength = 200;
    public const int MaxSortKeys = 3;
    public const int MaxDateRangeDays = 366;
    public const int MaxAgeDays = 3650;

    /// <summary>เพดานจำนวนแถวของ list ย่อยใน detail.</summary>
    public const int MaxHistoryRows = 100;

    public const int MaxEvidenceRows = 100;
    public const int MaxCommentRows = 100;
    public const int MaxVerificationRows = 50;

    public static readonly IReadOnlyList<InstanceSortKey> DefaultSort = [new(InstanceSortField.RiskScore, true)];

    /// <summary>สถานะที่นับว่า "ยังเปิด" = สถานะที่ SLA ยังเดินอยู่ (ตรงกับ <c>SlaCalculator.IsTimerRunning</c>).</summary>
    public static bool IsOpen(VulnerabilityStatus status) => !ClosedStatuses.Contains(status);

    /// <summary>สถานะปิด/ยกเว้น (Resolved, Closed, FalsePositive, RiskAccepted, ExceptionApproved, Archived).</summary>
    public static readonly IReadOnlyList<VulnerabilityStatus> ClosedStatuses =
    [
        VulnerabilityStatus.Resolved,
        VulnerabilityStatus.Closed,
        VulnerabilityStatus.FalsePositive,
        VulnerabilityStatus.RiskAccepted,
        VulnerabilityStatus.ExceptionApproved,
        VulnerabilityStatus.Archived,
    ];

    // alias ของ sort field (ตัวพิมพ์ไม่สำคัญ) -> field ที่อนุญาต; api-spec ใช้ detectedAt
    private static readonly IReadOnlyDictionary<string, InstanceSortField> SortAllowlist =
        new Dictionary<string, InstanceSortField>(StringComparer.OrdinalIgnoreCase)
        {
            ["riskScore"] = InstanceSortField.RiskScore,
            ["detectedAt"] = InstanceSortField.FirstDetectedAt,
            ["firstDetectedAt"] = InstanceSortField.FirstDetectedAt,
            ["lastDetectedAt"] = InstanceSortField.LastDetectedAt,
            ["status"] = InstanceSortField.Status,
            ["title"] = InstanceSortField.Title,
            ["slaDueDate"] = InstanceSortField.SlaDueDate,
            ["assetCode"] = InstanceSortField.AssetCode,
            ["businessUnit"] = InstanceSortField.BusinessUnit,
        };

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
    ];

    public static string AllowedSortFields => string.Join(", ", SortAllowlist.Keys);

    // ------------------------------------------------------------------ public

    public static RuleOutcome<ParsedInstanceQuery> Validate(InstanceListQuery? query)
    {
        query ??= new InstanceListQuery();
        var errors = new ErrorBag();

        if (query.Page < 1 || query.Page > MaxPage)
        {
            errors.Add("page", $"page must be between 1 and {MaxPage}.");
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"pageSize must be between 1 and {MaxPageSize}.");
        }

        var search = CleanText(errors, "q", query.Search, MaxSearchLength);
        var severity = CleanText(errors, "severity", query.Severity, 30);
        var businessUnit = CleanText(errors, "businessUnit", query.BusinessUnit, MaxTextFilterLength);
        var assetCode = CleanText(errors, "assetCode", query.AssetCode, MaxTextFilterLength);
        var cve = CleanText(errors, "cve", query.Cve, 30)?.ToUpperInvariant();

        var statuses = ParseEnumList<VulnerabilityStatus>(errors, "status", query.Status, 20, alias: null);
        var risks = ParseEnumList<RiskLevel>(errors, "riskLevel", query.RiskLevel, 8, RiskAlias);
        var slas = ParseEnumList<SlaFilter>(errors, "slaStatus", query.SlaStatus, 8, alias: null);

        var assetId = ParseGuid(errors, "assetId", query.AssetId);
        var assignedTo = ParseGuid(errors, "assignedTo", query.AssignedTo);

        var isKev = ParseBool(errors, "isKev", query.IsKev);
        var exposure = ParseBool(errors, "internetExposure", query.InternetExposure);
        var openOnly = ParseBool(errors, "openOnly", query.OpenOnly) ?? false;
        var unassigned = ParseBool(errors, "unassigned", query.Unassigned);
        var mine = ParseBool(errors, "assignedToMe", query.AssignedToMe) ?? false;

        if (assignedTo is not null && (unassigned == true || mine))
        {
            errors.Add("assignedTo", "assignedTo cannot be combined with unassigned or assignedToMe.");
        }

        if (unassigned == true && mine)
        {
            errors.Add("unassigned", "unassigned cannot be combined with assignedToMe.");
        }

        var detectedFrom = ParseDate(errors, "detectedFrom", query.DetectedFrom, isUpper: false);
        var detectedTo = ParseDate(errors, "detectedTo", query.DetectedTo, isUpper: true);
        var dueFrom = ParseDate(errors, "dueFrom", query.DueFrom, isUpper: false);
        var dueTo = ParseDate(errors, "dueTo", query.DueTo, isUpper: true);
        CheckRange(errors, "detectedFrom", detectedFrom, detectedTo);
        CheckRange(errors, "dueFrom", dueFrom, dueTo);

        var ageMin = ParseAge(errors, "ageDaysMin", query.AgeDaysMin);
        var ageMax = ParseAge(errors, "ageDaysMax", query.AgeDaysMax);
        if (ageMin is { } lo && ageMax is { } hi && lo > hi)
        {
            errors.Add("ageDaysMin", "ageDaysMin must not exceed ageDaysMax.");
        }

        var sort = ParseSort(errors, query.Sort);

        if (errors.HasErrors)
        {
            return RuleOutcome<ParsedInstanceQuery>.Invalid(errors.ToDictionary());
        }

        return RuleOutcome<ParsedInstanceQuery>.Ok(new ParsedInstanceQuery(
            search, statuses, risks, severity, slas, businessUnit, assetCode, assetId, assignedTo, cve,
            isKev, exposure, openOnly, unassigned, mine,
            detectedFrom, detectedTo, dueFrom, dueTo, ageMin, ageMax, sort, query.Page, query.PageSize));
    }

    /// <summary>
    /// <c>sort=riskScore:desc,detectedAt:asc</c> — เฉพาะ field ใน allowlist, ไม่ซ้ำ, สูงสุด <see cref="MaxSortKeys"/> key.
    /// ไม่ระบุ direction = asc. ว่าง = <see cref="DefaultSort"/>.
    /// </summary>
    public static IReadOnlyList<InstanceSortKey> ParseSort(ErrorBag errors, string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return DefaultSort;
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Length > MaxSortKeys)
        {
            errors.Add("sort", $"sort must contain 1 to {MaxSortKeys} keys.");
            return DefaultSort;
        }

        var keys = new List<InstanceSortKey>();
        foreach (var part in parts)
        {
            var pair = part.Split(':', StringSplitOptions.TrimEntries);
            if (pair.Length is < 1 or > 2 || !SortAllowlist.TryGetValue(pair[0], out var field))
            {
                errors.Add("sort", $"sort field must be one of: {AllowedSortFields}.");
                return DefaultSort;
            }

            var descending = false;
            if (pair.Length == 2)
            {
                if (string.Equals(pair[1], "desc", StringComparison.OrdinalIgnoreCase))
                {
                    descending = true;
                }
                else if (!string.Equals(pair[1], "asc", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("sort", "sort direction must be 'asc' or 'desc'.");
                    return DefaultSort;
                }
            }

            if (keys.Any(k => k.Field == field))
            {
                errors.Add("sort", "sort must not repeat a field.");
                return DefaultSort;
            }

            keys.Add(new InstanceSortKey(field, descending));
        }

        return keys;
    }

    /// <summary>จำนวนแถวที่ต้องข้าม (offset) — null เมื่อเกินช่วง int (ตอบหน้าว่าง).</summary>
    public static int? ComputeSkip(int page, int pageSize)
    {
        var skip = (long)(page - 1) * pageSize;
        return skip > int.MaxValue ? null : (int)skip;
    }

    /// <summary>รหัสที่แสดงให้คนอ่าน (instance ไม่มีคอลัมน์ code) — มาจาก id จึงคงที่เสมอ.</summary>
    public static string DisplayCode(Guid id) => "VUL-" + id.ToString("N")[..8].ToUpperInvariant();

    /// <summary>อายุ (วันเต็ม) นับจากพบครั้งแรก; อนาคต = 0.</summary>
    public static int AgeDays(DateTime firstDetectedAt, DateTime now)
    {
        var span = now - firstDetectedAt;
        return span <= TimeSpan.Zero ? 0 : (int)Math.Min(int.MaxValue, Math.Floor(span.TotalDays));
    }

    /// <summary>
    /// สถานะ SLA สำหรับแสดงผล: Running = คำนวณสดด้วย <see cref="SlaCalculator.EvaluateBreach"/>;
    /// หยุดนับ = Breached ถ้าเคยเกิน, ไม่เช่นนั้น Met.
    /// </summary>
    public static SlaSummaryDto BuildSla(
        string policyName,
        VulnerabilityStatus instanceStatus,
        DateTime startedAt,
        DateTime resolutionDue,
        SlaBreachStatus storedBreach,
        DateTime now,
        double nearDueFraction)
    {
        var running = SlaCalculator.IsTimerRunning(instanceStatus);
        var started = SlaCalculator.AsUtc(startedAt);
        var due = SlaCalculator.AsUtc(resolutionDue);

        string breach;
        if (running)
        {
            breach = SlaCalculator.EvaluateBreach(started, due, now, nearDueFraction).ToString();
        }
        else
        {
            breach = storedBreach == SlaBreachStatus.Breached ? nameof(SlaFilter.Breached) : nameof(SlaFilter.Met);
        }

        var remainingHours = Math.Round((due - now).TotalHours, 1);
        return new SlaSummaryDto(
            policyName, started, due, breach, remainingHours, running ? SlaCalculator.DaysOverdue(due, now) : 0, running);
    }

    /// <summary>ชื่อ policy สำหรับแสดงผล เช่น <c>Critical-7d</c> (ResolveHours / 24 ปัดขึ้น) — policy ไม่มีคอลัมน์ name.</summary>
    public static string PolicyName(RiskLevel level, int resolveHours)
        => $"{level}-{Math.Max(1, (int)Math.Ceiling(resolveHours / 24.0))}d";

    // ------------------------------------------------------------------ parsing helpers

    private static readonly IReadOnlyDictionary<string, string> RiskAlias =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Info"] = nameof(RiskLevel.Informational),
        };

    private static string? CleanText(ErrorBag errors, string field, string? raw, int maxLength)
    {
        var value = TextSanitizer.Clean(raw, allowLineBreaks: false);
        if (value.Length == 0)
        {
            return null;
        }

        if (value.Length > maxLength)
        {
            errors.Add(field, $"{field} must not exceed {maxLength} characters.");
            return null;
        }

        return value;
    }

    private static IReadOnlyList<T> ParseEnumList<T>(
        ErrorBag errors, string field, string? raw, int maxItems, IReadOnlyDictionary<string, string>? alias)
        where T : struct, Enum
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        var items = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (items.Length == 0 || items.Length > maxItems)
        {
            errors.Add(field, $"{field} must contain 1 to {maxItems} values.");
            return [];
        }

        var result = new List<T>();
        foreach (var item in items)
        {
            var name = alias is not null && alias.TryGetValue(item, out var mapped) ? mapped : item;

            // ชื่อ enum เท่านั้น — ไม่รับตัวเลข (กัน Enum.TryParse("7") และค่านอกนิยาม)
            if (char.IsLetter(name[0]) && Enum.TryParse<T>(name, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            {
                if (!result.Contains(parsed))
                {
                    result.Add(parsed);
                }

                continue;
            }

            errors.Add(field, $"{field} must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
            return [];
        }

        return result;
    }

    private static Guid? ParseGuid(ErrorBag errors, string field, string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (Guid.TryParse(value, out var id) && id != Guid.Empty)
        {
            return id;
        }

        errors.Add(field, $"{field} must be a valid GUID.");
        return null;
    }

    private static bool? ParseBool(ErrorBag errors, string field, string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        errors.Add(field, $"{field} must be 'true' or 'false'.");
        return null;
    }

    /// <summary>
    /// รับ <c>yyyy-MM-dd</c> หรือ ISO-8601 (UTC เป็นค่าเริ่มต้น). ขอบบน (isUpper) แบบวันที่ล้วนรวมทั้งวัน
    /// (คืน exclusive = วันถัดไป 00:00); แบบมีเวลารวมค่านั้นพอดี (exclusive = +1 tick).
    /// </summary>
    private static DateTime? ParseDate(ErrorBag errors, string field, string? raw, bool isUpper)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (!DateTime.TryParseExact(
                value, DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            errors.Add(field, $"{field} must be an ISO-8601 date (yyyy-MM-dd) or date-time.");
            return null;
        }

        var utc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        if (!isUpper)
        {
            return utc;
        }

        return value.Length == 10 ? utc.AddDays(1) : utc.AddTicks(1);
    }

    private static void CheckRange(ErrorBag errors, string field, DateTime? from, DateTime? toExclusive)
    {
        if (from is not { } start || toExclusive is not { } end)
        {
            return;
        }

        if (end <= start)
        {
            errors.Add(field, "The range start must be before the range end.");
        }
        else if ((end - start).TotalDays > MaxDateRangeDays + 1)
        {
            errors.Add(field, $"The date range must not exceed {MaxDateRangeDays} days.");
        }
    }

    private static int? ParseAge(ErrorBag errors, string field, string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days <= MaxAgeDays)
        {
            return days;
        }

        errors.Add(field, $"{field} must be a whole number between 0 and {MaxAgeDays}.");
        return null;
    }
}

/// <summary>ผล validation ของ rule ล้วน: ค่า หรือ field errors.</summary>
public sealed record RuleOutcome<T>(T? Value, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static RuleOutcome<T> Ok(T value) => new(value, new Dictionary<string, string[]>());

    public static RuleOutcome<T> Invalid(IReadOnlyDictionary<string, string[]> errors) => new(default, errors);
}

/// <summary>เก็บ validation error แบบ field -> messages (รูปแบบเดียวกับ <c>ValidationProblemDetails.errors</c>).</summary>
public sealed class ErrorBag
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool HasErrors => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            list = [];
            _errors[field] = list;
        }

        list.Add(message);
    }

    public IReadOnlyDictionary<string, string[]> ToDictionary()
        => _errors.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
}
