using SvmApi.Domain.Reference;

namespace SvmApi.Infrastructure.Persistence.Seed;

internal static partial class SeedData
{
    internal sealed record CveSeed(
        string CveId,
        string Description,
        DateTime PublishedDate,
        bool IsKev,
        DateTime? KevDateAdded);

    internal sealed record CweSeed(string CweId, string Name, string Description);

    internal sealed record CvssVersionSeed(string Version, bool IsSupported);

    private static DateTime Utc(int year, int month, int day)
        => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// ตัวอย่าง CVE สำหรับ demo/ทดสอบ (master_cve) — FR-13/14, EX-47.
    /// <para>
    /// เป็น demo seed เท่านั้น ไม่ใช่ข้อมูลอ้างอิงทางการ: คำอธิบายย่อและเป็นกลาง วันที่เป็น snapshot ณ เวลาที่เตรียม seed
    /// สถานะ KEV (<see cref="Cve.IsKev"/>) อ้างอิงจาก CISA Known Exploited Vulnerabilities catalog
    /// ที่ตรวจทานแล้ว ณ เวลาที่เขียน — ข้อมูลจริงต้อง sync จากแหล่งทางการ (reference sync อยู่นอก core slice).
    /// มีทั้งรายการที่เป็น KEV และไม่ใช่ KEV เพื่อทดสอบ risk scoring/SLA ตาม KEV.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<CveSeed> Cves { get; } =
    [
        // --- KEV ---
        new("CVE-2021-44228",
            "Apache Log4j2 JNDI lookup remote code execution (Log4Shell).",
            Utc(2021, 12, 10), true, Utc(2021, 12, 10)),
        new("CVE-2021-45046",
            "Apache Log4j2 incomplete fix of CVE-2021-44228 in certain non-default configurations.",
            Utc(2021, 12, 14), true, Utc(2023, 5, 1)),
        new("CVE-2014-0160",
            "OpenSSL TLS/DTLS Heartbeat extension information disclosure (Heartbleed).",
            Utc(2014, 4, 7), true, Utc(2022, 5, 4)),
        new("CVE-2017-0144",
            "Microsoft SMBv1 server remote code execution (EternalBlue).",
            Utc(2017, 3, 16), true, Utc(2022, 2, 10)),
        new("CVE-2022-22965",
            "Spring Framework data binding remote code execution on JDK 9+ (Spring4Shell).",
            Utc(2022, 4, 1), true, Utc(2022, 4, 4)),
        new("CVE-2023-4966",
            "Citrix NetScaler ADC/Gateway buffer overflow leading to sensitive information disclosure (Citrix Bleed).",
            Utc(2023, 10, 10), true, Utc(2023, 10, 18)),

        // --- ไม่ใช่ KEV ---
        new("CVE-2016-2183",
            "Birthday attack against 64-bit block ciphers such as 3DES in TLS (SWEET32).",
            Utc(2016, 9, 1), false, null),
        new("CVE-2022-3602",
            "OpenSSL 3.x X.509 name constraint checking buffer overflow (punycode).",
            Utc(2022, 11, 1), false, null),
        new("CVE-2023-38545",
            "curl SOCKS5 proxy handshake heap-based buffer overflow.",
            Utc(2023, 10, 11), false, null),
        new("CVE-2014-3566",
            "SSL 3.0 padding oracle on downgraded legacy encryption (POODLE).",
            Utc(2014, 10, 14), false, null),
        new("CVE-2024-3094",
            "Malicious code embedded in upstream xz/liblzma release tarballs (xz backdoor).",
            Utc(2024, 3, 29), false, null),
    ];

    /// <summary>ตัวอย่าง CWE (master_cwe) — ชื่อตามมาตรฐาน MITRE CWE (demo seed).</summary>
    internal static IReadOnlyList<CweSeed> Cwes { get; } =
    [
        new("CWE-20", "Improper Input Validation", "The product does not validate or incorrectly validates input."),
        new("CWE-22", "Improper Limitation of a Pathname to a Restricted Directory ('Path Traversal')", "Path elements are not neutralized, allowing access outside the intended directory."),
        new("CWE-78", "Improper Neutralization of Special Elements used in an OS Command ('OS Command Injection')", "Externally influenced input is used to construct an operating system command."),
        new("CWE-79", "Improper Neutralization of Input During Web Page Generation ('Cross-site Scripting')", "User-controllable input is placed in output served to other users without proper neutralization."),
        new("CWE-89", "Improper Neutralization of Special Elements used in an SQL Command ('SQL Injection')", "Externally influenced input is used to construct an SQL command."),
        new("CWE-94", "Improper Control of Generation of Code ('Code Injection')", "Externally influenced input is used to construct code segments."),
        new("CWE-125", "Out-of-bounds Read", "The product reads data past the end, or before the beginning, of the intended buffer."),
        new("CWE-287", "Improper Authentication", "An actor claims an identity but the product does not sufficiently prove it."),
        new("CWE-352", "Cross-Site Request Forgery (CSRF)", "The web application does not verify that a request was intentionally submitted by the user."),
        new("CWE-416", "Use After Free", "The product references memory after it has been freed."),
        new("CWE-502", "Deserialization of Untrusted Data", "The product deserializes untrusted data without sufficiently verifying the result."),
        new("CWE-787", "Out-of-bounds Write", "The product writes data past the end, or before the beginning, of the intended buffer."),
        new("CWE-798", "Use of Hard-coded Credentials", "The product contains hard-coded credentials such as a password or cryptographic key."),
        new("CWE-917", "Improper Neutralization of Special Elements used in an Expression Language Statement ('Expression Language Injection')", "Externally influenced input is used to construct an expression language statement."),
    ];

    /// <summary>CVSS version ที่รองรับ (config_cvssVersion) — 3.1/4.0 = supported, 2.0 = not supported (EX-48).</summary>
    internal static IReadOnlyList<CvssVersionSeed> CvssVersions { get; } =
    [
        new("3.1", true),
        new("4.0", true),
        new("2.0", false),
    ];
}
