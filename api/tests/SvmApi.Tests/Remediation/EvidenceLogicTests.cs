using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SvmApi.Application.Remediation;
using SvmApi.Domain.Remediation;
using SvmApi.Infrastructure.Integration.Mock;
using SvmApi.Infrastructure.Remediation;

namespace SvmApi.Tests.Remediation;

/// <summary>
/// Evidence (pure · C8 · US-022 · FR-31 · BR-42 · NFR-18 · EX-21/22 · task 5.2): allowlist + content sniffing,
/// ชื่อไฟล์ปลอดภัย, SHA-256, versioning, storage key, mock malware scanner, local storage (กัน traversal/ไม่ทับไฟล์).
/// </summary>
public class EvidenceLogicTests
{
    private const long Max = 10 * 1024 * 1024;

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    private static readonly byte[] Pdf = Ascii("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n");
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
    private static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00];

    private static EvidenceValidation Validate(string? name, byte[] content, long max = Max)
        => EvidenceFileValidator.Validate(name, content, max);

    // ------------------------------------------------------------ accepted

    [Theory]
    [InlineData("scan.pdf", "application/pdf")]
    [InlineData("shot.PNG", "image/png")]
    [InlineData("shot.jpg", "image/jpeg")]
    [InlineData("shot.jpeg", "image/jpeg")]
    [InlineData("pkg.zip", "application/zip")]
    [InlineData("report.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("sheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public void Accepts_binary_types_when_magic_bytes_match(string name, string contentType)
    {
        var content = name.EndsWith("pdf", StringComparison.OrdinalIgnoreCase) ? Pdf
            : name.EndsWith("png", StringComparison.OrdinalIgnoreCase) ? Png
            : name.EndsWith("jpg", StringComparison.OrdinalIgnoreCase) || name.EndsWith("jpeg", StringComparison.OrdinalIgnoreCase) ? Jpg
            : Zip;

        var v = Validate(name, content);

        Assert.True(v.IsValid);
        Assert.Equal(contentType, v.ContentType);
        Assert.Equal(name, v.SafeFileName);
    }

    [Theory]
    [InlineData("notes.txt", "hello world\r\nสวัสดี")]
    [InlineData("scan.log", "2026-01-01 INFO ok\n")]
    [InlineData("data.csv", "a,b\n1,2\n")]
    [InlineData("cfg.json", "  {\"a\":1}")]
    [InlineData("list.json", "[1,2]")]
    public void Accepts_text_types(string name, string text)
        => Assert.True(Validate(name, Encoding.UTF8.GetBytes(text)).IsValid);

    [Fact]
    public void Content_type_comes_from_extension_not_from_client()
    {
        // ผู้โจมตีไม่มีช่องทางส่ง Content-Type เข้ามาใน validator; ค่าที่เก็บต้องเป็นของ allowlist เท่านั้น
        var v = Validate("a.png", Png);

        Assert.Equal("image/png", v.ContentType);
        Assert.Equal(".png", v.Extension);
    }

    // ------------------------------------------------------------ rejected: size

    [Fact]
    public void Rejects_empty_file()
    {
        var v = Validate("a.pdf", []);

        Assert.False(v.IsValid);
        Assert.Equal(RemediationOutcome.ValidationFailed, v.Outcome);
        Assert.Equal(RemediationErrorCodes.FileEmpty, v.ErrorCode);
    }

    [Fact]
    public void Rejects_file_larger_than_configured_max()
    {
        var big = new byte[101];
        Array.Copy(Pdf, big, Pdf.Length);

        var v = Validate("a.pdf", big, max: 100);

        Assert.Equal(RemediationOutcome.PayloadTooLarge, v.Outcome);
        Assert.Equal(RemediationErrorCodes.FileTooLarge, v.ErrorCode);
    }

    [Fact]
    public void Accepts_file_exactly_at_max()
    {
        var exact = new byte[100];
        Array.Copy(Pdf, exact, Pdf.Length);

        Assert.True(Validate("a.pdf", exact, max: 100).IsValid);
    }

    // ------------------------------------------------------------ rejected: extension

    [Theory]
    [InlineData("run.exe")]
    [InlineData("lib.dll")]
    [InlineData("s.ps1")]
    [InlineData("x.sh")]
    [InlineData("page.html")]
    [InlineData("pic.svg")]
    [InlineData("shell.php")]
    [InlineData("w.aspx")]
    [InlineData("noextension")]
    [InlineData("archive.tar.gz")]
    [InlineData("doc.pdf.exe")]
    [InlineData("evil.exe.png")]
    [InlineData("evil.php.txt")]
    [InlineData("evil.js.pdf")]
    public void Rejects_extensions_outside_allowlist_or_dangerous_in_any_position(string name)
    {
        var v = Validate(name, Pdf);

        Assert.False(v.IsValid);
        Assert.Equal(RemediationOutcome.UnsupportedMediaType, v.Outcome);
        Assert.Equal(RemediationErrorCodes.TypeNotAllowed, v.ErrorCode);
    }

    // ------------------------------------------------------------ rejected: content sniffing

    [Fact]
    public void Rejects_pdf_extension_with_png_content()
    {
        var v = Validate("a.pdf", Png);

        Assert.Equal(RemediationOutcome.UnsupportedMediaType, v.Outcome);
        Assert.Equal(RemediationErrorCodes.ContentMismatch, v.ErrorCode);
    }

    [Theory]
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00 })]             // PE
    [InlineData(new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02 })]       // ELF
    [InlineData(new byte[] { 0x23, 0x21, 0x2F, 0x62, 0x69, 0x6E })] // #!/bin
    public void Rejects_executable_signatures_under_any_allowed_extension(byte[] content)
    {
        foreach (var name in new[] { "a.txt", "a.pdf", "a.zip", "a.png" })
        {
            Assert.Equal(RemediationErrorCodes.ContentMismatch, Validate(name, content).ErrorCode);
        }
    }

    [Fact]
    public void Rejects_text_with_binary_bytes()
        => Assert.Equal(RemediationErrorCodes.ContentMismatch, Validate("a.txt", [0x68, 0x00, 0x69]).ErrorCode);

    [Fact]
    public void Rejects_text_with_invalid_utf8()
        => Assert.Equal(RemediationErrorCodes.ContentMismatch, Validate("a.log", [0x68, 0xC3, 0x28]).ErrorCode);

    [Theory]
    [InlineData("plain text")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("")]
    public void Json_must_start_with_object_or_array(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text.Length == 0 ? " " : text);

        Assert.Equal(RemediationErrorCodes.ContentMismatch, Validate("a.json", bytes).ErrorCode);
    }

    [Fact]
    public void Multibyte_text_cut_at_probe_boundary_is_not_misjudged_as_invalid()
    {
        // ข้อความไทยยาวเกิน 8 KB — ขอบ probe อาจตัดกลางอักขระ 3 ไบต์
        var text = string.Concat(Enumerable.Repeat("สวัสดี", 3000));
        var bytes = Encoding.UTF8.GetBytes(text);
        Assert.True(bytes.Length > 8192);

        Assert.True(Validate("long.txt", bytes).IsValid);
    }

    // ------------------------------------------------------------ file name sanitation

    [Theory]
    [InlineData("..\\..\\evil.pdf", "evil.pdf")]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData("C:\\temp\\report.pdf", "report.pdf")]
    [InlineData("  report .pdf", "report .pdf")]
    [InlineData("re\u0007po\u202Ert.pdf", "report.pdf")]
    public void Path_segments_and_control_chars_are_removed(string input, string expected)
        => Assert.Equal(expected, EvidenceFileValidator.SanitizeFileName(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".pdf")]
    [InlineData("..")]
    [InlineData("a.pdf.")]
    [InlineData("a:b.pdf")]
    [InlineData("a|b.pdf")]
    [InlineData("a?.pdf")]
    [InlineData("a*.pdf")]
    [InlineData("dir/")]
    public void Unsafe_file_names_are_rejected(string? input)
        => Assert.Null(EvidenceFileValidator.SanitizeFileName(input));

    [Fact]
    public void Overlong_file_name_is_rejected()
        => Assert.Null(EvidenceFileValidator.SanitizeFileName(new string('a', EvidenceFileValidator.MaxFileNameLength) + ".pdf"));

    [Fact]
    public void Validate_rejects_invalid_file_name_with_dedicated_code()
        => Assert.Equal(RemediationErrorCodes.InvalidFileName, Validate(".pdf", Pdf).ErrorCode);

    // ------------------------------------------------------------ hash

    [Fact]
    public void Sha256_matches_known_vector_and_is_lowercase_hex()
        => Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            EvidenceFileValidator.Sha256Hex(Ascii("abc")));

    // ------------------------------------------------------------ versioning (BR-42)

    [Fact]
    public void First_upload_is_version_one()
        => Assert.Equal(1, EvidenceVersioning.NextVersion([]));

    [Theory]
    [InlineData(new[] { 1 }, 2)]
    [InlineData(new[] { 1, 2, 3 }, 4)]
    [InlineData(new[] { 3, 1 }, 4)]
    [InlineData(new[] { 0, -5 }, 1)]
    public void Next_version_is_max_plus_one(int[] existing, int expected)
        => Assert.Equal(expected, EvidenceVersioning.NextVersion(existing));

    [Fact]
    public void Series_key_ignores_case_and_surrounding_spaces()
        => Assert.Equal(EvidenceVersioning.SeriesKey("Report.PDF"), EvidenceVersioning.SeriesKey("  report.pdf "));

    // ------------------------------------------------------------ storage key

    [Fact]
    public void Storage_key_is_generated_from_guids_and_allowlisted_extension_only()
    {
        var instanceId = Guid.NewGuid();
        var evidenceId = Guid.NewGuid();

        var key = EvidenceVersioning.BuildStorageKey(instanceId, evidenceId, ".PDF");

        Assert.Matches(new Regex(@"^[0-9a-f]{32}/[0-9a-f]{32}\.pdf$"), key);
        Assert.Contains(instanceId.ToString("N"), key);
        Assert.Contains(evidenceId.ToString("N"), key);
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData("")]
    [InlineData("../x")]
    public void Storage_key_rejects_extensions_outside_allowlist(string extension)
        => Assert.Throws<ArgumentException>(() => EvidenceVersioning.BuildStorageKey(Guid.NewGuid(), Guid.NewGuid(), extension));

    // ------------------------------------------------------------ mock malware scanner (EX-21/22)

    [Fact]
    public void Scanner_flags_eicar_as_infected()
    {
        var eicar = Ascii("X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");

        Assert.Equal(ScanStatus.Infected, MockMalwareScanner.Scan(eicar, ".txt"));
    }

    [Fact]
    public void Scanner_flags_eicar_embedded_in_other_content()
    {
        var content = Pdf.Concat(Ascii("EICAR-STANDARD-ANTIVIRUS-TEST-FILE")).ToArray();

        Assert.Equal(ScanStatus.Infected, MockMalwareScanner.Scan(content, ".pdf"));
    }

    [Fact]
    public void Scanner_marks_encrypted_pdf_as_unscannable()
        => Assert.Equal(ScanStatus.Unscannable, MockMalwareScanner.Scan(Ascii("%PDF-1.7\n<< /Encrypt 5 0 R >>"), ".pdf"));

    [Fact]
    public void Scanner_marks_encrypted_zip_entry_as_unscannable()
    {
        byte[] encrypted = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x01, 0x00, 0x00];

        Assert.Equal(ScanStatus.Unscannable, MockMalwareScanner.Scan(encrypted, ".zip"));
    }

    [Fact]
    public void Scanner_passes_clean_content()
    {
        Assert.Equal(ScanStatus.Clean, MockMalwareScanner.Scan(Pdf, ".pdf"));
        Assert.Equal(ScanStatus.Clean, MockMalwareScanner.Scan(Zip, ".zip"));
        Assert.Equal(ScanStatus.Clean, MockMalwareScanner.Scan(Ascii("hello"), ".txt"));
    }

    [Fact]
    public async Task Unavailable_scanner_is_fail_closed()
        => Assert.Equal(ScanStatus.Unscannable, await new UnavailableMalwareScanner().ScanAsync(Pdf, ".pdf", CancellationToken.None));

    // ------------------------------------------------------------ local file storage

    private static (LocalFileStorage Storage, string Root) NewStorage()
    {
        var root = Path.Combine(Path.GetTempPath(), "svm-evidence-test-" + Guid.NewGuid().ToString("N"));
        return (new LocalFileStorage(Options.Create(new EvidenceOptions { StorageRoot = root })), root);
    }

    [Fact]
    public async Task Storage_writes_under_root_and_never_overwrites()
    {
        var (storage, root) = NewStorage();
        try
        {
            var key = EvidenceVersioning.BuildStorageKey(Guid.NewGuid(), Guid.NewGuid(), ".pdf");

            await storage.SaveAsync(key, Pdf, CancellationToken.None);

            var path = Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path));
            Assert.Equal(Pdf, await File.ReadAllBytesAsync(path));
            await Assert.ThrowsAsync<IOException>(() => storage.SaveAsync(key, Png, CancellationToken.None));
            Assert.Equal(Pdf, await File.ReadAllBytesAsync(path));

            await storage.DeleteAsync(key, CancellationToken.None);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("../evil.pdf")]
    [InlineData("..\\evil.pdf")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\x.pdf")]
    [InlineData("report.pdf")]
    [InlineData("0123456789abcdef0123456789abcdef/../../x.pdf")]
    [InlineData("0123456789abcdef0123456789abcdef/0123456789abcdef0123456789abcdef.exe.pdf")]
    [InlineData("")]
    public async Task Storage_rejects_keys_that_are_not_generated_keys(string key)
    {
        var (storage, root) = NewStorage();
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveAsync(key, Pdf, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => storage.DeleteAsync(key, CancellationToken.None));
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Delete_of_missing_file_is_a_noop()
    {
        var (storage, _) = NewStorage();
        var key = EvidenceVersioning.BuildStorageKey(Guid.NewGuid(), Guid.NewGuid(), ".txt");

        await storage.DeleteAsync(key, CancellationToken.None);
    }

    // ------------------------------------------------------------ options

    [Fact]
    public void Max_file_size_is_clamped_to_hard_cap_and_positive()
    {
        Assert.Equal(EvidenceOptions.HardMaxFileBytes, new EvidenceOptions { MaxFileBytes = long.MaxValue }.EffectiveMaxFileBytes);
        Assert.Equal(1, new EvidenceOptions { MaxFileBytes = -5 }.EffectiveMaxFileBytes);
        Assert.Equal(10L * 1024 * 1024, new EvidenceOptions().EffectiveMaxFileBytes);
    }
}
