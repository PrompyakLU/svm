using System.Globalization;
using SvmApi.Domain.Assets;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Application.Admin;

/// <summary>master data ที่ parse แล้ว: <c>Code</c> เป็นชื่อมาตรฐาน (version หรือ yyyy-MM-dd).</summary>
public sealed record MasterDataDraft(string Code, string? Description, bool IsActive);

/// <summary>
/// กติกา master data ฝั่ง admin (FR-64 · BR-40) — pure ไม่แตะ DB.
/// ชนิดที่แก้ไขได้: <c>cvss-versions</c> (version รูป <c>M.m</c>) และ <c>business-calendar</c> (วันที่ <c>yyyy-MM-dd</c> ช่วง
/// <see cref="MinYear"/>–<see cref="MaxYear"/>). ชนิด <c>severity</c>/<c>asset-type</c>/<c>asset-criticality</c> เป็น enum ของระบบ — อ่านอย่างเดียว.
/// </summary>
public static class MasterDataAdminRules
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;
    public const int MaxDescriptionLength = 200;
    public const int MaxVersionLength = 10;
    public const int MaxCalendarRangeDays = 800;

    public static bool TryNormalizeType(string? type, out string normalized)
    {
        var normalizedType = (type ?? string.Empty).Trim().ToLowerInvariant();
        normalized = normalizedType;
        return MasterDataTypes.All.Any(t => string.Equals(t.Type, normalizedType, StringComparison.Ordinal));
    }

    public static bool IsReadOnly(string normalizedType)
        => MasterDataTypes.All.Any(t => t.ReadOnly && string.Equals(t.Type, normalizedType, StringComparison.Ordinal));

    /// <summary>รายการค่าคงที่ของชนิด read-only (ชื่อ enum) — เรียงตามลำดับนิยาม.</summary>
    public static IReadOnlyList<string> EnumValues(string normalizedType) => normalizedType switch
    {
        MasterDataTypes.Severity => Enum.GetNames<RiskLevel>(),
        MasterDataTypes.AssetType => Enum.GetNames<AssetType>(),
        MasterDataTypes.AssetCriticality => Enum.GetNames<AssetCriticality>(),
        _ => [],
    };

    /// <summary>
    /// แปลงคำขอสร้าง/แก้. <paramref name="isUpdate"/>: PUT ต้องส่ง isActive; code ที่ส่งมาต้องตรงกับของเดิม (ตรวจโดยผู้เรียก — แก้ code ไม่ได้).
    /// </summary>
    public static (MasterDataDraft? Value, AdminValidation Validation) Parse(string normalizedType, MasterDataRequest? request, bool isUpdate)
    {
        var issues = new List<AdminIssue>();
        if (request is null)
        {
            issues.Add(new AdminIssue("body", "REQUIRED"));
            return (null, AdminValidation.From(issues));
        }

        string? code = null;
        var rawCode = AdminParse.CleanText(request.Code);
        if (rawCode is null)
        {
            if (!isUpdate)
            {
                issues.Add(new AdminIssue("code", "REQUIRED"));
            }
        }
        else
        {
            code = NormalizeCode(normalizedType, rawCode);
            if (code is null)
            {
                issues.Add(new AdminIssue("code", normalizedType == MasterDataTypes.BusinessCalendar ? "INVALID_DATE" : "INVALID_VERSION"));
            }
        }

        var description = AdminParse.CleanText(request.Description);
        if (description is { Length: > MaxDescriptionLength })
        {
            issues.Add(new AdminIssue("description", "TOO_LONG"));
        }

        if (isUpdate && request.IsActive is null)
        {
            issues.Add(new AdminIssue("isActive", "REQUIRED"));
        }

        if (issues.Count > 0)
        {
            return (null, AdminValidation.From(issues));
        }

        return (new MasterDataDraft(code ?? string.Empty, description, request.IsActive ?? true), AdminValidation.Valid);
    }

    /// <summary>ชื่อมาตรฐานของ code หรือ null เมื่อรูปแบบผิด.</summary>
    public static string? NormalizeCode(string normalizedType, string code)
    {
        var trimmed = code.Trim();
        switch (normalizedType)
        {
            case MasterDataTypes.CvssVersions:
                return IsCvssVersion(trimmed) ? trimmed : null;

            case MasterDataTypes.BusinessCalendar:
                return TryParseDate(trimmed, out var date) ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

            default:
                return null;
        }
    }

    /// <summary>รูปแบบ <c>M.m</c> (ตัวเลข 1–2 หลัก คั่นด้วยจุด) เช่น 3.1, 4.0.</summary>
    public static bool IsCvssVersion(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxVersionLength)
        {
            return false;
        }

        var parts = value.Split('.');
        return parts.Length == 2
               && parts.All(p => p.Length is >= 1 and <= 2 && p.All(char.IsAsciiDigit));
    }

    public static bool TryParseDate(string value, out DateOnly date)
        => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
           && date.Year is >= MinYear and <= MaxYear;

    /// <summary>ช่วงวันที่ของ GET business-calendar: ไม่ส่ง = ปีปัจจุบันทั้งปี; from ≤ to และกว้างไม่เกิน <see cref="MaxCalendarRangeDays"/> วัน.</summary>
    public static (DateOnly From, DateOnly To, AdminValidation Validation) ResolveCalendarRange(DateOnly? from, DateOnly? to, DateOnly today)
    {
        var start = from ?? new DateOnly(today.Year, 1, 1);
        var end = to ?? (from is null ? new DateOnly(today.Year, 12, 31) : start.AddDays(364));

        var issues = new List<AdminIssue>();
        if (end < start)
        {
            issues.Add(new AdminIssue("to", "RANGE_INVALID"));
        }
        else if (end.DayNumber - start.DayNumber + 1 > MaxCalendarRangeDays)
        {
            issues.Add(new AdminIssue("to", $"RANGE_TOO_LARGE: max {MaxCalendarRangeDays} days"));
        }

        return (start, end, AdminValidation.From(issues));
    }
}
