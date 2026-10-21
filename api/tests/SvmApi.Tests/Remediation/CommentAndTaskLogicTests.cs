using SvmApi.Application.Common;
using SvmApi.Application.Remediation;
using SvmApi.Domain.Remediation;

namespace SvmApi.Tests.Remediation;

/// <summary>CommentRules / TextSanitizer / TaskRules (pure · C8 · US-021/023 · FR-28/29/30 · EX-57/63 · task 5.2).</summary>
public class CommentAndTaskLogicTests
{
    // ------------------------------------------------------------ mention parsing

    [Fact]
    public void Extracts_distinct_mentions_in_order_ignoring_case()
    {
        var names = CommentRules.ExtractMentions("ดู @officer1 และ @Analyst1 ด้วย @OFFICER1");

        Assert.Equal("officer1|Analyst1", string.Join('|', names));
    }

    [Theory]
    [InlineData("contact me at john@example.com")]
    [InlineData("no mention here")]
    [InlineData("@")]
    [InlineData("@a")]
    [InlineData("user@@double")]
    [InlineData("")]
    public void Ignores_emails_and_malformed_mentions(string body)
        => Assert.Empty(CommentRules.ExtractMentions(body));

    [Fact]
    public void Null_body_has_no_mentions()
        => Assert.Empty(CommentRules.ExtractMentions(null));

    [Fact]
    public void Trailing_punctuation_is_not_part_of_the_username()
        => Assert.Equal("officer1", string.Join('|', CommentRules.ExtractMentions("please review, @officer1. thanks")));

    [Fact]
    public void Username_may_contain_dot_dash_underscore()
        => Assert.Equal("a.b-c_d", string.Join('|', CommentRules.ExtractMentions("hi @a.b-c_d!")));

    [Fact]
    public void Mention_count_is_capped()
    {
        var body = string.Join(' ', Enumerable.Range(0, 50).Select(i => $"@user{i}"));

        Assert.Equal(CommentRules.MaxMentions, CommentRules.ExtractMentions(body).Count);
    }

    [Fact]
    public void Mention_at_start_and_after_newline_is_found()
        => Assert.Equal("a1|b2", string.Join('|', CommentRules.ExtractMentions("@a1\n@b2")));

    // ------------------------------------------------------------ body normalization

    [Fact]
    public void Body_is_cleaned_of_control_chars_but_keeps_line_breaks()
    {
        var (body, error) = CommentRules.NormalizeBody("  line1\u0000\r\nline2\u0007\t ok \u202E ");

        Assert.Null(error);
        Assert.Equal("line1\nline2\t ok", body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    [InlineData("\u0000\u0001")]
    public void Blank_body_is_rejected(string? input)
    {
        var (body, error) = CommentRules.NormalizeBody(input);

        Assert.Null(body);
        Assert.Equal(RemediationErrorCodes.BodyRequired, error);
    }

    [Fact]
    public void Body_over_limit_is_rejected_and_at_limit_is_accepted()
    {
        Assert.Equal(RemediationErrorCodes.BodyTooLong, CommentRules.NormalizeBody(new string('a', CommentRules.MaxBodyLength + 1)).ErrorCode);
        Assert.Null(CommentRules.NormalizeBody(new string('a', CommentRules.MaxBodyLength)).ErrorCode);
    }

    [Fact]
    public void Html_is_not_interpreted_or_stripped_by_the_api()
    {
        var (body, _) = CommentRules.NormalizeBody("<b>bold</b> & <script>x</script>");

        Assert.Equal("<b>bold</b> & <script>x</script>", body);
    }

    // ------------------------------------------------------------ sanitizer

    [Fact]
    public void Sanitizer_single_line_mode_turns_breaks_into_spaces()
        => Assert.Equal("a b c", TextSanitizer.Clean("a\r\nb\nc", allowLineBreaks: false).Replace("  ", " "));

    [Fact]
    public void Truncate_does_not_split_surrogate_pairs()
    {
        var text = "ab😀cd"; // 😀 = 2 UTF-16 units at index 2..3

        var cut = TextSanitizer.Truncate(text, 3);

        Assert.Equal("ab", cut);
    }

    [Fact]
    public void Truncate_keeps_short_text()
        => Assert.Equal("abc", TextSanitizer.Truncate("abc", 10));

    // ------------------------------------------------------------ task update validation

    private static UpdateTaskRequest Req(
        string? rootCause = null, string? actionPlan = null, DateTime? target = null, string? changeRef = null,
        int? progress = null, string? status = null, string? obstacle = null, string? comment = null,
        string? rowVersion = "AAAAAAAAB9A=")
        => new(rootCause, actionPlan, target, changeRef, progress, status, obstacle, comment, rowVersion);

    [Fact]
    public void Valid_progress_update_passes()
    {
        var v = TaskRules.ValidateUpdate(Req(progress: 40, status: "InProgress"), null);

        Assert.True(v.IsValid);
        Assert.Equal(RemediationTaskStatus.InProgress, v.Status);
        Assert.NotNull(v.RowVersion);
    }

    [Fact]
    public void Null_request_is_invalid()
        => Assert.False(TaskRules.ValidateUpdate(null, null).IsValid);

    [Fact]
    public void Row_version_is_required_and_must_be_base64_of_8_bytes()
    {
        Assert.Equal(
            RemediationErrorCodes.RowVersionRequired,
            TaskRules.ValidateUpdate(Req(progress: 1, rowVersion: null), null).Errors["rowVersion"][0]);
        Assert.Equal(
            RemediationErrorCodes.InvalidRowVersion,
            TaskRules.ValidateUpdate(Req(progress: 1, rowVersion: "not-base64!"), null).Errors["rowVersion"][0]);
        Assert.Equal(
            RemediationErrorCodes.InvalidRowVersion,
            TaskRules.ValidateUpdate(Req(progress: 1, rowVersion: "AAEC"), null).Errors["rowVersion"][0]);
    }

    [Fact]
    public void Empty_update_is_rejected()
        => Assert.Equal(RemediationErrorCodes.NoChanges, TaskRules.ValidateUpdate(Req(), null).Errors["body"][0]);

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Progress_must_be_between_0_and_100(int progress)
        => Assert.Equal(RemediationErrorCodes.InvalidProgress, TaskRules.ValidateUpdate(Req(progress: progress), null).Errors["progressPercent"][0]);

    [Theory]
    [InlineData("0")]
    [InlineData("99")]
    [InlineData("Done")]
    public void Unknown_status_is_rejected(string status)
        => Assert.Equal(RemediationErrorCodes.InvalidTaskStatus, TaskRules.ValidateUpdate(Req(status: status), null).Errors["status"][0]);

    [Fact]
    public void Status_parsing_is_case_insensitive()
        => Assert.Equal(RemediationTaskStatus.Blocked, TaskRules.ValidateUpdate(Req(status: "blocked", obstacle: "waiting vendor"), null).Status);

    [Fact]
    public void Blocked_requires_an_obstacle_in_request_or_existing()
    {
        Assert.Equal(
            RemediationErrorCodes.ObstacleRequired,
            TaskRules.ValidateUpdate(Req(status: "Blocked"), existingObstacle: null).Errors["obstacle"][0]);
        Assert.Equal(
            RemediationErrorCodes.ObstacleRequired,
            TaskRules.ValidateUpdate(Req(status: "Blocked", obstacle: "  "), existingObstacle: "old").Errors["obstacle"][0]);
        Assert.True(TaskRules.ValidateUpdate(Req(status: "Blocked"), existingObstacle: "waiting on vendor").IsValid);
    }

    [Fact]
    public void Completed_with_progress_below_100_is_a_mismatch_but_without_progress_is_fine()
    {
        Assert.Equal(
            RemediationErrorCodes.ProgressMismatch,
            TaskRules.ValidateUpdate(Req(status: "Completed", progress: 80), null).Errors["progressPercent"][0]);
        Assert.True(TaskRules.ValidateUpdate(Req(status: "Completed"), null).IsValid);
        Assert.True(TaskRules.ValidateUpdate(Req(status: "Completed", progress: 100), null).IsValid);
    }

    [Fact]
    public void Long_text_fields_are_limited()
    {
        var tooLong = new string('x', TaskRules.MaxLongTextLength + 1);

        var v = TaskRules.ValidateUpdate(Req(rootCause: tooLong, actionPlan: tooLong, obstacle: tooLong), null);

        Assert.Equal(RemediationErrorCodes.TooLong, v.Errors["rootCause"][0]);
        Assert.Equal(RemediationErrorCodes.TooLong, v.Errors["actionPlan"][0]);
        Assert.Equal(RemediationErrorCodes.TooLong, v.Errors["obstacle"][0]);
        Assert.Equal(
            RemediationErrorCodes.TooLong,
            TaskRules.ValidateUpdate(Req(changeRef: new string('c', TaskRules.MaxChangeReferenceLength + 1)), null).Errors["changeReference"][0]);
    }

    [Fact]
    public void Absurd_target_dates_are_rejected()
    {
        Assert.True(TaskRules.ValidateUpdate(Req(target: new DateTime(1999, 1, 1)), null).Errors.ContainsKey("targetDate"));
        Assert.True(TaskRules.ValidateUpdate(Req(target: new DateTime(2300, 1, 1)), null).Errors.ContainsKey("targetDate"));
        Assert.True(TaskRules.ValidateUpdate(Req(target: new DateTime(2026, 6, 1)), null).IsValid);
    }

    [Fact]
    public void Comment_only_update_counts_as_a_change()
        => Assert.True(TaskRules.ValidateUpdate(Req(comment: "progress note"), null).IsValid);

    [Fact]
    public void Normalize_text_clears_blank_and_cleans_controls()
    {
        Assert.Null(TaskRules.NormalizeText("  \u0000 "));
        Assert.Equal("a\nb", TaskRules.NormalizeText("a\r\nb\u0007"));
        Assert.Equal("a b", TaskRules.NormalizeText("a\nb", allowLineBreaks: false));
    }

    // ------------------------------------------------------------ EX-63 target vs SLA

    [Fact]
    public void Target_date_after_due_date_exceeds_sla()
    {
        var due = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(TaskRules.ExceedsSla(due.AddDays(1), due));
        Assert.False(TaskRules.ExceedsSla(due, due));
        Assert.False(TaskRules.ExceedsSla(due.AddDays(-1), due));
        Assert.False(TaskRules.ExceedsSla(null, due));
    }

    // ------------------------------------------------------------ concurrency token (EX-57)

    [Fact]
    public void Concurrency_token_changes_when_task_is_updated()
    {
        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var updated = created.AddSeconds(5);

        Assert.NotEqual(
            TaskRules.ConcurrencyTokenBase64(created, null),
            TaskRules.ConcurrencyTokenBase64(created, updated));
    }

    [Fact]
    public void Token_is_8_bytes_and_round_trips_through_row_version_parser()
    {
        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var token = TaskRules.ConcurrencyTokenBase64(created, null);

        var v = TaskRules.ValidateUpdate(Req(progress: 1, rowVersion: token), null);

        Assert.True(v.IsValid);
        Assert.False(TaskRules.IsStale(v.RowVersion, created, null));
    }

    [Fact]
    public void Stale_when_missing_or_different()
    {
        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var updated = created.AddMinutes(1);
        var oldToken = TaskRules.ConcurrencyToken(created, null);

        Assert.True(TaskRules.IsStale(null, created, null));
        Assert.True(TaskRules.IsStale(oldToken, created, updated));
        Assert.False(TaskRules.IsStale(TaskRules.ConcurrencyToken(created, updated), created, updated));
    }
}
