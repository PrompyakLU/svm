using SvmApi.Application.Assets;

namespace SvmApi.Tests.Assets;

/// <summary>ทดสอบการสร้างประวัติ criticality จาก audit trail (US-006 · FR-04) — pure.</summary>
public class AssetCriticalityHistoryTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private static AssetAuditEvent Ev(
        long id, string action, string? before, string? after, int minutes, Guid? user = null, string source = "AssetService")
        => new(id, action, user, before, after, source, T0.AddMinutes(minutes));

    [Fact]
    public void Build_CriticalitySet_CapturesWhoWhenReason()
    {
        var uid = Guid.NewGuid();
        var events = new[]
        {
            Ev(1, AssetAuditActions.Criticality, """{"criticality":null}""",
                """{"criticality":"High","reason":"customer data","changed":true}""", 0, uid),
        };

        var h = AssetCriticalityHistory.Build(events, new Dictionary<Guid, string> { [uid] = "Demo Asset Owner 1" });

        var e = Assert.Single(h);
        Assert.Null(e.Previous);
        Assert.Equal("High", e.New);
        Assert.Equal("customer data", e.Reason);
        Assert.True(e.Changed);
        Assert.Equal(uid, e.ChangedBy);
        Assert.Equal("Demo Asset Owner 1", e.ChangedByName);
        Assert.Equal(AssetCriticalityHistory.SourceManual, e.Source);
        Assert.Equal(DateTimeKind.Utc, e.ChangedAt.Kind);
    }

    [Fact]
    public void Build_Confirmation_IncludedAsNotChanged()
    {
        var h = AssetCriticalityHistory.Build(
        [
            Ev(1, AssetAuditActions.Criticality, """{"criticality":"High"}""",
                """{"criticality":"High","reason":"reviewed","changed":false}""", 0),
        ]);

        Assert.False(Assert.Single(h).Changed);
    }

    [Fact]
    public void Build_CreateAndUpdate_OnlyWhenCriticalityPresentOrChanged()
    {
        var h = AssetCriticalityHistory.Build(
        [
            Ev(1, AssetAuditActions.Create, null, """{"assetCode":"A","criticality":null}""", 0),
            Ev(2, AssetAuditActions.Update, """{"criticality":null,"name":"a"}""", """{"criticality":null,"name":"b"}""", 1),
            Ev(3, AssetAuditActions.Update, """{"criticality":null}""", """{"criticality":"Low"}""", 2, source: "CmdbSync"),
            Ev(4, AssetAuditActions.Create, null, """{"assetCode":"B","criticality":"Medium"}""", 3),
        ]);

        Assert.Equal(2, h.Count);
        Assert.Equal("Medium", h[0].New);
        Assert.Equal(AssetCriticalityHistory.SourceCreate, h[0].Source);
        Assert.Equal("Low", h[1].New);
        Assert.Equal(AssetCriticalityHistory.SourceCmdbSync, h[1].Source);
        Assert.Null(h[1].Reason);
    }

    [Fact]
    public void Build_NewestFirst_TieBrokenById()
    {
        var h = AssetCriticalityHistory.Build(
        [
            Ev(1, AssetAuditActions.Criticality, null, """{"criticality":"Low","reason":"r1"}""", 0),
            Ev(3, AssetAuditActions.Criticality, null, """{"criticality":"High","reason":"r3"}""", 5),
            Ev(2, AssetAuditActions.Criticality, null, """{"criticality":"Medium","reason":"r2"}""", 5),
        ]);

        Assert.Equal(["r3", "r2", "r1"], h.Select(x => x.Reason!).ToList());
    }

    [Fact]
    public void Build_IgnoresMalformedAndUnrelatedRows()
    {
        var h = AssetCriticalityHistory.Build(
        [
            Ev(1, AssetAuditActions.Criticality, null, "not json", 0),
            Ev(2, AssetAuditActions.Criticality, null, """["array"]""", 1),
            Ev(3, AssetAuditActions.Criticality, null, """{"reason":"no level"}""", 2),
            Ev(4, AssetAuditActions.SyncReview, null, """{"criticality":"High"}""", 3),
            Ev(5, AssetAuditActions.Update, "{bad", """{"criticality":"High"}""", 4),
        ]);

        Assert.Empty(h);
    }

    [Fact]
    public void Build_ChangedFlagMissing_DerivedFromValues()
    {
        var h = AssetCriticalityHistory.Build(
        [
            Ev(1, AssetAuditActions.Criticality, """{"criticality":"Low"}""", """{"criticality":"High","reason":"x"}""", 0),
        ]);

        Assert.True(Assert.Single(h).Changed);
    }
}
