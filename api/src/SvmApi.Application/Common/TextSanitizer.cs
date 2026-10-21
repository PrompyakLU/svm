using System.Text;

namespace SvmApi.Application.Common;

/// <summary>
/// ล้างอักขระควบคุมออกจากข้อความที่ผู้ใช้ส่งมา (comment, เหตุผล, notification) ก่อนเก็บ/แสดงผล.
/// ฝั่ง API ไม่ render HTML — การ encode ตอนแสดงผลเป็นหน้าที่ของ UI; ที่นี่กัน control char / bidi override / null byte เท่านั้น.
/// </summary>
public static class TextSanitizer
{
    /// <summary>
    /// ตัด control char (ยกเว้น \n และ \t เมื่อ <paramref name="allowLineBreaks"/>), แปลง CRLF/CR เป็น LF,
    /// ตัด zero-width/bidi override (U+200B..U+200F, U+202A..U+202E, U+2066..U+2069, U+FEFF) แล้ว trim.
    /// </summary>
    public static string Clean(string? value, bool allowLineBreaks)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c == '\r')
            {
                if (allowLineBreaks)
                {
                    if (i + 1 < value.Length && value[i + 1] == '\n')
                    {
                        continue; // CRLF → ปล่อย \n ตัวถัดไปจัดการ
                    }

                    sb.Append('\n');
                }
                else
                {
                    sb.Append(' ');
                }

                continue;
            }

            if (c is '\n' or '\t')
            {
                sb.Append(allowLineBreaks ? c : ' ');
                continue;
            }

            if (char.IsControl(c) || IsInvisibleFormatting(c))
            {
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString().Trim();
    }

    /// <summary>ตัดให้ไม่เกิน <paramref name="maxLength"/> โดยไม่ตัดกลาง surrogate pair.</summary>
    public static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var cut = maxLength;
        if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
        {
            cut--;
        }

        return value[..cut];
    }

    private static bool IsInvisibleFormatting(char c)
        => c is >= '\u200B' and <= '\u200F'
            or >= '\u202A' and <= '\u202E'
            or >= '\u2066' and <= '\u2069'
            or '\uFEFF';
}
