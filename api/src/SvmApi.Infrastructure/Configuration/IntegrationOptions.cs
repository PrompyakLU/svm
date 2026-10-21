namespace SvmApi.Infrastructure.Configuration;

/// <summary>
/// Config สำหรับ external-system adapters (CMDB/Scanner/Email/Teams/ITSM/SIEM).
/// bind จาก section "Integration". workshop default = ใช้ mock ทั้งหมด (ห้าม call ของจริง).
/// </summary>
public sealed class IntegrationOptions
{
    public const string SectionName = "Integration";

    /// <summary>ใช้ mock implementation ของ external adapters (default true สำหรับ workshop).</summary>
    public bool UseMock { get; set; } = true;
}
