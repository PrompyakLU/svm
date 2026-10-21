using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SvmApi.Application.Audit;
using SvmApi.Domain.Audit;
using SvmApi.Infrastructure.Audit;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Tests.Audit;

/// <summary>
/// ทดสอบชั้นป้องกัน append-only (BR-33, NFR-20/21, EX-98) โดยไม่ต้องต่อ DB จริง:
/// interceptor ปฏิเสธก่อนเปิด connection และ AppendAsync ปฏิเสธเมื่อไม่มี transaction.
/// </summary>
public class AuditAppendOnlyTests
{
    private static SvmDbContext CreateDb()
    {
        // connection string ไม่ถูกใช้: ทุกเคสในไฟล์นี้ล้มเหลวหรือจบก่อนเปิด connection
        var options = new DbContextOptionsBuilder<SvmDbContext>()
            .UseSqlServer("Server=(local);Database=svm_unit_test;Trusted_Connection=True;TrustServerCertificate=True")
            .AddInterceptors(new AuditLogImmutabilityInterceptor())
            .Options;
        return new SvmDbContext(options);
    }

    private static AuditLog ExistingRow() => new()
    {
        Id = 1,
        UserId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Action = "act",
        ObjectType = "Thing",
        ObjectId = "o1",
        AfterValue = "{}",
        Result = "Success",
        Source = "api",
        Timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        RowHash = new string('a', AuditHashChain.HashLength),
    };

    private static AuditEntry Entry() =>
        new(null, "act", "Thing", "o1", null, "{}", "Success", "tests");

    [Fact]
    public async Task Interceptor_RejectsModifiedAuditRow_Async()
    {
        await using var db = CreateDb();
        var row = ExistingRow();
        db.AuditLogs.Attach(row);
        db.Entry(row).State = EntityState.Modified;

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Interceptor_RejectsDeletedAuditRow_Async()
    {
        await using var db = CreateDb();
        var row = ExistingRow();
        db.AuditLogs.Attach(row);
        db.AuditLogs.Remove(row);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void Interceptor_RejectsModifiedAuditRow_Sync()
    {
        using var db = CreateDb();
        var row = ExistingRow();
        db.AuditLogs.Attach(row);
        db.Entry(row).State = EntityState.Modified;

        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task AppendAsync_WithoutTransaction_IsRejected()
    {
        await using var db = CreateDb();
        var service = new AuditService(db, new EfUnitOfWork(db), TimeProvider.System, NullLogger<AuditService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AppendAsync(Entry(), CancellationToken.None));

        Assert.Contains("transaction", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.ChangeTracker.Entries<AuditLog>());
    }

    [Fact]
    public async Task AppendAsync_NullEntry_Throws()
    {
        await using var db = CreateDb();
        var service = new AuditService(db, new EfUnitOfWork(db), TimeProvider.System, NullLogger<AuditService>.Instance);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.AppendAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void IAuditService_ExposesNoUpdateOrDeleteOperation()
    {
        var names = typeof(IAuditService).GetMethods().Select(m => m.Name).ToList();

        Assert.DoesNotContain(names, n =>
            n.Contains("Update", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Delete", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImmutabilityTriggerSql_BlocksUpdateAndDelete()
    {
        Assert.Contains("INSTEAD OF UPDATE, DELETE", AuditSql.CreateImmutabilityTrigger);
        Assert.Contains("THROW", AuditSql.CreateImmutabilityTrigger);
        Assert.Contains(AuditSql.ImmutabilityTriggerName, AuditSql.CreateImmutabilityTrigger);
        Assert.Contains(AuditSql.ImmutabilityTriggerName, AuditSql.DropImmutabilityTrigger);
    }
}
