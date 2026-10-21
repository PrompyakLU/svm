using System.Text.RegularExpressions;

namespace SvmApi.Application.Reference;

/// <summary>
/// ภาพ snapshot ของ reference data (immutable) สำหรับ lookup เร็ว — key เทียบแบบไม่สนตัวพิมพ์.
/// </summary>
public sealed class ReferenceSnapshot
{
    public static ReferenceSnapshot Empty { get; } = new([], [], new Dictionary<string, bool>());

    public ReferenceSnapshot(
        IEnumerable<CveDto> cves,
        IEnumerable<CweDto> cwes,
        IReadOnlyDictionary<string, bool> cvssVersions)
    {
        Cves = cves.ToDictionary(c => c.CveId.Trim(), StringComparer.OrdinalIgnoreCase);
        Cwes = cwes.ToDictionary(c => c.CweId.Trim(), StringComparer.OrdinalIgnoreCase);
        CvssVersions = new Dictionary<string, bool>(cvssVersions, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, CveDto> Cves { get; }

    public IReadOnlyDictionary<string, CweDto> Cwes { get; }

    /// <summary>version -> isSupported (config_cvssVersion).</summary>
    public IReadOnlyDictionary<string, bool> CvssVersions { get; }
}

/// <summary>รูปแบบ id ของ CVE/CWE และตัวช่วย normalize (ใช้ร่วมกับ controller validation).</summary>
public static partial class ReferenceIds
{
    public const int MaxCveIdLength = 30;
    public const int MaxCweIdLength = 20;

    /// <summary>ตรงกับ trans_vulnerabilityFinding.cvssVersion nvarchar(10) — Intake ต้องตรวจก่อนบันทึก.</summary>
    public const int MaxCvssVersionLength = 10;

    [GeneratedRegex(@"^CVE-\d{4}-\d{4,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CvePattern();

    [GeneratedRegex(@"^CWE-\d{1,7}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CwePattern();

    public static bool IsValidCveId(string? id)
        => id is { Length: > 0 and <= MaxCveIdLength } && CvePattern().IsMatch(id);

    public static bool IsValidCweId(string? id)
        => id is { Length: > 0 and <= MaxCweIdLength } && CwePattern().IsMatch(id);

    /// <summary>trim; ว่าง = null. ไม่เปลี่ยนตัวพิมพ์ (EX-47 คงค่าที่ส่งมา; lookup ไม่สนตัวพิมพ์อยู่แล้ว).</summary>
    public static string? NormalizeCveId(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : id.Trim();

    /// <summary>trim; ว่าง = null; ตัวเลขล้วน (scanner บางตัวส่ง "79") -> "CWE-79".</summary>
    public static string? NormalizeCweId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var trimmed = id.Trim();
        return trimmed.Length <= 7 && trimmed.All(char.IsAsciiDigit) ? "CWE-" + trimmed : trimmed;
    }

    /// <summary>"CVSS:3.1" / "CVSSv3.1" / "v3.1" / " 3.1 " -> "3.1"; ว่าง = null. ไม่ตรวจว่ารองรับหรือไม่.</summary>
    public static string? NormalizeCvssVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var v = version.Trim();
        if (v.StartsWith("CVSS", StringComparison.OrdinalIgnoreCase))
        {
            v = v[4..].TrimStart(':', ' ');
        }

        if (v.StartsWith('v') || v.StartsWith('V'))
        {
            v = v[1..];
        }

        v = v.Trim();
        return v.Length == 0 ? null : v;
    }
}

/// <summary>CVSS base score (FR-14): ช่วง 0.0–10.0, ทศนิยม 1 ตำแหน่ง (decimal(4,1)) และ severity rating scale v3.x/v4.0.</summary>
public static class CvssScore
{
    public const decimal Min = 0.0m;
    public const decimal Max = 10.0m;

    public static bool IsValid(decimal score)
        => score is >= Min and <= Max && decimal.Round(score, 1) == score;

    /// <summary>qualitative severity (None/Low/Medium/High/Critical); null เมื่อ score ไม่ถูกต้อง.</summary>
    public static string? Severity(decimal score)
    {
        if (!IsValid(score))
        {
            return null;
        }

        return score switch
        {
            0.0m => CvssSeverities.None,
            < 4.0m => CvssSeverities.Low,
            < 7.0m => CvssSeverities.Medium,
            < 9.0m => CvssSeverities.High,
            _ => CvssSeverities.Critical,
        };
    }
}

/// <summary>
/// ตรวจรูปแบบ CVSS vector (EX-49). รองรับ v3.1 และ v4.0 ตามลำดับ metric มาตรฐาน; metric เสริม
/// (temporal/environmental/supplemental) ต่อท้ายได้ถ้าอยู่ในรูป <c>KEY:VALUE</c>.
/// ไม่คำนวณ score และไม่ตรวจความหมายของ metric เสริม.
/// </summary>
public static partial class CvssVectorValidator
{
    /// <summary>ตรงกับ trans_vulnerabilityFinding.cvssVector nvarchar(200).</summary>
    public const int MaxVectorLength = 200;

    private const string V31Base =
        @"^CVSS:3\.1/AV:[NALP]/AC:[LH]/PR:[NLH]/UI:[NR]/S:[UC]/C:[NLH]/I:[NLH]/A:[NLH]";

    private const string V40Base =
        @"^CVSS:4\.0/AV:[NALP]/AC:[LH]/AT:[NP]/PR:[NLH]/UI:[NPA]/VC:[HLN]/VI:[HLN]/VA:[HLN]/SC:[HLN]/SI:[HLN]/SA:[HLN]";

    private const string Optional = @"(/[A-Z]{1,3}:[A-Za-z0-9]{1,3})*$";

    [GeneratedRegex(V31Base + Optional, RegexOptions.CultureInvariant)]
    private static partial Regex V31();

    [GeneratedRegex(V40Base + Optional, RegexOptions.CultureInvariant)]
    private static partial Regex V40();

    /// <summary>version ที่อนุมานได้จาก prefix "CVSS:x.y/" (null ถ้าไม่มี prefix).</summary>
    public static string? InferVersion(string? vector)
    {
        if (vector is null || !vector.StartsWith("CVSS:", StringComparison.Ordinal))
        {
            return null;
        }

        var slash = vector.IndexOf('/');
        return slash > 5 ? vector[5..slash] : null;
    }

    /// <summary>true เมื่อ vector ถูกรูปแบบสำหรับ <paramref name="version"/> ("3.1" หรือ "4.0"). version อื่น = false.</summary>
    public static bool IsValid(string? version, string? vector)
    {
        if (string.IsNullOrWhiteSpace(vector) || vector.Length > MaxVectorLength)
        {
            return false;
        }

        return version switch
        {
            "3.1" => V31().IsMatch(vector),
            "4.0" => V40().IsMatch(vector),
            _ => false,
        };
    }
}

/// <summary>
/// ตรรกะ enrich แบบ pure (ไม่มี I/O): รับ snapshot + ค่าดิบ คืนผลพร้อม issue code. ไม่ throw เมื่อข้อมูลผิด
/// (EX-47: คง CVE ที่ส่งมา+ทำเครื่องหมาย · EX-48: CVSS version ไม่รองรับ · EX-49: vector ผิดรูปแบบ).
/// </summary>
public static class ReferenceEnricher
{
    public static ReferenceEnrichment Enrich(ReferenceSnapshot snapshot, ReferenceEnrichmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<string>();

        // --- CVE (EX-47): พบ = ใช้ id มาตรฐานจาก master; ไม่พบ = คงค่าที่ส่งมา + mark ---
        var cveId = ReferenceIds.NormalizeCveId(request.CveId);
        CveDto? cve = null;
        if (cveId is not null)
        {
            if (snapshot.Cves.TryGetValue(cveId, out cve))
            {
                cveId = cve.CveId;
            }
            else
            {
                issues.Add(ReferenceIssueCodes.CveNotFound);
                if (!ReferenceIds.IsValidCveId(cveId))
                {
                    issues.Add(ReferenceIssueCodes.CveIdInvalid);
                }
            }
        }

        // --- CWE ---
        var cweId = ReferenceIds.NormalizeCweId(request.CweId);
        CweDto? cwe = null;
        if (cweId is not null)
        {
            if (snapshot.Cwes.TryGetValue(cweId, out cwe))
            {
                cweId = cwe.CweId;
            }
            else
            {
                issues.Add(ReferenceIssueCodes.CweNotFound);
                if (!ReferenceIds.IsValidCweId(cweId))
                {
                    issues.Add(ReferenceIssueCodes.CweIdInvalid);
                }
            }
        }

        // --- CVSS (FR-14 · EX-48/49) ---
        var vector = Normalize(request.CvssVector);
        var version = ReferenceIds.NormalizeCvssVersion(request.CvssVersion);
        var score = request.CvssBaseScore;
        var versionSupported = false;
        var vectorValid = false;

        if (version is null && (vector is not null || score is not null))
        {
            version = CvssVectorValidator.InferVersion(vector);
            if (version is null)
            {
                issues.Add(ReferenceIssueCodes.CvssVersionMissing);
            }
        }

        if (version is not null)
        {
            versionSupported = snapshot.CvssVersions.TryGetValue(version, out var supported) && supported;
            if (!versionSupported)
            {
                issues.Add(ReferenceIssueCodes.CvssVersionUnsupported);
            }
            else if (vector is not null)
            {
                // prefix ใน vector ต้องตรงกับ version ที่ระบุ (ถ้ามี prefix)
                var inferred = CvssVectorValidator.InferVersion(vector);
                vectorValid = (inferred is null || string.Equals(inferred, version, StringComparison.Ordinal))
                              && CvssVectorValidator.IsValid(version, vector);
                if (!vectorValid)
                {
                    issues.Add(ReferenceIssueCodes.CvssVectorInvalid);
                }
            }
        }

        // score ผิด -> mark และไม่ส่งต่อ (ไม่ให้ค่าผิดถูกใช้คำนวณ risk); severity เฉพาะ version ที่รองรับ
        decimal? validScore = null;
        string? severity = null;
        if (score is { } s)
        {
            if (CvssScore.IsValid(s))
            {
                validScore = s;
                severity = versionSupported ? CvssScore.Severity(s) : null;
            }
            else
            {
                issues.Add(ReferenceIssueCodes.CvssScoreInvalid);
            }
        }

        return new ReferenceEnrichment(
            cveId,
            cve,
            cweId,
            cwe,
            version,
            vector,
            versionSupported,
            vectorValid,
            cve?.IsKev ?? false,
            issues,
            validScore,
            severity);
    }

    public static bool IsKev(ReferenceSnapshot snapshot, string? cveId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var id = Normalize(cveId);
        return id is not null && snapshot.Cves.TryGetValue(id, out var cve) && cve.IsKev;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
