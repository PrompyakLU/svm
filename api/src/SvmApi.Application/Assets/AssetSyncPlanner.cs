using SvmApi.Domain.Assets;

namespace SvmApi.Application.Assets;

public enum SyncAction
{
    Create,
    Update,
    Unchanged,

    /// <summary>map/validate ข้อมูลจาก CMDB ไม่ได้ (นับเป็น unmappedCount) — ไม่แตะข้อมูลเดิม.</summary>
    Unmapped,
}

/// <summary>ข้อมูล CMDB ที่ map แล้ว. <c>Status</c>/<c>Criticality</c> เป็น null = source ไม่ได้ระบุ.</summary>
public sealed record MappedCmdbAsset(
    string AssetCode,
    AssetFields Fields,
    AssetStatus? Status,
    AssetCriticality? Criticality,
    string? OwnerUsername,
    OwnerType OwnerType,
    string? TeamName);

public sealed record SyncDecision(
    SyncAction Action,
    string ExternalId,
    MappedCmdbAsset? Mapped,
    IReadOnlyList<string> ChangedFields,
    IReadOnlyList<string> Problems);

/// <summary>
/// ตรรกะตัดสินใจของ CMDB sync แบบ pure (FR-02, EX-30/31, BR-38): ไม่แตะ DB/เวลา/IO จึง test ได้โดยตรง.
/// นโยบาย:
/// <list type="bullet">
/// <item>upsert ด้วย assetCode (ไม่สนตัวพิมพ์) — idempotent: ข้อมูลเดิมซ้ำ = Unchanged</item>
/// <item>CMDB เป็นเจ้าของ field ทั่วไป (name/type/ip/hostname/url/environment/BU/exposure/cloud/repo/status) และเขียนทับได้</item>
/// <item>criticality: เติมให้เฉพาะเมื่อ asset ยังไม่มี — ไม่เขียนทับค่าที่ผู้รับผิดชอบกำหนด/ยืนยันไว้แล้ว (FR-04)</item>
/// <item>owners: ใช้เฉพาะตอนสร้าง asset ใหม่ — ไม่แก้ ownership ที่มีอยู่</item>
/// <item>asset ที่หายจาก feed ไม่ถูกลบ/เปลี่ยนสถานะ — แค่ถูกระบุให้ทบทวน (<see cref="FindMissing"/>)</item>
/// </list>
/// </summary>
public static class AssetSyncPlanner
{
    public const string UnknownExternalId = "(missing)";

    public static SyncDecision Decide(Asset? existing, CmdbAssetRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var errors = new AssetErrorBag();
        var externalId = AssetRules.ParseAssetCode(errors, "externalId", record.ExternalId);
        var fields = AssetRules.ParseFields(
            errors, record.Name, record.AssetType, record.IpAddress, record.Hostname, record.Url,
            record.Environment, record.BusinessUnit, record.InternetExposure, record.CloudResourceId,
            record.RepositoryId);
        var status = AssetRules.ParseEnum<AssetStatus>(errors, "status", record.Status, required: false);
        var criticality = AssetRules.ParseEnum<AssetCriticality>(errors, "criticality", record.Criticality, required: false);
        var ownerType = AssetRules.ParseEnum<OwnerType>(errors, "ownerType", record.OwnerType, required: false);
        var team = AssetRules.ParseText(errors, "teamName", record.TeamName, AssetRules.MaxTeamNameLength, required: false);
        var owner = AssetRules.ParseText(errors, "ownerUsername", record.OwnerUsername, 100, required: false);

        if (errors.HasErrors || externalId is null || fields is null)
        {
            var problems = errors.ToDictionary()
                .SelectMany(p => p.Value.Select(m => m))
                .ToList();
            return new SyncDecision(
                SyncAction.Unmapped, AssetRules.Clean(record.ExternalId) ?? UnknownExternalId, null, [], problems);
        }

        var mapped = new MappedCmdbAsset(
            externalId, fields, status, criticality, owner, ownerType ?? OwnerType.AssetOwner, team);

        if (existing is null)
        {
            return new SyncDecision(SyncAction.Create, externalId, mapped, [], []);
        }

        var changed = Diff(existing, mapped);
        return new SyncDecision(
            changed.Count == 0 ? SyncAction.Unchanged : SyncAction.Update, externalId, mapped, changed, []);
    }

    /// <summary>ชื่อ field ที่จะเปลี่ยนเมื่อ apply (เรียงตามลำดับคงที่).</summary>
    public static IReadOnlyList<string> Diff(Asset existing, MappedCmdbAsset mapped)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(mapped);

        var f = mapped.Fields;
        var changed = new List<string>();

        if (!Same(existing.Name, f.Name)) { changed.Add("name"); }
        if (existing.AssetType != f.AssetType) { changed.Add("assetType"); }
        if (!Same(existing.IpAddress, f.IpAddress)) { changed.Add("ipAddress"); }
        if (!Same(existing.Hostname, f.Hostname)) { changed.Add("hostname"); }
        if (!Same(existing.Url, f.Url)) { changed.Add("url"); }
        if (existing.Environment != f.Environment) { changed.Add("environment"); }
        if (!Same(existing.BusinessUnit, f.BusinessUnit)) { changed.Add("businessUnit"); }
        if (existing.InternetExposure != f.InternetExposure) { changed.Add("internetExposure"); }
        if (!Same(existing.CloudResourceId, f.CloudResourceId)) { changed.Add("cloudResourceId"); }
        if (!Same(existing.RepositoryId, f.RepositoryId)) { changed.Add("repositoryId"); }
        if (mapped.Status is { } status && existing.Status != status) { changed.Add("status"); }
        if (existing.Criticality is null && mapped.Criticality is not null) { changed.Add("criticality"); }

        return changed;
    }

    /// <summary>เขียนเฉพาะ field ที่ <see cref="Diff"/> ระบุลงบน asset เดิม (ไม่แตะ Id/AssetCode/audit columns/ownership).</summary>
    public static void ApplyUpdate(Asset target, MappedCmdbAsset mapped)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(mapped);

        var f = mapped.Fields;
        target.Name = f.Name;
        target.AssetType = f.AssetType;
        target.IpAddress = f.IpAddress;
        target.Hostname = f.Hostname;
        target.Url = f.Url;
        target.Environment = f.Environment;
        target.BusinessUnit = f.BusinessUnit;
        target.InternetExposure = f.InternetExposure;
        target.CloudResourceId = f.CloudResourceId;
        target.RepositoryId = f.RepositoryId;

        if (mapped.Status is { } status)
        {
            target.Status = status;
        }

        if (target.Criticality is null && mapped.Criticality is not null)
        {
            target.Criticality = mapped.Criticality;
        }
    }

    /// <summary>สร้าง entity ใหม่จากข้อมูล CMDB (ผู้เรียกกำหนด Id/audit columns/ownership).</summary>
    public static Asset NewAsset(MappedCmdbAsset mapped)
    {
        ArgumentNullException.ThrowIfNull(mapped);

        var asset = new Asset { AssetCode = mapped.AssetCode, Status = AssetStatus.Active };
        ApplyUpdate(asset, mapped);
        return asset;
    }

    /// <summary>
    /// asset (Active) ที่ไม่ปรากฏใน feed รอบล่าสุด → ต้องทบทวน (EX-31, BR-38) — ไม่ลบ/ไม่เปลี่ยนสถานะ.
    /// เทียบ assetCode แบบไม่สนตัวพิมพ์; รหัสที่ feed ส่งมาแต่ map ไม่ได้ก็นับว่า "ปรากฏ".
    /// </summary>
    public static IReadOnlyList<string> FindMissing(IEnumerable<string> activeAssetCodes, IEnumerable<string?> feedExternalIds)
    {
        ArgumentNullException.ThrowIfNull(activeAssetCodes);
        ArgumentNullException.ThrowIfNull(feedExternalIds);

        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in feedExternalIds)
        {
            var clean = AssetRules.Clean(id);
            if (clean is not null)
            {
                present.Add(clean);
            }
        }

        return activeAssetCodes
            .Where(code => !present.Contains(code))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);
}
