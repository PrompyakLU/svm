using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SvmApi.Application.Audit;
using SvmApi.Application.Common;
using SvmApi.Domain.Audit;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Audit;

/// <summary>
/// <see cref="IAuditService"/> บน <see cref="SvmDbContext"/>: append-only + hash chain
/// (FR-68, BR-33, NFR-20/21, EX-98/100, C13).
/// การต่อ chain serialize ด้วย <c>sp_getapplock</c> (Exclusive, owner = Transaction) ภายใน transaction ของผู้เรียก
/// lock ปล่อยเองเมื่อ commit/rollback จึงไม่มีสอง transaction ต่อ chain พร้อมกัน.
/// </summary>
public sealed class AuditService(
    SvmDbContext db,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<AuditService> logger) : IAuditService
{
    /// <summary>ชื่อ resource ของ application lock ที่ใช้ serialize การต่อ chain.</summary>
    public const string ChainLockResource = "svm.auditlog.chain";

    private const int LockTimeoutMilliseconds = 30_000;
    private const int VerifyBatchSize = 1_000;
    private const int MaxPageSize = 200;

    // ความยาวสูงสุดตาม AuditLogConfiguration
    private const int MaxActionLength = 50;
    private const int MaxObjectTypeLength = 100;
    private const int MaxObjectIdLength = 100;
    private const int MaxResultLength = 20;
    private const int MaxSourceLength = 100;

    /// <inheritdoc />
    public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // EX-98: ไม่มี transaction = ไม่มีทางรับประกัน atomic + serialize chain -> ปฏิเสธ action
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Audit append requires an active transaction (run inside IUnitOfWork.ExecuteAsync). " +
                "The action is rejected because it cannot be audited atomically.");
        }

        Validate(entry);

        await AcquireChainLockAsync(cancellationToken);

        var prevHash = await db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(x => x.Id)
            .Select(x => x.RowHash)
            .FirstOrDefaultAsync(cancellationToken);

        // DateTime ticks (100ns) ตรงกับ datetime2(7) พอดี -> อ่านกลับได้ค่าเดิมเป๊ะ
        var timestamp = clock.GetUtcNow().UtcDateTime;
        var before = AuditRedactor.Redact(entry.BeforeValue);
        var after = AuditRedactor.Redact(entry.AfterValue);

        var row = new AuditLog
        {
            UserId = entry.UserId,
            Action = entry.Action,
            ObjectType = entry.ObjectType,
            ObjectId = entry.ObjectId,
            BeforeValue = before,
            AfterValue = after,
            Result = entry.Result,
            Source = entry.Source,
            Timestamp = timestamp,
            PrevHash = prevHash,
            RowHash = AuditHashChain.ComputeRowHash(
                entry.UserId, entry.Action, entry.ObjectType, entry.ObjectId, before, after,
                entry.Result, entry.Source, timestamp, prevHash),
        };

        db.AuditLogs.Add(row);
        try
        {
            // SaveChanges ภายใน transaction ปัจจุบัน เพื่อให้แถวถัดไปใน transaction เดียวกันเห็น chain ล่าสุด
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            db.Entry(row).State = EntityState.Detached;
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await unitOfWork.ExecuteAsync(async ct => { await AppendAsync(entry, ct); }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PagedResult<AuditRecordDto>> SearchAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        IQueryable<AuditLog> q = db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.ObjectType))
        {
            var objectType = query.ObjectType;
            q = q.Where(x => x.ObjectType == objectType);
        }

        if (!string.IsNullOrWhiteSpace(query.ObjectId))
        {
            var objectId = query.ObjectId;
            q = q.Where(x => x.ObjectId == objectId);
        }

        if (query.UserId is { } userId)
        {
            q = q.Where(x => x.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action;
            q = q.Where(x => x.Action == action);
        }

        if (query.From is { } fromValue)
        {
            var fromUtc = AuditHashChain.NormalizeTimestamp(fromValue);
            q = q.Where(x => x.Timestamp >= fromUtc);
        }

        if (query.To is { } toValue)
        {
            var toUtc = AuditHashChain.NormalizeTimestamp(toValue);
            q = q.Where(x => x.Timestamp <= toUtc);
        }

        var total = await q.LongCountAsync(cancellationToken);

        var skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue)
        {
            return new PagedResult<AuditRecordDto>([], page, pageSize, total);
        }

        var rows = await q
            .OrderByDescending(x => x.Timestamp)
            .ThenByDescending(x => x.Id)
            .Skip((int)skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(ToDto).ToList();
        return new PagedResult<AuditRecordDto>(items, page, pageSize, total);
    }

    /// <inheritdoc />
    public async Task<AuditChainVerification> VerifyChainAsync(CancellationToken cancellationToken)
    {
        var verifier = new AuditHashChain.Verifier();
        long lastId = 0;

        while (true)
        {
            var afterId = lastId;
            var batch = await db.AuditLogs
                .AsNoTracking()
                .Where(x => x.Id > afterId)
                .OrderBy(x => x.Id)
                .Take(VerifyBatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            foreach (var row in batch)
            {
                if (!verifier.Verify(row))
                {
                    logger.LogError(
                        "audit.chain.broken firstBrokenId={FirstBrokenId} checked={CheckedCount}",
                        row.Id, verifier.CheckedCount);
                    return new AuditChainVerification(false, verifier.CheckedCount, row.Id);
                }
            }

            lastId = batch[^1].Id;
        }

        return new AuditChainVerification(true, verifier.CheckedCount, null);
    }

    private async Task AcquireChainLockAsync(CancellationToken cancellationToken)
    {
        // sp_getapplock มีเฉพาะ SQL Server; provider อื่น (เช่น SQLite ในเทสต์) เขียนแบบ serialize อยู่แล้ว
        if (!db.Database.IsSqlServer())
        {
            return;
        }

        var returnCode = new SqlParameter("@returnCode", SqlDbType.Int) { Direction = ParameterDirection.Output };

        await db.Database.ExecuteSqlRawAsync(
            "EXEC @returnCode = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', " +
            "@LockOwner = 'Transaction', @LockTimeout = @timeout;",
            new object[]
            {
                returnCode,
                new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = ChainLockResource },
                new SqlParameter("@timeout", SqlDbType.Int) { Value = LockTimeoutMilliseconds },
            },
            cancellationToken);

        // >= 0 = ได้ lock (0 ทันที, 1 หลังรอ); < 0 = ล้มเหลว (-1 timeout, -2 cancelled, -3 deadlock, -999 error)
        var code = returnCode.Value is int value ? value : -999;
        if (code < 0)
        {
            logger.LogError("audit.chain.lock_failed code={LockResultCode}", code);
            throw new InvalidOperationException(
                "Could not acquire the audit chain lock; the action is rejected because it cannot be audited.");
        }
    }

    private static void Validate(AuditEntry entry)
    {
        Require(entry.Action, nameof(entry.Action), MaxActionLength);
        Require(entry.ObjectType, nameof(entry.ObjectType), MaxObjectTypeLength);
        Require(entry.ObjectId, nameof(entry.ObjectId), MaxObjectIdLength);
        Require(entry.Result, nameof(entry.Result), MaxResultLength);
        Require(entry.Source, nameof(entry.Source), MaxSourceLength);
    }

    private static void Require(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Audit field '{field}' is required.", field);
        }

        if (value.Length > maxLength)
        {
            throw new ArgumentException($"Audit field '{field}' exceeds {maxLength} characters.", field);
        }
    }

    private static AuditRecordDto ToDto(AuditLog x) => new(
        x.Id,
        x.UserId,
        x.Action,
        x.ObjectType,
        x.ObjectId,
        x.BeforeValue,
        x.AfterValue,
        x.Result,
        x.Source,
        DateTime.SpecifyKind(x.Timestamp, DateTimeKind.Utc),
        x.PrevHash,
        x.RowHash);
}
