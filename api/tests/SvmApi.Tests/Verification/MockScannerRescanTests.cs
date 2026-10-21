using SvmApi.Application.Verification;
using SvmApi.Infrastructure.Integration.Mock;

namespace SvmApi.Tests.Verification;

/// <summary>Mock scanner rescan (integration.md · EX-74/75): deterministic, ไม่เรียกระบบภายนอก.</summary>
public class MockScannerRescanTests
{
    private static RescanRequest Req(string assetCode = "SVM-APP-001", string? cve = "CVE-2021-44228")
        => new(Guid.NewGuid(), assetCode, Guid.NewGuid(), cve, "https://app001.example/api", 443, "log4j-core@2.14.1");

    [Fact]
    public async Task Known_vulnerable_cve_is_still_detected()
    {
        var r = await new MockScannerAdapter().RequestRescanAsync(Req(), CancellationToken.None);

        Assert.Equal(RescanStatus.StillDetected, r.Status);
        Assert.StartsWith("MOCK-", r.RequestId);
    }

    [Fact]
    public async Task Other_cve_or_no_cve_is_resolved()
    {
        var adapter = new MockScannerAdapter();

        Assert.Equal(RescanStatus.Resolved, (await adapter.RequestRescanAsync(Req(cve: "CVE-2014-0160"), CancellationToken.None)).Status);
        Assert.Equal(RescanStatus.Resolved, (await adapter.RequestRescanAsync(Req(cve: null), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Missing_asset_code_is_target_not_found()
    {
        var r = await new MockScannerAdapter().RequestRescanAsync(Req(assetCode: " "), CancellationToken.None);

        Assert.Equal(RescanStatus.TargetNotFound, r.Status);
    }

    [Fact]
    public async Task Same_input_gives_same_status_every_time()
    {
        var adapter = new MockScannerAdapter();
        var req = Req();

        var a = await adapter.RequestRescanAsync(req, CancellationToken.None);
        var b = await adapter.RequestRescanAsync(req, CancellationToken.None);

        Assert.Equal(a.Status, b.Status);
        Assert.NotEqual(a.RequestId, b.RequestId);
    }

    [Fact]
    public async Task Unavailable_adapter_reports_failure()
    {
        var r = await new UnavailableScannerAdapter().RequestRescanAsync(Req(), CancellationToken.None);

        Assert.Equal(RescanStatus.Failed, r.Status);
    }
}
