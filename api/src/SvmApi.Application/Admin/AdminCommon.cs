using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SvmApi.Application.Common;

namespace SvmApi.Application.Admin;

/// <summary>
/// version สำหรับ optimistic concurrency ของ config ที่ไม่มี rowVersion: hash (SHA-256, 16 hex) ของค่าที่แก้ไขได้ทั้งหมด.
/// ผู้ใช้ส่งกลับมาใน PUT; ไม่ตรงกับค่าปัจจุบัน = มีคนแก้ไปก่อน → 409. ไม่ส่ง = ไม่ตรวจ (last-write-wins, ยังมี audit).
/// ไม่ใช่ความลับ/ไม่ใช่ token — เป็นเพียงลายนิ้วมือของเนื้อหา.
/// </summary>
public static class ConfigVersion
{
    public const int Length = 16;

    public static string Of(params object?[] parts)
    {
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            var value = part switch
            {
                null => null,
                string s => s,
                bool b => b ? "1" : "0",
                decimal d => d.ToString("G29", CultureInfo.InvariantCulture),
                DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture),
                DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => part.ToString() ?? "~",
            };
            // Length prefixes distinguish embedded separators, empty text and null.
            sb.Append(value?.Length ?? -1).Append(':').Append(value);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash)[..Length].ToLowerInvariant();
    }

    /// <summary>true เมื่อไม่ได้ส่ง version มา (ไม่ตรวจ) หรือตรงกับค่าปัจจุบัน.</summary>
    public static bool IsCurrent(string? supplied, string current)
        => string.IsNullOrWhiteSpace(supplied)
           || string.Equals(supplied.Trim(), current, StringComparison.OrdinalIgnoreCase);
}

/// <summary>ตัวช่วย parse ค่า enum/ข้อความของคำขอ admin (pure).</summary>
public static class AdminParse
{
    /// <summary>
    /// parse ชื่อ enum แบบไม่สนตัวพิมพ์; ไม่รับตัวเลข (Enum.TryParse ยอมรับ "1") และต้องเป็นค่าที่นิยามไว้เท่านั้น.
    /// </summary>
    public static bool TryEnum<TEnum>(string? text, out TEnum value) where TEnum : struct, Enum
    {
        value = default;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !trimmed.All(char.IsLetter))
        {
            return false;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out value) && Enum.IsDefined(value);
    }

    /// <summary>ทำความสะอาดข้อความ (ตัด control/bidi/zero-width + trim); คืน null เมื่อว่าง.</summary>
    public static string? CleanText(string? value)
    {
        var cleaned = TextSanitizer.Clean(value, allowLineBreaks: false);
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    /// <summary>จำนวนตำแหน่งทศนิยมที่มีความหมายของ decimal (ไม่นับศูนย์ท้าย).</summary>
    public static int DecimalPlaces(decimal value)
    {
        var normalized = value / 1.000000000000000000000000000000m;
        return (decimal.GetBits(normalized)[3] >> 16) & 0xFF;
    }
}
