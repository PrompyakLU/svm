using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SvmApi.Application.Reference;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Application.Intake;

/// <summary>
/// Dedup key (BR-12 · data-model unique filtered index): asset + cve + location + port + component.
/// เทียบแบบไม่สนตัวพิมพ์ (สอดคล้องกับ collation CI ของ SQL Server ที่ unique index ใช้) และ null == null.
/// </summary>
public sealed class DedupKey : IEquatable<DedupKey>
{
    public DedupKey(Guid assetId, string? cveId, string? location, int? port, string? component)
    {
        AssetId = assetId;
        CveId = Clean(cveId);
        Location = Clean(location);
        Port = port;
        Component = Clean(component);
    }

    public Guid AssetId { get; }
    public string? CveId { get; }
    public string? Location { get; }
    public int? Port { get; }
    public string? Component { get; }

    /// <summary>
    /// fingerprint (SHA-256 16 hex) สำหรับ log/trace — ไม่เปิดเผย location/component ซึ่งอาจมี URL/parameter ภายใน.
    /// </summary>
    public string Fingerprint
    {
        get
        {
            var raw = string.Join(
                '\u001F',
                AssetId.ToString("N"),
                CveId?.ToUpperInvariant() ?? string.Empty,
                Location?.ToUpperInvariant() ?? string.Empty,
                Port?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                Component?.ToUpperInvariant() ?? string.Empty);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16].ToLowerInvariant();
        }
    }

    public bool Equals(DedupKey? other)
        => other is not null
           && AssetId == other.AssetId
           && Port == other.Port
           && string.Equals(CveId, other.CveId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(Location, other.Location, StringComparison.OrdinalIgnoreCase)
           && string.Equals(Component, other.Component, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is DedupKey k && Equals(k);

    public override int GetHashCode()
        => HashCode.Combine(
            AssetId,
            Port,
            CveId is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(CveId),
            Location is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Location),
            Component is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Component));

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>instance ที่ยัง active (ไม่ Archived) บน asset เดียวกัน + CVE เดียวกัน — ผู้สมัครของ dedup.</summary>
public sealed record DedupCandidate(Guid InstanceId, DedupKey Key, string Title, VulnerabilityStatus Status);

public enum DedupMatchKind
{
    /// <summary>ไม่ซ้ำ — สร้าง instance ใหม่.</summary>
    New,

    /// <summary>dedup key ตรงกับ instance เดียว — เชื่อม finding + ปรับ Last Detected (FR-12).</summary>
    Exact,

    /// <summary>ตัดสินอัตโนมัติไม่ได้ — เข้าคิว NeedsReview (EX-45).</summary>
    Ambiguous,
}

public sealed record DedupMatch(DedupMatchKind Kind, Guid? InstanceId, IReadOnlyList<Guid> CandidateIds, bool IsRecurrence = false)
{
    public static DedupMatch New { get; } = new(DedupMatchKind.New, null, []);
}

/// <summary>
/// กติกา dedup แบบ pure (US-012 · FR-11/12 · BR-12/13 · EX-45):
/// <list type="number">
/// <item>key ตรง (asset+cve+location+port+component) → <see cref="DedupMatchKind.Exact"/>;
///   ยกเว้นไม่มี CVE และชื่อช่องโหว่ต่างกัน → <see cref="DedupMatchKind.Ambiguous"/> (key เดียวกันแต่อาจเป็นคนละช่องโหว่).</item>
/// <item>ไม่มี key ตรง แต่มี candidate ที่ field ต่างกันเฉพาะฝั่งหนึ่งว่าง (เช่น finding ไม่ระบุ port แต่ instance มี 443)
///   → <see cref="DedupMatchKind.Ambiguous"/> (ข้อมูลไม่พอจะตัดสิน — ให้คนตัดสิน).</item>
/// <item>นอกนั้น → <see cref="DedupMatchKind.New"/>.</item>
/// </list>
/// ถ้า Exact กับ instance ที่ปิดไปแล้ว (Resolved/Closed) ตั้ง <see cref="DedupMatch.IsRecurrence"/> (BR-13 — การ reopen เป็นงานของ workflow).
/// </summary>
public static class DedupMatcher
{
    public static DedupMatch Match(DedupKey key, string findingName, IReadOnlyList<DedupCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(candidates);

        var relevant = candidates
            .Where(c => c.Status != VulnerabilityStatus.Archived
                        && c.Key.AssetId == key.AssetId
                        && string.Equals(c.Key.CveId, key.CveId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var exact = relevant.Where(c => c.Key.Equals(key)).ToList();
        if (exact.Count > 0)
        {
            var sameVulnerability = key.CveId is not null
                ? exact
                : exact.Where(c => SameTitle(c.Title, findingName)).ToList();

            if (sameVulnerability.Count == 1 && exact.Count == 1)
            {
                var hit = sameVulnerability[0];
                return new DedupMatch(DedupMatchKind.Exact, hit.InstanceId, [hit.InstanceId], IsClosed(hit.Status));
            }

            return new DedupMatch(DedupMatchKind.Ambiguous, null, exact.Select(c => c.InstanceId).ToList());
        }

        var partial = relevant
            .Where(c => (key.CveId is not null || SameTitle(c.Title, findingName)) && IsPartialMatch(key, c.Key))
            .Select(c => c.InstanceId)
            .ToList();

        return partial.Count > 0
            ? new DedupMatch(DedupMatchKind.Ambiguous, null, partial)
            : DedupMatch.New;
    }

    /// <summary>ทุก field (location/port/component) เท่ากัน หรือฝั่งใดฝั่งหนึ่งว่าง — และมีอย่างน้อยหนึ่ง field ที่ว่างไม่ตรงกัน.</summary>
    public static bool IsPartialMatch(DedupKey a, DedupKey b)
    {
        static bool Compatible(string? x, string? y)
            => x is null || y is null || string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

        var compatible = Compatible(a.Location, b.Location)
                         && (a.Port is null || b.Port is null || a.Port == b.Port)
                         && Compatible(a.Component, b.Component);

        return compatible && !a.Equals(b);
    }

    public static bool SameTitle(string? a, string? b)
        => string.Equals(NormalizeTitle(a), NormalizeTitle(b), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeTitle(string? s)
        => Regex.Replace((s ?? string.Empty).Trim(), @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static bool IsClosed(VulnerabilityStatus status)
        => status is VulnerabilityStatus.Resolved or VulnerabilityStatus.Closed;
}

/// <summary>ผลการตัดสิน reference data ของ Intake (EX-16 vs EX-47/48/49).</summary>
public sealed record ReferenceDecision(IReadOnlyList<FieldError> Rejections, IReadOnlyList<string> ReviewIssues)
{
    public bool Rejected => Rejections.Count > 0;
    public bool NeedsReview => ReviewIssues.Count > 0;
}

/// <summary>
/// Intake ตัดสินผล <see cref="ReferenceEnrichment"/> (4.2 ไม่ปฏิเสธเอง):
/// <list type="bullet">
/// <item>ปฏิเสธ (EX-16 — ค่าไม่ตรงประเภท): <c>CVE_ID_INVALID</c>, <c>CWE_ID_INVALID</c>, <c>CVSS_SCORE_INVALID</c>.</item>
/// <item>บันทึกแต่ NeedsReview: CVE/CWE ไม่พบ (EX-47), CVSS version ไม่รองรับ/ไม่ระบุ (EX-48), vector ผิดรูปแบบ (EX-49).</item>
/// </list>
/// </summary>
public static class ReferencePolicy
{
    public static ReferenceDecision Evaluate(ReferenceEnrichment enrichment)
    {
        ArgumentNullException.ThrowIfNull(enrichment);

        var rejections = new List<FieldError>();
        if (enrichment.HasIssue(ReferenceIssueCodes.CveIdInvalid))
        {
            rejections.Add(new FieldError(IntakeTemplate.CveId, IntakeErrorCodes.InvalidIdentifier));
        }

        if (enrichment.HasIssue(ReferenceIssueCodes.CweIdInvalid))
        {
            rejections.Add(new FieldError(IntakeTemplate.CweId, IntakeErrorCodes.InvalidIdentifier));
        }

        if (enrichment.HasIssue(ReferenceIssueCodes.CvssScoreInvalid))
        {
            rejections.Add(new FieldError(IntakeTemplate.CvssBaseScore, IntakeErrorCodes.InvalidScore));
        }

        var rejectedCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            ReferenceIssueCodes.CveIdInvalid, ReferenceIssueCodes.CweIdInvalid, ReferenceIssueCodes.CvssScoreInvalid,
        };

        var review = rejections.Count > 0
            ? []
            : enrichment.Issues.Where(i => !rejectedCodes.Contains(i)).Distinct(StringComparer.Ordinal).ToList();

        return new ReferenceDecision(rejections, review);
    }

    /// <summary>
    /// validationStatus ของ finding: คิว dedup (ambiguous) หรือมี reference issue = NeedsReview; นอกนั้น Valid.
    /// (finding ที่ถูกปฏิเสธไม่ถูกบันทึก.)
    /// </summary>
    public static ValidationStatus StatusFor(bool ambiguous, bool referenceNeedsReview)
        => ambiguous || referenceNeedsReview ? ValidationStatus.NeedsReview : ValidationStatus.Valid;
}

public enum IdempotencyDecision
{
    /// <summary>ไม่มี transactionId หรือยังไม่เคยใช้ — นำเข้าใหม่.</summary>
    Proceed,

    /// <summary>transactionId นี้นำเข้าเสร็จแล้วโดยผู้ใช้เดียวกัน — คืนผลเดิม ไม่สร้างข้อมูลซ้ำ (BR-36/EX-17).</summary>
    Replay,

    /// <summary>batch เดิมยังประมวลผลอยู่.</summary>
    InProgress,

    /// <summary>transactionId ถูกใช้โดยผู้ใช้อื่น — ปฏิเสธโดยไม่เปิดเผยรายละเอียด batch ของผู้อื่น.</summary>
    Conflict,
}

/// <summary>ข้อมูลขั้นต่ำของ batch เดิมที่ใช้ตัดสิน idempotency.</summary>
public sealed record ExistingBatch(Guid BatchId, Guid ImportedBy, ImportBatchStatus Status);

/// <summary>กติกา idempotency ของ import ด้วย <c>transactionId</c> (BR-36 · EX-17) แบบ pure.</summary>
public static partial class ImportIdempotency
{
    [GeneratedRegex(@"^[A-Za-z0-9._:\-]{1,200}$", RegexOptions.CultureInvariant)]
    private static partial Regex TransactionIdPattern();

    /// <summary>trim; ว่าง = null (ไม่ใช้ idempotency); รูปแบบไม่ถูกต้อง = invalid.</summary>
    public static (bool IsValid, string? Value) NormalizeTransactionId(string? transactionId)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            return (true, null);
        }

        var t = transactionId.Trim();
        return TransactionIdPattern().IsMatch(t) ? (true, t) : (false, null);
    }

    public static IdempotencyDecision Decide(ExistingBatch? existing, Guid currentUserId)
    {
        if (existing is null)
        {
            return IdempotencyDecision.Proceed;
        }

        if (existing.ImportedBy != currentUserId)
        {
            return IdempotencyDecision.Conflict;
        }

        return existing.Status == ImportBatchStatus.Processing ? IdempotencyDecision.InProgress : IdempotencyDecision.Replay;
    }

    /// <summary>สถานะ batch จากจำนวนแถว (FR-05 · EX-18): ครบ = Success, ไม่มีสำเร็จ = Failed, บางส่วน = Partial.</summary>
    public static ImportBatchStatus StatusFor(int totalRows, int successCount)
    {
        if (totalRows <= 0 || successCount <= 0)
        {
            return ImportBatchStatus.Failed;
        }

        return successCount >= totalRows ? ImportBatchStatus.Success : ImportBatchStatus.Partial;
    }
}
