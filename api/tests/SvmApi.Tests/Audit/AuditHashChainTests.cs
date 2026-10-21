using SvmApi.Domain.Audit;
using SvmApi.Infrastructure.Audit;

namespace SvmApi.Tests.Audit;

/// <summary>ทดสอบ pure logic ของ hash chain (FR-68, BR-33, NFR-21, EX-100).</summary>
public class AuditHashChainTests
{
    private static readonly Guid UserA = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTime T0 = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private static string Hash(
        Guid? userId = null,
        string action = "login",
        string objectType = "Session",
        string objectId = "s1",
        string? before = null,
        string? after = "{}",
        string result = "Success",
        string source = "api",
        DateTime? timestamp = null,
        string? prevHash = null)
        => AuditHashChain.ComputeRowHash(
            userId ?? UserA, action, objectType, objectId, before, after, result, source, timestamp ?? T0, prevHash);

    private static AuditLog MakeRow(long id, string? prevHash, string action = "act", string? after = "{}")
    {
        var row = new AuditLog
        {
            Id = id,
            UserId = UserA,
            Action = action,
            ObjectType = "Thing",
            ObjectId = "o" + id,
            BeforeValue = null,
            AfterValue = after,
            Result = "Success",
            Source = "api",
            Timestamp = T0.AddSeconds(id),
            PrevHash = prevHash,
        };
        row.RowHash = AuditHashChain.ComputeRowHash(row);
        return row;
    }

    private static List<AuditLog> MakeChain(int count)
    {
        var rows = new List<AuditLog>();
        string? prev = null;
        for (var i = 1; i <= count; i++)
        {
            var row = MakeRow(i, prev);
            rows.Add(row);
            prev = row.RowHash;
        }

        return rows;
    }

    [Fact]
    public void ComputeRowHash_IsDeterministic()
    {
        Assert.Equal(Hash(), Hash());
    }

    [Fact]
    public void ComputeRowHash_Is64LowercaseHex()
    {
        var hash = Hash();
        Assert.Equal(AuditHashChain.HashLength, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    public static TheoryData<string> FieldNames => new()
    {
        "userId", "action", "objectType", "objectId", "before", "after", "result", "source", "timestamp", "prevHash",
    };

    [Theory]
    [MemberData(nameof(FieldNames))]
    public void ComputeRowHash_ChangesWhenAnyFieldChanges(string field)
    {
        var baseline = Hash(before: "b", prevHash: "p");

        var changed = field switch
        {
            "userId" => Hash(userId: Guid.Parse("99999999-2222-3333-4444-555555555555"), before: "b", prevHash: "p"),
            "action" => Hash(action: "logout", before: "b", prevHash: "p"),
            "objectType" => Hash(objectType: "Other", before: "b", prevHash: "p"),
            "objectId" => Hash(objectId: "s2", before: "b", prevHash: "p"),
            "before" => Hash(before: "b2", prevHash: "p"),
            "after" => Hash(after: "{\"a\":1}", before: "b", prevHash: "p"),
            "result" => Hash(result: "Failure", before: "b", prevHash: "p"),
            "source" => Hash(source: "job", before: "b", prevHash: "p"),
            "timestamp" => Hash(timestamp: T0.AddTicks(1), before: "b", prevHash: "p"),
            "prevHash" => Hash(before: "b", prevHash: "p2"),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void ComputeRowHash_NullUserDiffersFromUser()
    {
        var withUser = AuditHashChain.ComputeRowHash(UserA, "a", "t", "i", null, null, "ok", "s", T0, null);
        var anonymous = AuditHashChain.ComputeRowHash(null, "a", "t", "i", null, null, "ok", "s", T0, null);
        Assert.NotEqual(withUser, anonymous);
    }

    [Fact]
    public void Canonical_NullAndEmptyAreDistinct()
    {
        Assert.NotEqual(Hash(before: null), Hash(before: string.Empty));
        Assert.NotEqual(Hash(prevHash: null), Hash(prevHash: string.Empty));
    }

    [Fact]
    public void Canonical_FieldBoundaryIsNotAmbiguous()
    {
        // ย้ายอักขระข้ามขอบฟิลด์ต้องได้ hash ต่างกัน
        Assert.NotEqual(
            Hash(action: "a", objectType: "bc"),
            Hash(action: "ab", objectType: "c"));
        Assert.NotEqual(
            Hash(before: "x", after: "yz"),
            Hash(before: "xy", after: "z"));
    }

    [Fact]
    public void Canonical_SeparatorInsideValueCannotForgeAnotherRecord()
    {
        var sep = AuditHashChain.FieldSeparator;

        // พยายามปลอมโดยฝังตัวคั่นและ length prefix ในค่าของฟิลด์
        var forged = Hash(action: $"a{sep}9:Session", objectType: "x");
        var normal = Hash(action: "a", objectType: "9:Sessionx");
        Assert.NotEqual(forged, normal);

        Assert.NotEqual(
            Hash(action: $"a{sep}-"),
            Hash(action: "a"));
    }

    [Fact]
    public void Timestamp_UtcAndUnspecifiedOfSameInstantProduceSameHash()
    {
        // ค่าที่อ่านกลับจาก datetime2 มี Kind = Unspecified
        var unspecified = DateTime.SpecifyKind(T0, DateTimeKind.Unspecified);
        Assert.Equal(Hash(timestamp: T0), Hash(timestamp: unspecified));
    }

    [Fact]
    public void Timestamp_LocalOfSameInstantProducesSameHash()
    {
        var local = T0.ToLocalTime();
        Assert.Equal(Hash(timestamp: T0), Hash(timestamp: local));
    }

    [Fact]
    public void IsRowHashValid_TrueForUntouchedRow_FalseWhenTampered()
    {
        var row = MakeRow(1, null);
        Assert.True(AuditHashChain.IsRowHashValid(row));

        row.Result = "Failure";
        Assert.False(AuditHashChain.IsRowHashValid(row));
    }

    [Fact]
    public void IsRowHashValid_FalseWhenRowHashEmpty()
    {
        var row = MakeRow(1, null);
        row.RowHash = string.Empty;
        Assert.False(AuditHashChain.IsRowHashValid(row));
    }

    [Fact]
    public void VerifySequence_EmptyIsValid()
    {
        var result = AuditHashChain.VerifySequence([]);
        Assert.True(result.IsValid);
        Assert.Equal(0, result.CheckedCount);
        Assert.Null(result.FirstBrokenId);
    }

    [Fact]
    public void VerifySequence_ValidChain()
    {
        var result = AuditHashChain.VerifySequence(MakeChain(5));
        Assert.True(result.IsValid);
        Assert.Equal(5, result.CheckedCount);
        Assert.Null(result.FirstBrokenId);
    }

    [Fact]
    public void VerifySequence_ValidAfterRoundTripWithUnspecifiedKind()
    {
        var rows = MakeChain(3);
        foreach (var row in rows)
        {
            row.Timestamp = DateTime.SpecifyKind(row.Timestamp, DateTimeKind.Unspecified);
        }

        Assert.True(AuditHashChain.VerifySequence(rows).IsValid);
    }

    [Fact]
    public void VerifySequence_DetectsModifiedField_ReportsThatRow()
    {
        var rows = MakeChain(5);
        rows[2].AfterValue = "{\"tampered\":true}";

        var result = AuditHashChain.VerifySequence(rows);
        Assert.False(result.IsValid);
        Assert.Equal(rows[2].Id, result.FirstBrokenId);
    }

    [Fact]
    public void VerifySequence_DetectsRecomputedRowHashBecauseNextPrevHashMismatches()
    {
        // ผู้โจมตีแก้ข้อมูลและคำนวณ RowHash ของแถวนั้นใหม่ -> แถวถัดไปต้องจับได้
        var rows = MakeChain(4);
        rows[1].Action = "forged";
        rows[1].RowHash = AuditHashChain.ComputeRowHash(rows[1]);

        var result = AuditHashChain.VerifySequence(rows);
        Assert.False(result.IsValid);
        Assert.Equal(rows[2].Id, result.FirstBrokenId);
    }

    [Fact]
    public void VerifySequence_DetectsRemovedMiddleRow()
    {
        var rows = MakeChain(5);
        rows.RemoveAt(2);

        var result = AuditHashChain.VerifySequence(rows);
        Assert.False(result.IsValid);
        Assert.Equal(4, result.FirstBrokenId);
    }

    [Fact]
    public void VerifySequence_DetectsRemovedFirstRow()
    {
        var rows = MakeChain(3);
        rows.RemoveAt(0);

        var result = AuditHashChain.VerifySequence(rows);
        Assert.False(result.IsValid);
        Assert.Equal(2, result.FirstBrokenId);
    }

    [Fact]
    public void VerifySequence_FirstRowWithPrevHashIsBroken()
    {
        var rows = new List<AuditLog> { MakeRow(1, "deadbeef") };

        var result = AuditHashChain.VerifySequence(rows);
        Assert.False(result.IsValid);
        Assert.Equal(1, result.FirstBrokenId);
    }

    [Fact]
    public void VerifySequence_DetectsReorderedRows()
    {
        var rows = MakeChain(3);
        (rows[1], rows[2]) = (rows[2], rows[1]);

        var result = AuditHashChain.VerifySequence(rows);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Verifier_SupportsBatchedFeeding()
    {
        var rows = MakeChain(6);
        var verifier = new AuditHashChain.Verifier();

        foreach (var batch in rows.Chunk(2))
        {
            foreach (var row in batch)
            {
                Assert.True(verifier.Verify(row));
            }
        }

        Assert.Equal(6, verifier.CheckedCount);
        Assert.Equal(rows[^1].RowHash, verifier.LastHash);
    }
}
