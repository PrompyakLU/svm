using System.Globalization;
using System.Text;
using SvmApi.Application.Reference;

namespace SvmApi.Application.Intake;

/// <summary>
/// Common data model (US-011 · FR-10): finding หลัง validate + normalize แล้ว — ค่าทุกตัว trim/มาตรฐาน,
/// severity เป็นค่ามาตรฐานของระบบ, เวลาเป็น UTC. asset ยังเป็น assetCode (resolve เป็น id ใน DataQualityService).
/// </summary>
public sealed record NormalizedFinding(
    string AssetCode,
    string SourceSystem,
    string? SourceRecordId,
    string FindingName,
    string Severity,
    string? CveId,
    string? CweId,
    string? CvssVersion,
    decimal? CvssBaseScore,
    string? CvssVector,
    string? Location,
    int? Port,
    string? Component,
    DateTime DetectedAt);

public sealed record FindingValidation(NormalizedFinding? Value, IReadOnlyList<FieldError> Errors)
{
    public bool IsValid => Value is not null && Errors.Count == 0;
}

/// <summary>
/// Validation (US-010 · FR-09 · EX-15/16) + normalization (US-011 · FR-10) แบบ pure:
/// mandatory (assetCode/sourceSystem/findingName/severity), ความยาวตาม column, ชนิดข้อมูล (port/score/date),
/// รูปแบบ identifier (CVE/CWE), ห้าม control character. Reference lookup (มีใน master หรือไม่) ทำใน DataQualityService.
/// </summary>
public static class FindingValidator
{
    /// <summary>detectedAt ในอนาคตเกินค่านี้ถือว่าผิด (เผื่อ clock skew ของ scanner).</summary>
    public static readonly TimeSpan MaxFutureSkew = TimeSpan.FromDays(1);

    public static readonly DateTime MinDetectedAt = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static FindingValidation Validate(RawFindingRow? row, DateTime nowUtc)
    {
        var errors = new List<FieldError>();
        if (row is null)
        {
            foreach (var name in IntakeTemplate.RequiredNames)
            {
                errors.Add(new FieldError(name, IntakeErrorCodes.MissingMandatory));
            }

            return new FindingValidation(null, errors);
        }

        var assetCode = Text(row.AssetCode, IntakeTemplate.AssetCode, FindingLimits.AssetCode, required: true, errors);
        var sourceSystem = Text(row.SourceSystem, IntakeTemplate.SourceSystem, FindingLimits.SourceSystem, required: true, errors);
        var sourceRecordId = Text(row.SourceRecordId, IntakeTemplate.SourceRecordId, FindingLimits.SourceRecordId, required: false, errors);
        var findingName = Text(row.FindingName, IntakeTemplate.FindingName, FindingLimits.FindingName, required: true, errors, collapseWhitespace: true);

        string? severity = null;
        var rawSeverity = Text(row.Severity, IntakeTemplate.Severity, FindingLimits.Severity, required: true, errors);
        if (rawSeverity is not null)
        {
            severity = SeverityNormalizer.Normalize(rawSeverity);
            if (severity is null)
            {
                errors.Add(new FieldError(IntakeTemplate.Severity, IntakeErrorCodes.InvalidSeverity));
            }
        }

        // --- identifiers (EX-16): ผิดรูปแบบ = ปฏิเสธ; รูปแบบถูกแต่ไม่พบใน master = NeedsReview (EX-47, ตัดสินภายหลัง)
        var cveId = ReferenceIds.NormalizeCveId(Text(row.CveId, IntakeTemplate.CveId, 4 * FindingLimits.CveId, required: false, errors));
        if (cveId is not null && !ReferenceIds.IsValidCveId(cveId))
        {
            errors.Add(new FieldError(IntakeTemplate.CveId, IntakeErrorCodes.InvalidIdentifier));
            cveId = null;
        }
        else if (cveId is not null)
        {
            cveId = cveId.ToUpperInvariant();
        }

        var cweId = ReferenceIds.NormalizeCweId(Text(row.CweId, IntakeTemplate.CweId, 4 * FindingLimits.CweId, required: false, errors));
        if (cweId is not null && !ReferenceIds.IsValidCweId(cweId))
        {
            errors.Add(new FieldError(IntakeTemplate.CweId, IntakeErrorCodes.InvalidIdentifier));
            cweId = null;
        }
        else if (cweId is not null)
        {
            cweId = cweId.ToUpperInvariant();
        }

        var cvssVersion = Text(row.CvssVersion, IntakeTemplate.CvssVersion, FindingLimits.CvssVersion, required: false, errors);
        var cvssVector = Text(row.CvssVector, IntakeTemplate.CvssVector, FindingLimits.CvssVector, required: false, errors);

        decimal? score = null;
        var rawScore = Text(row.CvssBaseScore, IntakeTemplate.CvssBaseScore, 16, required: false, errors);
        if (rawScore is not null)
        {
            if (!decimal.TryParse(rawScore, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            {
                errors.Add(new FieldError(IntakeTemplate.CvssBaseScore, IntakeErrorCodes.InvalidType));
            }
            else if (!CvssScore.IsValid(parsed))
            {
                errors.Add(new FieldError(IntakeTemplate.CvssBaseScore, IntakeErrorCodes.InvalidScore));
            }
            else
            {
                score = parsed;
            }
        }

        var location = LocationNormalizer.Normalize(Text(row.Location, IntakeTemplate.Location, FindingLimits.Location, required: false, errors));

        int? port = null;
        var rawPort = Text(row.Port, IntakeTemplate.Port, 16, required: false, errors);
        if (rawPort is not null)
        {
            if (!int.TryParse(rawPort, NumberStyles.None, CultureInfo.InvariantCulture, out var p))
            {
                errors.Add(new FieldError(IntakeTemplate.Port, IntakeErrorCodes.InvalidType));
            }
            else if (p is < 0 or > 65535)
            {
                errors.Add(new FieldError(IntakeTemplate.Port, IntakeErrorCodes.InvalidPort));
            }
            else
            {
                port = p;
            }
        }

        var component = Text(row.Component, IntakeTemplate.Component, FindingLimits.Component, required: false, errors);

        var detectedAt = nowUtc;
        var rawDate = Text(row.DetectedAt, IntakeTemplate.DetectedAt, 64, required: false, errors);
        if (rawDate is not null)
        {
            if (!DateTimeOffset.TryParse(
                    rawDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var dto))
            {
                errors.Add(new FieldError(IntakeTemplate.DetectedAt, IntakeErrorCodes.InvalidType));
            }
            else if (dto.UtcDateTime > nowUtc + MaxFutureSkew || dto.UtcDateTime < MinDetectedAt)
            {
                errors.Add(new FieldError(IntakeTemplate.DetectedAt, IntakeErrorCodes.InvalidDate));
            }
            else
            {
                detectedAt = dto.UtcDateTime;
            }
        }

        if (errors.Count > 0 || assetCode is null || sourceSystem is null || findingName is null || severity is null)
        {
            return new FindingValidation(null, errors);
        }

        return new FindingValidation(
            new NormalizedFinding(
                assetCode, sourceSystem, sourceRecordId, findingName, severity, cveId, cweId, cvssVersion, score, cvssVector,
                location, port, component, DateTime.SpecifyKind(detectedAt, DateTimeKind.Utc)),
            errors);
    }

    /// <summary>trim; ว่าง = null; ตรวจ mandatory/ความยาว/control character. ไม่เก็บค่าดิบใน error.</summary>
    private static string? Text(
        string? value, string field, int maxLength, bool required, List<FieldError> errors, bool collapseWhitespace = false)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v))
        {
            if (required)
            {
                errors.Add(new FieldError(field, IntakeErrorCodes.MissingMandatory));
            }

            return null;
        }

        // ยุบ whitespace (รวม tab/newline) ก่อน แล้วค่อยตรวจ control character ที่เหลือ
        if (collapseWhitespace)
        {
            v = CollapseWhitespace(v);
        }

        if (v.Any(char.IsControl))
        {
            errors.Add(new FieldError(field, IntakeErrorCodes.InvalidFormat));
            return null;
        }

        if (v.Length > maxLength)
        {
            errors.Add(new FieldError(field, IntakeErrorCodes.TooLong));
            return null;
        }

        return v;
    }

    private static string CollapseWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        var lastWasSpace = false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                }

                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// แปลง severity จากหลายแหล่ง (คำพ้อง/ตัวย่อ/ตัวเลข 0–5) เป็นค่ามาตรฐาน Critical/High/Medium/Low/Informational (US-011).
/// </summary>
public static class SeverityNormalizer
{
    public const string Critical = "Critical";
    public const string High = "High";
    public const string Medium = "Medium";
    public const string Low = "Low";
    public const string Informational = "Informational";

    public static IReadOnlyList<string> CanonicalValues { get; } = [Critical, High, Medium, Low, Informational];

    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["critical"] = Critical, ["crit"] = Critical, ["urgent"] = Critical, ["very high"] = Critical, ["veryhigh"] = Critical,
        ["p1"] = Critical, ["5"] = Critical, ["sev5"] = Critical,
        ["high"] = High, ["h"] = High, ["important"] = High, ["serious"] = High, ["severe"] = High, ["p2"] = High,
        ["4"] = High, ["sev4"] = High,
        ["medium"] = Medium, ["med"] = Medium, ["m"] = Medium, ["moderate"] = Medium, ["p3"] = Medium, ["3"] = Medium,
        ["sev3"] = Medium,
        ["low"] = Low, ["l"] = Low, ["minor"] = Low, ["p4"] = Low, ["2"] = Low, ["sev2"] = Low,
        ["informational"] = Informational, ["info"] = Informational, ["information"] = Informational, ["none"] = Informational,
        ["note"] = Informational, ["p5"] = Informational, ["1"] = Informational, ["0"] = Informational, ["sev1"] = Informational,
    };

    /// <summary>ค่ามาตรฐาน หรือ null ถ้าไม่รู้จัก (→ INVALID_SEVERITY).</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var key = raw.Trim().Replace('_', ' ').Replace('-', ' ');
        return Map.TryGetValue(key, out var v) ? v : null;
    }
}

/// <summary>
/// Normalize location เพื่อให้ dedup key เสถียร: URL http(s) → scheme/host ตัวเล็ก, ตัด fragment และ "/" ท้าย path
/// (คง query ไว้เพราะ parameter มีผลต่อการระบุช่องโหว่); ค่าอื่น trim เท่านั้น.
/// </summary>
public static class LocationNormalizer
{
    public static string? Normalize(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        var s = location.Trim();
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var path = uri.AbsolutePath;
            if (path.Length > 1)
            {
                path = path.TrimEnd('/');
            }

            var normalized = $"{uri.Scheme}://{uri.Authority.ToLowerInvariant()}{path}{uri.Query}";
            return normalized.Length <= FindingLimits.Location ? normalized : s;
        }

        return s;
    }
}
