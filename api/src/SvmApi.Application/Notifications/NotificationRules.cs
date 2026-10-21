using SvmApi.Application.Common;
using SvmApi.Domain.Notifications;

namespace SvmApi.Application.Notifications;

/// <summary>กติกา pure ของ NotificationModule (C14): ล้างข้อความ, ตัดผู้รับซ้ำ, เลือกช่องทาง.</summary>
public static class NotificationRules
{
    public const int MaxTypeLength = 50;
    public const int MaxSubjectLength = 300;
    public const int MaxBodyLength = 4000;

    /// <summary>type → UPPER_SNAKE สั้น ๆ; ว่าง = null (ผู้เรียกต้องปฏิเสธ).</summary>
    public static string? NormalizeType(string? type)
    {
        var cleaned = TextSanitizer.Clean(type, allowLineBreaks: false);
        return cleaned.Length == 0 ? null : TextSanitizer.Truncate(cleaned.ToUpperInvariant(), MaxTypeLength);
    }

    public static string Subject(string? title)
        => TextSanitizer.Truncate(TextSanitizer.Clean(title, allowLineBreaks: false), MaxSubjectLength);

    public static string Body(string? message)
        => TextSanitizer.Truncate(TextSanitizer.Clean(message, allowLineBreaks: true), MaxBodyLength);

    /// <summary>ตัด <see cref="Guid.Empty"/>, id ซ้ำ และผู้ถูกยกเว้น (เช่น ผู้กระทำเอง) โดยรักษาลำดับเดิม.</summary>
    public static IReadOnlyList<Guid> DistinctRecipients(IEnumerable<Guid> ids, Guid? exclude = null)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var seen = new HashSet<Guid>();
        var result = new List<Guid>();
        foreach (var id in ids)
        {
            if (id == Guid.Empty || id == exclude || !seen.Add(id))
            {
                continue;
            }

            result.Add(id);
        }

        return result;
    }

    /// <summary>
    /// ช่องทางที่ต้องส่ง: in-app เสมอ (ถ้าผู้รับ active); ช่องทางเร่งด่วน (Email เมื่อมีที่อยู่ + Teams) เฉพาะ <paramref name="urgent"/> (FR-41).
    /// ผู้รับที่ไม่ active = ไม่ส่งช่องทางภายนอก.
    /// </summary>
    public static IReadOnlyList<NotificationChannel> PlanChannels(bool urgent, bool recipientActive, bool hasEmailAddress)
    {
        var channels = new List<NotificationChannel> { NotificationChannel.InApp };
        if (urgent && recipientActive)
        {
            if (hasEmailAddress)
            {
                channels.Add(NotificationChannel.Email);
            }

            channels.Add(NotificationChannel.Teams);
        }

        return channels;
    }

    /// <summary>clamp page/pageSize (page ≥ 1, pageSize 1..100).</summary>
    public static (int Page, int PageSize) ClampPaging(int page, int pageSize)
        => (Math.Max(page, 1), Math.Clamp(pageSize, 1, 100));
}
