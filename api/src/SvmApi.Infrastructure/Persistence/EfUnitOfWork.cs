using Microsoft.EntityFrameworkCore;
using SvmApi.Application.Common;

namespace SvmApi.Infrastructure.Persistence;

/// <summary>
/// <see cref="IUnitOfWork"/> บน <see cref="SvmDbContext"/>: execution strategy (retry) + transaction เดียว.
/// ถ้ามี transaction เปิดอยู่แล้ว (ผู้เรียกซ้อน) จะ join แทนที่จะเปิดใหม่ — กัน
/// "execution strategy does not support user-initiated transactions".
/// </summary>
public sealed class EfUnitOfWork(SvmDbContext db) : IUnitOfWork
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await work(cancellationToken);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await work(ct);
            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }

    public async Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
        => await ExecuteAsync<bool>(async ct =>
        {
            await work(ct);
            return true;
        }, cancellationToken);
}
