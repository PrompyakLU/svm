using System.Security.Cryptography;
using System.Text;
using SvmApi.Application.Common;

namespace SvmApi.Application.Remediation;

/// <summary>ผลตรวจไฟล์ evidence (ก่อนส่งสแกน).</summary>
public sealed record EvidenceValidation(
    bool IsValid,
    RemediationOutcome Outcome,
    string? ErrorCode,
    string SafeFileName,
    string Extension,
    string ContentType)
{
    public static EvidenceValidation Fail(RemediationOutcome outcome, string code)
        => new(false, outcome, code, string.Empty, string.Empty, string.Empty);
}

/// <summary>
/// ตรวจไฟล์ evidence แบบ pure (NFR-18): allowlist นามสกุล, ปฏิเสธนามสกุลอันตรายแม้เป็นส่วนกลาง (เช่น <c>a.exe.png</c>),
/// ขนาด, และ content sniffing ด้วย magic bytes — ไม่เชื่อ Content-Type ของ client; Content-Type ที่เก็บมาจากนามสกุลที่ผ่านการตรวจเท่านั้น.
/// </summary>
public static class EvidenceFileValidator
{
    public const int MaxFileNameLength = 255;
    private const int TextProbeBytes = 8192;

    /// <summary>นามสกุลที่รับ → Content-Type ที่เชื่อถือได้.</summary>
    public static IReadOnlyDictionary<string, string> AllowedTypes { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".txt"] = "text/plain",
            [".log"] = "text/plain",
            [".csv"] = "text/csv",
            [".json"] = "application/json",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".zip"] = "application/zip",
        };

    private static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".com", ".scr", ".msi", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".hta",
        ".sh", ".bash", ".php", ".phtml", ".asp", ".aspx", ".ashx", ".jsp", ".cgi", ".pl", ".py", ".rb", ".jar", ".war",
        ".html", ".htm", ".xhtml", ".svg", ".swf", ".lnk", ".reg", ".iso", ".dmg", ".apk", ".config",
    };

    public static EvidenceValidation Validate(string? fileName, ReadOnlySpan<byte> content, long maxBytes)
    {
        if (content.Length == 0)
        {
            return EvidenceValidation.Fail(RemediationOutcome.ValidationFailed, RemediationErrorCodes.FileEmpty);
        }

        if (content.Length > maxBytes)
        {
            return EvidenceValidation.Fail(RemediationOutcome.PayloadTooLarge, RemediationErrorCodes.FileTooLarge);
        }

        var safeName = SanitizeFileName(fileName);
        if (safeName is null)
        {
            return EvidenceValidation.Fail(RemediationOutcome.ValidationFailed, RemediationErrorCodes.InvalidFileName);
        }

        var segments = safeName.Split('.');
        // segments[0] = ชื่อฐาน (ไม่ใช่นามสกุล); ที่เหลือคือนามสกุลทุกชั้น — ต้องไม่มีชั้นใดอันตราย
        for (var i = 1; i < segments.Length; i++)
        {
            if (DangerousExtensions.Contains("." + segments[i]))
            {
                return EvidenceValidation.Fail(RemediationOutcome.UnsupportedMediaType, RemediationErrorCodes.TypeNotAllowed);
            }
        }

        var extension = segments.Length > 1 ? "." + segments[^1].ToLowerInvariant() : string.Empty;
        if (!AllowedTypes.TryGetValue(extension, out var contentType))
        {
            return EvidenceValidation.Fail(RemediationOutcome.UnsupportedMediaType, RemediationErrorCodes.TypeNotAllowed);
        }

        if (LooksExecutable(content) || !ContentMatches(extension, content))
        {
            return EvidenceValidation.Fail(RemediationOutcome.UnsupportedMediaType, RemediationErrorCodes.ContentMismatch);
        }

        return new EvidenceValidation(true, RemediationOutcome.Success, null, safeName, extension, contentType);
    }

    /// <summary>
    /// ตัด path (ทั้ง / และ \), control char, bidi/zero-width; ปฏิเสธชื่อว่าง ชื่อที่ขึ้นต้นด้วยจุด (hidden) ชื่อยาวเกิน
    /// หรือมีอักขระต้องห้ามของระบบไฟล์. ชื่อนี้ใช้ "แสดงผล" เท่านั้น — ไม่ถูกใช้เป็นชื่อไฟล์บนดิสก์.
    /// </summary>
    public static string? SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var lastSeparator = fileName.LastIndexOfAny(['/', '\\']);
        var name = lastSeparator >= 0 ? fileName[(lastSeparator + 1)..] : fileName;
        name = TextSanitizer.Clean(name, allowLineBreaks: false);

        if (name.Length == 0 || name.Length > MaxFileNameLength || name.StartsWith('.') || name.EndsWith('.') || name.EndsWith(' '))
        {
            return null;
        }

        if (name.IndexOfAny(['<', '>', ':', '"', '|', '?', '*', '\0']) >= 0)
        {
            return null;
        }

        return name;
    }

    /// <summary>SHA-256 (hex ตัวเล็ก) ของเนื้อหา — บันทึกใน audit/response เท่านั้น ไม่เก็บเนื้อหา.</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static bool LooksExecutable(ReadOnlySpan<byte> c)
        => StartsWith(c, 0x4D, 0x5A)                       // MZ (PE)
           || StartsWith(c, 0x7F, 0x45, 0x4C, 0x46)        // ELF
           || StartsWith(c, 0xCF, 0xFA, 0xED, 0xFE)        // Mach-O 64
           || StartsWith(c, 0xCA, 0xFE, 0xBA, 0xBE)        // Mach-O fat / Java class
           || StartsWith(c, 0x23, 0x21);                   // #! script

    private static bool ContentMatches(string extension, ReadOnlySpan<byte> c) => extension switch
    {
        ".pdf" => StartsWith(c, 0x25, 0x50, 0x44, 0x46, 0x2D),                                   // %PDF-
        ".png" => StartsWith(c, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
        ".jpg" or ".jpeg" => StartsWith(c, 0xFF, 0xD8, 0xFF),
        ".docx" or ".xlsx" or ".zip" => StartsWith(c, 0x50, 0x4B, 0x03, 0x04) || StartsWith(c, 0x50, 0x4B, 0x05, 0x06),
        ".json" => LooksLikeText(c) && FirstNonWhitespace(c) is (byte)'{' or (byte)'[',
        ".txt" or ".log" or ".csv" => LooksLikeText(c),
        _ => false,
    };

    private static bool StartsWith(ReadOnlySpan<byte> c, params byte[] prefix)
        => c.Length >= prefix.Length && c[..prefix.Length].SequenceEqual(prefix);

    /// <summary>ข้อความ: ไม่มี NUL / control char ที่ไม่ใช่ \t \n \r ใน 8 KB แรก และเป็น UTF-8 ที่ถูกต้อง (ยอม BOM).</summary>
    private static bool LooksLikeText(ReadOnlySpan<byte> c)
    {
        var probe = c[..Math.Min(c.Length, TextProbeBytes)];
        foreach (var b in probe)
        {
            if (b == 0 || (b < 0x20 && b != 0x09 && b != 0x0A && b != 0x0D))
            {
                return false;
            }
        }

        // ตัดท้ายที่อาจถูกตัดกลางอักขระ multi-byte ก่อนตรวจ UTF-8
        var end = probe.Length;
        if (c.Length > TextProbeBytes)
        {
            var back = 0;
            while (end > 0 && back < 3 && (probe[end - 1] & 0xC0) == 0x80)
            {
                end--;
                back++;
            }

            if (end > 0 && probe[end - 1] >= 0xC0)
            {
                end--;
            }
        }

        try
        {
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetCharCount(probe[..end]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static int FirstNonWhitespace(ReadOnlySpan<byte> c)
    {
        var start = StartsWith(c, 0xEF, 0xBB, 0xBF) ? 3 : 0;
        for (var i = start; i < c.Length; i++)
        {
            if (c[i] is not (0x20 or 0x09 or 0x0A or 0x0D))
            {
                return c[i];
            }
        }

        return -1;
    }
}

/// <summary>Version ของ evidence (BR-42): ไฟล์ชื่อเดียวกัน (ไม่สนตัวพิมพ์) ใน scope เดียวกันได้เลขเวอร์ชันถัดไป — ไม่ทับของเดิม.</summary>
public static class EvidenceVersioning
{
    /// <summary>เวอร์ชันถัดไป = max(existing) + 1 (เริ่มที่ 1). ค่าที่ ≤ 0 ถูกมองข้าม.</summary>
    public static int NextVersion(IEnumerable<int> existingVersions)
    {
        ArgumentNullException.ThrowIfNull(existingVersions);

        var max = 0;
        foreach (var v in existingVersions)
        {
            if (v > max)
            {
                max = v;
            }
        }

        return checked(max + 1);
    }

    /// <summary>คีย์เทียบชื่อไฟล์สำหรับ version series.</summary>
    public static string SeriesKey(string fileName) => fileName.Trim().ToUpperInvariant();

    /// <summary>
    /// key บนที่เก็บไฟล์: <c>{instanceId:N}/{evidenceId:N}{ext}</c> — สร้างจาก GUID + นามสกุล allowlist เท่านั้น
    /// (ไม่มีชื่อไฟล์ผู้ใช้ จึงไม่มี path traversal). นามสกุลนอก allowlist → <see cref="ArgumentException"/>.
    /// </summary>
    public static string BuildStorageKey(Guid instanceId, Guid evidenceId, string extension)
    {
        if (!EvidenceFileValidator.AllowedTypes.ContainsKey(extension))
        {
            throw new ArgumentException("Extension is not allowed.", nameof(extension));
        }

        return $"{instanceId:N}/{evidenceId:N}{extension.ToLowerInvariant()}";
    }
}
