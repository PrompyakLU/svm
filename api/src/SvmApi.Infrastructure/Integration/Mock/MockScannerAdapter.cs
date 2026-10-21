using SvmApi.Application.Intake;
using SvmApi.Application.Verification;

namespace SvmApi.Infrastructure.Integration.Mock;

/// <summary>
/// Mock <see cref="IScannerAdapter"/> (integration.md): คืนผลสแกนตัวอย่างคงที่บน seed asset — ไม่เรียกระบบภายนอก.
/// ผลเป็น <see cref="RawFindingRow"/> จึงผ่าน validation/normalize/dedup ชุดเดียวกับ file import.
/// </summary>
public sealed class MockScannerAdapter : IScannerAdapter
{
    private static readonly IReadOnlyList<RawFindingRow> Sample =
    [
        new("SVM-APP-001", "MockScanner", "MS-0001", "Apache Log4j2 remote code execution", "Critical",
            CveId: "CVE-2021-44228", CvssVersion: "3.1", CvssBaseScore: "10.0",
            CvssVector: "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:C/C:H/I:H/A:H",
            Location: "https://app001.example/api", Port: "443", Component: "log4j-core@2.14.1"),
        new("SVM-SRV-001", "MockScanner", "MS-0002", "OpenSSL Heartbleed", "high",
            CveId: "CVE-2014-0160", Port: "443", Component: "openssl@1.0.1f"),
        new("SVM-SRV-001", "MockScanner", "MS-0003", "SSL 3.0 POODLE", "medium",
            CveId: "CVE-2014-3566", Port: "8443"),
    ];

    public Task<IReadOnlyList<RawFindingRow>> PullFindingsAsync(string sourceSystem, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<RawFindingRow> rows = string.IsNullOrWhiteSpace(sourceSystem)
            ? []
            : Sample.Select(r => r with { SourceSystem = sourceSystem.Trim() }).ToList();
        return Task.FromResult(rows);
    }

    /// <summary>
    /// จำลอง rescan แบบ deterministic (ไม่สุ่ม — ทดสอบซ้ำได้): CVE ใน <see cref="StillVulnerableCves"/> = <see cref="RescanStatus.StillDetected"/>,
    /// ไม่มี asset code = <see cref="RescanStatus.TargetNotFound"/> (EX-75), นอกนั้น = <see cref="RescanStatus.Resolved"/>.
    /// ไม่เรียกระบบภายนอก.
    /// </summary>
    public Task<RescanResponse> RequestRescanAsync(RescanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var requestId = "MOCK-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

        var status = string.IsNullOrWhiteSpace(request.AssetCode)
            ? RescanStatus.TargetNotFound
            : request.CveId is not null && StillVulnerableCves.Contains(request.CveId.Trim())
                ? RescanStatus.StillDetected
                : RescanStatus.Resolved;

        return Task.FromResult(new RescanResponse(requestId, status));
    }

    /// <summary>CVE ที่ mock ถือว่า "ยังพบ" หลัง rescan (Log4Shell ใน seed ตัวอย่าง) — CVE อื่นถือว่าแก้แล้ว.</summary>
    public static readonly IReadOnlySet<string> StillVulnerableCves =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CVE-2021-44228" };
}

/// <summary>ใช้เมื่อ <c>Integration:UseMock=false</c> (workshop ยังไม่มี real adapter): ไม่เรียกระบบจริง คืนรายการว่าง / rescan ล้มเหลว (EX-74).</summary>
public sealed class UnavailableScannerAdapter : IScannerAdapter
{
    public Task<IReadOnlyList<RawFindingRow>> PullFindingsAsync(string sourceSystem, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<RawFindingRow>>([]);

    public Task<RescanResponse> RequestRescanAsync(RescanRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new RescanResponse(string.Empty, RescanStatus.Failed));
}
