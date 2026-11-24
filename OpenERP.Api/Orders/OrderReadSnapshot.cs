using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace OpenERP.Api.Orders;

internal static class OrderReadSnapshot
{
    internal static Task<T> ReadAsync<T>(AppDbContext db, Func<Task<T>> read, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (db.Database.GetDbConnection() is not SqliteConnection sqlite)
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
                var result = await read();
                await transaction.CommitAsync(ct);
                return result;
            }

            // SQLite 的默认显式事务会预占唯一写者位置。延迟事务在首次读取时固定 WAL 快照。
            await db.Database.OpenConnectionAsync(ct);
            try
            {
                await using var snapshot = sqlite.BeginTransaction(IsolationLevel.Serializable, deferred: true);
                await using var transaction = await db.Database.UseTransactionAsync(snapshot, ct);
                var result = await read();
                await snapshot.CommitAsync(ct);
                return result;
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        });
}
