using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SvmApi.Infrastructure.Audit;

/// <summary>
/// ปิดบังค่าของคีย์ที่ละเอียดอ่อนใน JSON ก่อนเก็บ Before/After ลง audit (NFR-20, BR-33; ห้ามเก็บ secret).
/// คีย์ที่ถือว่าอ่อนไหว = ชื่อคีย์ (ไม่สนตัวพิมพ์) มีส่วนประกอบใดส่วนหนึ่งของ
/// password, token, secret, authorization, cookie, hash, key, credential.
/// ถ้าไม่ใช่ JSON object/array (หรือ parse ไม่ได้) จะคืนค่าเดิมทุกตัวอักษร — ไม่ทำให้ข้อมูลหาย.
/// </summary>
public static class AuditRedactor
{
    /// <summary>ค่าที่ใช้แทนข้อมูลที่ถูกปิดบัง.</summary>
    public const string Mask = "***";

    private static readonly string[] SensitiveFragments =
    [
        "password", "token", "secret", "authorization", "cookie", "hash", "key", "credential",
    ];

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false,
        // คงอักขระไทย/Unicode ไว้ตามเดิม ไม่ escape เป็น \uXXXX
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// คืน JSON ที่ปิดบังค่าอ่อนไหวแล้ว. ถ้าไม่มีอะไรต้องปิดบัง คืน string เดิม (รูปแบบ/ช่องว่างไม่เปลี่ยน).
    /// </summary>
    public static string? Redact(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        var trimmed = json.AsSpan().TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return json;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return json;
        }
        catch (ArgumentException)
        {
            return json;
        }

        if (root is null)
        {
            return json;
        }

        return MaskNode(root) ? root.ToJsonString(WriteOptions) : json;
    }

    /// <summary>ชื่อคีย์นี้ถือว่าอ่อนไหวหรือไม่.</summary>
    public static bool IsSensitiveKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        foreach (var fragment in SensitiveFragments)
        {
            if (key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MaskNode(JsonNode? node)
    {
        var changed = false;

        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    var value = obj[key];
                    if (IsSensitiveKey(key))
                    {
                        // null คงเป็น null (ไม่ใช่การรั่วของค่า); ค่าที่ถูก mask แล้วไม่ต้องแก้ซ้ำ
                        if (value is not null && !IsAlreadyMasked(value))
                        {
                            obj[key] = JsonValue.Create(Mask);
                            changed = true;
                        }
                    }
                    else
                    {
                        changed |= MaskNode(value);
                    }
                }

                break;

            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    changed |= MaskNode(arr[i]);
                }

                break;
        }

        return changed;
    }

    private static bool IsAlreadyMasked(JsonNode value)
        => value is JsonValue v && v.TryGetValue<string>(out var s) && string.Equals(s, Mask, StringComparison.Ordinal);
}
