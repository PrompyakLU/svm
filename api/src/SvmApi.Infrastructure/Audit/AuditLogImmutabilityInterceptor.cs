using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SvmApi.Domain.Audit;

namespace SvmApi.Infrastructure.Audit;

/// <summary>
/// ชั้นป้องกันฝั่งแอป: ปฏิเสธ SaveChanges ที่มี <see cref="AuditLog"/> สถานะ Modified/Deleted
/// (append-only · BR-33, NFR-20/21). ชั้น DB ใช้ trigger จาก <see cref="AuditSql"/> เสริม.
/// </summary>
public sealed class AuditLogImmutabilityInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        EnsureAppendOnly(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        EnsureAppendOnly(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void EnsureAppendOnly(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var violated = context.ChangeTracker
            .Entries<AuditLog>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted);

        if (violated)
        {
            throw new InvalidOperationException(
                "trans_auditLog is append-only: modifying or deleting audit rows is not permitted.");
        }
    }
}
