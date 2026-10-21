using System.Text.Json;

namespace SvmApi.Application.Assets;

/// <summary>ชื่อ audit action ของ AssetModule (ใช้ร่วมกันระหว่างผู้เขียน audit และผู้อ่านประวัติ).</summary>
public static class AssetAuditActions
{
    public const string Create = "asset.create";
    public const string Update = "asset.update";
    public const string Criticality = "asset.criticality.set";
    public const string Sync = "asset.sync";
    public const string SyncReview = "asset.sync.review";
}

/// <summary>audit row ที่เกี่ยวกับ asset หนึ่งตัว (ค่าดิบจาก trans_auditLog — ไม่มี hash).</summary>
public sealed record AssetAuditEvent(
    long Id,
    string Action,
    Guid? UserId,
    string? BeforeValue,
    string? AfterValue,
    string Source,
    DateTime Timestamp);

/// <summary>
/// ประวัติ criticality หนึ่งรายการ (FR-04: ผู้แก้ไข เวลา เหตุผล). <c>Changed=false</c> = ยืนยันค่าเดิม.
/// <c>Source</c>: Manual (PUT criticality) | Create | Update | CmdbSync.
/// </summary>
public sealed record CriticalityHistoryEntryDto(
    DateTime ChangedAt,
    Guid? ChangedBy,
    string? ChangedByName,
    string? Previous,
    string? New,
    string? Reason,
    bool Changed,
    string Source);

/// <summary>
/// สร้างประวัติ criticality จาก audit trail (immutable + hash chain) แบบ pure — data-model.md ไม่มีตาราง history เฉพาะ
/// จึงใช้ audit เป็นแหล่งเดียว (single source of truth). รวม:
/// <list type="bullet">
/// <item><c>asset.criticality.set</c> ทุกแถว (รวมการยืนยันค่าเดิม)</item>
/// <item><c>asset.create</c> ที่สร้างพร้อม criticality (เช่น CMDB sync)</item>
/// <item><c>asset.update</c> ที่ค่า criticality ก่อน/หลังต่างกัน (เช่น CMDB sync เติมค่าที่ยังว่าง)</item>
/// </list>
/// แถวที่ parse JSON ไม่ได้ถูกข้าม (ไม่ throw). เรียงใหม่สุดก่อน (timestamp desc, id desc).
/// </summary>
public static class AssetCriticalityHistory
{
    public const string SourceManual = "Manual";
    public const string SourceCreate = "Create";
    public const string SourceUpdate = "Update";
    public const string SourceCmdbSync = "CmdbSync";

    /// <summary>ค่า Source ของ audit ที่มาจาก CMDB sync (ตรงกับ AssetService.SyncAuditSource).</summary>
    public const string CmdbAuditSource = "CmdbSync";

    public static IReadOnlyList<CriticalityHistoryEntryDto> Build(
        IEnumerable<AssetAuditEvent> events,
        IReadOnlyDictionary<Guid, string>? userNames = null)
    {
        ArgumentNullException.ThrowIfNull(events);

        var result = new List<(AssetAuditEvent Event, CriticalityHistoryEntryDto Entry)>();

        foreach (var e in events)
        {
            if (e is null)
            {
                continue;
            }

            var entry = e.Action switch
            {
                AssetAuditActions.Criticality => FromCriticalitySet(e, userNames),
                AssetAuditActions.Create => FromCreate(e, userNames),
                AssetAuditActions.Update => FromUpdate(e, userNames),
                _ => null,
            };

            if (entry is not null)
            {
                result.Add((e, entry));
            }
        }

        return result
            .OrderByDescending(x => x.Event.Timestamp)
            .ThenByDescending(x => x.Event.Id)
            .Select(x => x.Entry)
            .ToList();
    }

    private static CriticalityHistoryEntryDto? FromCriticalitySet(AssetAuditEvent e, IReadOnlyDictionary<Guid, string>? names)
    {
        if (!TryRead(e.AfterValue, out var after) || !TryRead(e.BeforeValue, out var before, allowNull: true))
        {
            return null;
        }

        var previous = ReadString(before, "criticality");
        var next = ReadString(after, "criticality");
        if (next is null)
        {
            return null;
        }

        var changed = ReadBool(after, "changed") ?? !string.Equals(previous, next, StringComparison.Ordinal);
        return Entry(e, names, previous, next, ReadString(after, "reason"), changed, SourceManual);
    }

    private static CriticalityHistoryEntryDto? FromCreate(AssetAuditEvent e, IReadOnlyDictionary<Guid, string>? names)
    {
        if (!TryRead(e.AfterValue, out var after))
        {
            return null;
        }

        var next = ReadString(after, "criticality");
        return next is null
            ? null
            : Entry(e, names, null, next, null, true, IsCmdb(e) ? SourceCmdbSync : SourceCreate);
    }

    private static CriticalityHistoryEntryDto? FromUpdate(AssetAuditEvent e, IReadOnlyDictionary<Guid, string>? names)
    {
        if (!TryRead(e.AfterValue, out var after) || !TryRead(e.BeforeValue, out var before, allowNull: true))
        {
            return null;
        }

        var previous = ReadString(before, "criticality");
        var next = ReadString(after, "criticality");
        return string.Equals(previous, next, StringComparison.Ordinal)
            ? null
            : Entry(e, names, previous, next, null, true, IsCmdb(e) ? SourceCmdbSync : SourceUpdate);
    }

    private static CriticalityHistoryEntryDto Entry(
        AssetAuditEvent e, IReadOnlyDictionary<Guid, string>? names, string? previous, string? next,
        string? reason, bool changed, string source)
        => new(
            DateTime.SpecifyKind(e.Timestamp, DateTimeKind.Utc),
            e.UserId,
            e.UserId is { } uid && names is not null && names.TryGetValue(uid, out var name) ? name : null,
            previous,
            next,
            reason,
            changed,
            source);

    private static bool IsCmdb(AssetAuditEvent e) => string.Equals(e.Source, CmdbAuditSource, StringComparison.Ordinal);

    private static bool TryRead(string? json, out JsonElement element, bool allowNull = false)
    {
        element = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return allowNull;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            element = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadString(JsonElement obj, string name)
        => obj.ValueKind == JsonValueKind.Object
           && obj.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool? ReadBool(JsonElement obj, string name)
        => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;
}
