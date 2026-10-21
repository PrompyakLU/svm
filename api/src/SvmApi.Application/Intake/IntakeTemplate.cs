using System.Text;

namespace SvmApi.Application.Intake;

/// <summary>
/// ข้อจำกัดการอัปโหลด (bind จาก section <c>Intake</c>). ค่าเกิน hard cap จะถูก clamp ด้วย <see cref="EffectiveMaxFileBytes"/>/<see cref="EffectiveMaxRows"/>.
/// </summary>
public sealed class IntakeOptions
{
    public const string SectionName = "Intake";

    /// <summary>เพดานขนาด request ของ endpoint import (ค่าคงที่สำหรับ RequestSizeLimit) — 20 MB.</summary>
    public const long HardMaxFileBytes = 20L * 1024 * 1024;

    public const int HardMaxRows = 50_000;

    /// <summary>ขนาดไฟล์สูงสุด (byte) — default 5 MB (EX-12).</summary>
    public long MaxFileBytes { get; set; } = 5L * 1024 * 1024;

    /// <summary>จำนวนแถวข้อมูลสูงสุดต่อไฟล์ — default 5,000.</summary>
    public int MaxRows { get; set; } = 5_000;

    /// <summary>จำนวน error รายแถวสูงสุดที่เก็บ/ตอบกลับ (กัน response/errorDetail ใหญ่เกิน).</summary>
    public int MaxReportedErrors { get; set; } = 200;

    public long EffectiveMaxFileBytes => Math.Clamp(MaxFileBytes, 1, HardMaxFileBytes);

    public int EffectiveMaxRows => Math.Clamp(MaxRows, 1, HardMaxRows);

    public int EffectiveMaxReportedErrors => Math.Clamp(MaxReportedErrors, 1, 1_000);
}

/// <summary>ชนิดข้อมูลของ column ใน template (ตรงกับ validation ของ <see cref="FindingValidator"/>).</summary>
public static class IntakeColumnTypes
{
    public const string Text = "string";
    public const string Integer = "integer";
    public const string Decimal = "decimal";
    public const string DateTime = "datetime";
    public const string Enum = "enum";
}

/// <summary>นิยาม column หนึ่งของ import template.</summary>
public sealed record IntakeColumn(
    string Name,
    bool Required,
    string Type,
    int? MaxLength,
    string Description,
    string Example,
    IReadOnlyList<string>? AllowedValues = null);

/// <summary>
/// Template กลาง (US-009 · FR-08): แหล่งความจริงเดียวของชื่อ column/mandatory/ชนิดข้อมูล — ทั้ง parser, validator
/// และไฟล์ template ที่ดาวน์โหลดใช้รายการนี้ จึงตรงกับ validation ปัจจุบันเสมอ.
/// </summary>
public static class IntakeTemplate
{
    public const string AssetCode = "assetCode";
    public const string SourceSystem = "sourceSystem";
    public const string SourceRecordId = "sourceRecordId";
    public const string FindingName = "findingName";
    public const string Severity = "severity";
    public const string CveId = "cveId";
    public const string CweId = "cweId";
    public const string CvssVersion = "cvssVersion";
    public const string CvssBaseScore = "cvssBaseScore";
    public const string CvssVector = "cvssVector";
    public const string Location = "location";
    public const string Port = "port";
    public const string Component = "component";
    public const string DetectedAt = "detectedAt";

    public static IReadOnlyList<IntakeColumn> Columns { get; } =
    [
        new(AssetCode, true, IntakeColumnTypes.Text, FindingLimits.AssetCode, "รหัส asset ที่มีอยู่ในระบบ (ใน data scope ของผู้นำเข้า)", "SVM-APP-001"),
        new(SourceSystem, true, IntakeColumnTypes.Text, FindingLimits.SourceSystem, "ชื่อแหล่งที่มา/scanner", "PentestTeam"),
        new(SourceRecordId, false, IntakeColumnTypes.Text, FindingLimits.SourceRecordId, "รหัสรายการในระบบต้นทาง (traceability)", "PT-2026-001"),
        new(FindingName, true, IntakeColumnTypes.Text, FindingLimits.FindingName, "ชื่อช่องโหว่", "SQL Injection in login form"),
        new(Severity, true, IntakeColumnTypes.Enum, FindingLimits.Severity, "ระดับความรุนแรง (รับคำพ้อง/ตัวเลข 0–5; แปลงเป็นค่ามาตรฐาน)", "High", SeverityNormalizer.CanonicalValues),
        new(CveId, false, IntakeColumnTypes.Text, FindingLimits.CveId, "CVE-YYYY-NNNN", "CVE-2021-44228"),
        new(CweId, false, IntakeColumnTypes.Text, FindingLimits.CweId, "CWE-NNN หรือเลขล้วน", "CWE-89"),
        new(CvssVersion, false, IntakeColumnTypes.Text, FindingLimits.CvssVersion, "เช่น 3.1 / 4.0 (อนุมานจาก vector ได้)", "3.1"),
        new(CvssBaseScore, false, IntakeColumnTypes.Decimal, null, "0.0–10.0 ทศนิยม 1 ตำแหน่ง", "9.8"),
        new(CvssVector, false, IntakeColumnTypes.Text, FindingLimits.CvssVector, "CVSS:x.y/...", "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H"),
        new(Location, false, IntakeColumnTypes.Text, FindingLimits.Location, "URL/path/host ที่พบ", "https://app.example/login"),
        new(Port, false, IntakeColumnTypes.Integer, null, "0–65535", "443"),
        new(Component, false, IntakeColumnTypes.Text, FindingLimits.Component, "package/library/parameter", "log4j-core@2.14.1"),
        new(DetectedAt, false, IntakeColumnTypes.DateTime, null, "ISO 8601 (UTC ถ้าไม่ระบุ offset); ว่าง = เวลานำเข้า", "2026-10-01T08:00:00Z"),
    ];

    public static IReadOnlySet<string> Names { get; } =
        Columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> RequiredNames { get; } =
        Columns.Where(c => c.Required).Select(c => c.Name).ToList();

    /// <summary>หา column จากชื่อ (ไม่สนตัวพิมพ์/ช่องว่างหัวท้าย); null ถ้าไม่รู้จัก.</summary>
    public static string? Canonical(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        var h = header.Trim();
        return Columns.FirstOrDefault(c => string.Equals(c.Name, h, StringComparison.OrdinalIgnoreCase))?.Name;
    }

    /// <summary>
    /// CSV template (header + ตัวอย่าง 1 แถว) แบบ UTF-8 พร้อม BOM ให้ Excel เปิดภาษาไทยได้.
    /// ทุก cell ผ่าน <see cref="CsvSafety.EscapeCell"/> (กัน formula injection).
    /// </summary>
    public static byte[] BuildCsv()
    {
        var sb = new StringBuilder();
        sb.AppendJoin(',', Columns.Select(c => CsvSafety.EscapeCell(c.Name))).Append("\r\n");
        sb.AppendJoin(',', Columns.Select(c => CsvSafety.EscapeCell(c.Example))).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    /// <summary>JSON template: นิยาม column + ตัวอย่างไฟล์ที่นำเข้าได้ (<c>{ "findings": [ ... ] }</c>).</summary>
    public static object BuildJson() => new
    {
        format = "Upload either a JSON array of finding objects or an object with a 'findings' array.",
        columns = Columns.Select(c => new
        {
            name = c.Name,
            required = c.Required,
            type = c.Type,
            maxLength = c.MaxLength,
            allowedValues = c.AllowedValues,
            description = c.Description,
        }),
        example = new
        {
            findings = new[] { Columns.ToDictionary(c => c.Name, c => c.Example) },
        },
    };
}

/// <summary>ความยาวสูงสุดของ field (ตรงกับ column ใน data-model.md / EF mapping).</summary>
public static class FindingLimits
{
    public const int AssetCode = 100;
    public const int SourceSystem = 100;
    public const int SourceRecordId = 200;
    public const int FindingName = 500;
    public const int Severity = 20;
    public const int CveId = Reference.ReferenceIds.MaxCveIdLength;
    public const int CweId = Reference.ReferenceIds.MaxCweIdLength;
    public const int CvssVersion = Reference.ReferenceIds.MaxCvssVersionLength;
    public const int CvssVector = Reference.CvssVectorValidator.MaxVectorLength;
    public const int Location = 500;
    public const int Component = 300;
    public const int FileName = 300;
    public const int TransactionId = 200;
    public const int ResolveReason = 1000;
}

/// <summary>
/// กัน CSV/spreadsheet formula injection (OWASP): cell ที่ขึ้นต้นด้วย <c>= + - @</c> หรือ tab/CR ถูกเติม <c>'</c> นำหน้า
/// และทุก cell ถูก quote ตาม RFC 4180 เมื่อจำเป็น. ใช้กับ template และ export ทุกจุด.
/// </summary>
public static class CsvSafety
{
    public static bool IsFormulaLike(string? value)
        => !string.IsNullOrEmpty(value) && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r';

    public static string EscapeCell(string? value)
    {
        var v = value ?? string.Empty;
        if (IsFormulaLike(v))
        {
            v = "'" + v;
        }

        var needsQuote = v.IndexOfAny([',', '"', '\r', '\n']) >= 0 || v.StartsWith(' ') || v.EndsWith(' ');
        return needsQuote ? "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : v;
    }
}
