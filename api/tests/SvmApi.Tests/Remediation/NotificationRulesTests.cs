using SvmApi.Application.Notifications;
using SvmApi.Domain.Notifications;

namespace SvmApi.Tests.Remediation;

/// <summary>NotificationRules (pure · C14 · FR-38..42 · EX-73 · task 5.2): ผู้รับ, ช่องทาง, การล้างข้อความ, paging.</summary>
public class NotificationRulesTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid C = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    // ------------------------------------------------------------ recipients

    [Fact]
    public void Recipients_are_distinct_and_keep_first_seen_order()
        => Assert.Equal(
            $"{B}|{A}|{C}",
            string.Join('|', NotificationRules.DistinctRecipients([B, A, B, C, A])));

    [Fact]
    public void Empty_guid_and_excluded_actor_are_removed()
        => Assert.Equal(
            $"{B}|{C}",
            string.Join('|', NotificationRules.DistinctRecipients([Guid.Empty, A, B, C], exclude: A)));

    [Fact]
    public void No_recipients_yields_empty_list()
        => Assert.Empty(NotificationRules.DistinctRecipients([]));

    [Fact]
    public void Null_recipients_is_a_programming_error()
        => Assert.Throws<ArgumentNullException>(() => NotificationRules.DistinctRecipients(null!));

    // ------------------------------------------------------------ channels

    private static string Plan(bool urgent, bool active, bool hasEmail)
        => string.Join(',', NotificationRules.PlanChannels(urgent, active, hasEmail));

    [Fact]
    public void Normal_notification_is_in_app_only()
        => Assert.Equal(nameof(NotificationChannel.InApp), Plan(urgent: false, active: true, hasEmail: true));

    [Fact]
    public void Urgent_adds_email_and_teams_when_recipient_has_email()
        => Assert.Equal("InApp,Email,Teams", Plan(urgent: true, active: true, hasEmail: true));

    [Fact]
    public void Urgent_without_email_address_skips_email()
        => Assert.Equal("InApp,Teams", Plan(urgent: true, active: true, hasEmail: false));

    [Fact]
    public void Inactive_recipient_gets_no_external_channels()
        => Assert.Equal("InApp", Plan(urgent: true, active: false, hasEmail: true));

    // ------------------------------------------------------------ text

    [Theory]
    [InlineData("assignment", "ASSIGNMENT")]
    [InlineData("  sla_breach \u0007", "SLA_BREACH")]
    public void Type_is_normalized_to_upper_case(string input, string expected)
        => Assert.Equal(expected, NotificationRules.NormalizeType(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \u0000 ")]
    public void Blank_type_is_null(string? input)
        => Assert.Null(NotificationRules.NormalizeType(input));

    [Fact]
    public void Type_is_truncated_to_column_length()
        => Assert.Equal(NotificationRules.MaxTypeLength, NotificationRules.NormalizeType(new string('a', 200))!.Length);

    [Fact]
    public void Subject_is_single_line_and_limited()
    {
        var subject = NotificationRules.Subject("line1\r\nline2\u0000" + new string('x', 500));

        Assert.Equal(NotificationRules.MaxSubjectLength, subject.Length);
        Assert.DoesNotContain('\n', subject);
        Assert.DoesNotContain('\0', subject);
    }

    [Fact]
    public void Body_keeps_line_breaks_strips_controls_and_is_limited()
    {
        Assert.Equal("a\nb", NotificationRules.Body("a\r\nb\u0007"));
        Assert.Equal(NotificationRules.MaxBodyLength, NotificationRules.Body(new string('y', 10_000)).Length);
        Assert.Equal(string.Empty, NotificationRules.Body(null));
    }

    // ------------------------------------------------------------ paging

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-3, 1000, 1, 100)]
    [InlineData(2, 25, 2, 25)]
    public void Paging_is_clamped(int page, int pageSize, int expectedPage, int expectedSize)
        => Assert.Equal((expectedPage, expectedSize), NotificationRules.ClampPaging(page, pageSize));
}
