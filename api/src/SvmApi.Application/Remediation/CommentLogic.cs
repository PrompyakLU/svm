using System.Text.RegularExpressions;
using SvmApi.Application.Common;

namespace SvmApi.Application.Remediation;

/// <summary>กติกา pure ของ comment + @mention (US-023 · FR-30).</summary>
public static partial class CommentRules
{
    public const int MaxBodyLength = 2000;
    public const int MaxMentions = 20;

    // @username: ต้องไม่ติดหลังตัวอักษร/ตัวเลข (กัน a@b.com) ; username ยาว 2..64 ตัว [A-Za-z0-9._-]
    [GeneratedRegex(@"(?<![\p{L}\p{N}._%+\-@])@([A-Za-z0-9][A-Za-z0-9._\-]{1,63})", RegexOptions.CultureInvariant)]
    private static partial Regex MentionRegex();

    /// <summary>ล้าง control char แล้วตรวจ: ว่าง → <see cref="RemediationErrorCodes.BodyRequired"/>, ยาวเกิน → <see cref="RemediationErrorCodes.BodyTooLong"/>.</summary>
    public static (string? Body, string? ErrorCode) NormalizeBody(string? body)
    {
        var cleaned = TextSanitizer.Clean(body, allowLineBreaks: true);
        if (cleaned.Length == 0)
        {
            return (null, RemediationErrorCodes.BodyRequired);
        }

        return cleaned.Length > MaxBodyLength ? (null, RemediationErrorCodes.BodyTooLong) : (cleaned, null);
    }

    /// <summary>
    /// ดึง username ที่ถูก mention (ไม่ซ้ำ ไม่สนตัวพิมพ์ ตามลำดับที่พบ สูงสุด <see cref="MaxMentions"/>);
    /// trailing จุด/ขีด (เช่น "@officer1." ท้ายประโยค) ถูกตัดออก. ยังไม่ resolve กับ DB — ผู้เรียกต้องกรองเฉพาะผู้ใช้ที่มีอยู่/active/อยู่ใน scope.
    /// </summary>
    public static IReadOnlyList<string> ExtractMentions(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (Match m in MentionRegex().Matches(body))
        {
            var name = m.Groups[1].Value.TrimEnd('.', '-', '_');
            if (name.Length < 2 || !seen.Add(name))
            {
                continue;
            }

            result.Add(name);
            if (result.Count >= MaxMentions)
            {
                break;
            }
        }

        return result;
    }
}
