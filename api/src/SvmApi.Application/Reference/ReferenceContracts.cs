using SvmApi.Application.Common;

namespace SvmApi.Application.Reference;

/// <summary>master_cve (read model). EX-47, US-013/014.</summary>
public sealed record CveDto(
    string CveId,
    string? Description,
    DateTime? PublishedDate,
    bool IsKev,
    DateTime? KevDateAdded);

/// <summary>master_cwe (read model). US-013.</summary>
public sealed record CweDto(string CweId, string Name, string? Description);

/// <summary>
/// ข้อมูลอ้างอิงที่ finding ส่งมา (ค่าดิบ ยังไม่ normalize). FR-13/14.
/// <para><paramref name="CvssVersion"/> รับ "3.1", "v3.1", "CVSS:3.1", "CVSSv3.1" ได้; ถ้าว่างจะอนุมานจาก prefix ของ vector.</para>
/// <para><paramref name="CvssVector"/> ต้องมี prefix "CVSS:x.y/" (ถ้ามี prefix ต้องตรง version).</para>
/// <para><paramref name="CvssBaseScore"/> 0.0–10.0 ทศนิยมไม่เกิน 1 ตำแหน่ง (data-model decimal(4,1)).</para>
/// </summary>
public sealed record ReferenceEnrichmentRequest(
    string? CveId,
    string? CweId,
    string? CvssVersion,
    string? CvssVector,
    decimal? CvssBaseScore = null);

/// <summary>CVSS qualitative severity (v3.x/v4.0 rating scale). ค่าเป็น string คงที่เพื่อเก็บ/แสดงผลตรง ๆ.</summary>
public static class CvssSeverities
{
    public const string None = "None";
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
    public const string Critical = "Critical";
}

/// <summary>
/// รหัสเหตุที่ต้องทำเครื่องหมาย "ตรวจสอบ" (ไม่ปฏิเสธ finding). ค่าเป็น string คงที่เพื่อเก็บ/แสดงผลได้ตรง ๆ.
/// </summary>
public static class ReferenceIssueCodes
{
    /// <summary>EX-47: CVE ไม่พบใน reference data (หรือรูปแบบ id ไม่ถูกต้อง) — คงค่าที่ส่งมา.</summary>
    public const string CveNotFound = "CVE_NOT_FOUND";

    /// <summary>CWE ไม่พบใน reference data (หรือรูปแบบ id ไม่ถูกต้อง) — คงค่าที่ส่งมา.</summary>
    public const string CweNotFound = "CWE_NOT_FOUND";

    /// <summary>
    /// EX-47 (ละเอียดขึ้น): CVE id ผิดรูปแบบ (ไม่ตรง CVE-YYYY-NNNN หรือยาวเกิน <see cref="ReferenceIds.MaxCveIdLength"/>).
    /// มาคู่กับ <see cref="CveNotFound"/> เสมอ — Intake ใช้แยก "พิมพ์ผิด" ออกจาก "ยังไม่มีใน seed".
    /// </summary>
    public const string CveIdInvalid = "CVE_ID_INVALID";

    /// <summary>CWE id ผิดรูปแบบ (ไม่ตรง CWE-NNN หรือยาวเกิน <see cref="ReferenceIds.MaxCweIdLength"/>). มาคู่กับ <see cref="CweNotFound"/> เสมอ.</summary>
    public const string CweIdInvalid = "CWE_ID_INVALID";

    /// <summary>EX-48: CVSS version ไม่รองรับ (ไม่อยู่ใน config_cvssVersion หรือ isSupported = false).</summary>
    public const string CvssVersionUnsupported = "CVSS_VERSION_UNSUPPORTED";

    /// <summary>มี vector หรือ base score แต่ระบุ version ไม่ได้ (ไม่ส่งมาและอนุมานจาก prefix ไม่ได้).</summary>
    public const string CvssVersionMissing = "CVSS_VERSION_MISSING";

    /// <summary>EX-49: CVSS vector ผิดรูปแบบ หรือ prefix ไม่ตรงกับ version ที่ระบุ.</summary>
    public const string CvssVectorInvalid = "CVSS_VECTOR_INVALID";

    /// <summary>
    /// FR-14/EX-16: base score นอกช่วง 0.0–10.0 หรือทศนิยมเกิน 1 ตำแหน่ง. Enrich แค่ทำเครื่องหมาย;
    /// Intake (4.3) เป็นผู้ตัดสินว่าจะปฏิเสธรายการตาม EX-16 หรือไม่.
    /// </summary>
    public const string CvssScoreInvalid = "CVSS_SCORE_INVALID";
}

/// <summary>
/// ผลการ enrich: ไม่ throw แม้ id ไม่รู้จัก — ใช้ <see cref="Issues"/> ทำเครื่องหมายให้ตรวจสอบแทน.
/// <list type="bullet">
/// <item><see cref="CveId"/>/<see cref="CweId"/>: ถ้าพบใน master = id ตัวพิมพ์มาตรฐานจาก master; ไม่พบ = ค่าที่ส่งมา (trim) ตาม EX-47.</item>
/// <item><see cref="CvssVersion"/>: version ที่ normalize แล้ว (เช่น "3.1") หรือที่อนุมานจาก vector.</item>
/// <item><see cref="CvssBaseScore"/>: score ที่ส่งมาเมื่อถูกต้อง, null เมื่อไม่ส่ง/ไม่ถูกต้อง (ห้ามใช้ score ผิดคำนวณ risk).</item>
/// <item><see cref="CvssSeverity"/>: <see cref="CvssSeverities"/> จาก score เมื่อ version รองรับและ score ถูกต้อง, ไม่เช่นนั้น null.</item>
/// </list>
/// </summary>
public sealed record ReferenceEnrichment(
    string? CveId,
    CveDto? Cve,
    string? CweId,
    CweDto? Cwe,
    string? CvssVersion,
    string? CvssVector,
    bool CvssVersionSupported,
    bool CvssVectorValid,
    bool IsKev,
    IReadOnlyList<string> Issues,
    decimal? CvssBaseScore = null,
    string? CvssSeverity = null)
{
    /// <summary>true เมื่อมีเหตุอย่างน้อยหนึ่งข้อ (finding ถูกบันทึกแต่ต้องตรวจสอบ — EX-47/48/49).</summary>
    public bool NeedsReview => Issues.Count > 0;

    /// <summary>วันที่ CVE ถูกเพิ่มใน KEV (US-014); null เมื่อไม่ใช่ KEV หรือไม่พบ CVE.</summary>
    public DateTime? KevDateAdded => IsKev ? Cve?.KevDateAdded : null;

    /// <summary>true เมื่อมี <paramref name="issueCode"/> (ค่าจาก <see cref="ReferenceIssueCodes"/>) ในผล enrich.</summary>
    public bool HasIssue(string issueCode) => Issues.Contains(issueCode, StringComparer.Ordinal);
}

public sealed record CveQuery(string? Search, bool? IsKev, int Page = 1, int PageSize = 25);

public sealed record CweQuery(string? Search, int Page = 1, int PageSize = 25);

/// <summary>
/// Reference data (C5 · US-013/014 · FR-13/14/18). Read-only; ข้อมูล seed ถูก cache ในหน่วยความจำ
/// จึงเรียก <see cref="EnrichAsync"/>/<see cref="IsKevAsync"/> ต่อ finding ได้โดยไม่ยิง DB ซ้ำ.
/// </summary>
public interface IReferenceDataService
{
    /// <summary>เชื่อม CVE/CWE/CVSS ของ finding กับ reference data. ไม่ throw เมื่อ id/version/vector ผิด (EX-47/48/49).</summary>
    Task<ReferenceEnrichment> EnrichAsync(ReferenceEnrichmentRequest request, CancellationToken cancellationToken);

    /// <summary>CVE อยู่ใน KEV (seed) หรือไม่ (US-014). id ที่ไม่รู้จัก/ว่าง = false.</summary>
    Task<bool> IsKevAsync(string? cveId, CancellationToken cancellationToken);

    Task<CveDto?> GetCveAsync(string cveId, CancellationToken cancellationToken);

    Task<CweDto?> GetCweAsync(string cweId, CancellationToken cancellationToken);

    Task<PagedResult<CveDto>> SearchCvesAsync(CveQuery query, CancellationToken cancellationToken);

    Task<PagedResult<CweDto>> SearchCwesAsync(CweQuery query, CancellationToken cancellationToken);
}
