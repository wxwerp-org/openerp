using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Orders;

namespace OpenERP.Api.Persistence;

internal sealed class SqliteOrderReadSource : IOrderReadSource
{
    private readonly SqliteAppDbContext _db;
    private readonly SqliteConnection _connection;

    public SqliteOrderReadSource(SqliteAppDbContext db)
    {
        _db = db;
        _connection = (SqliteConnection)db.Database.GetDbConnection();
        SqliteOrderTime.Register(_connection);
    }

    public IQueryable<OrderHeader> ReadHeaders() =>
        _db.Database.SqlQueryRaw<OrderHeader>("""
            SELECT "ErpId", "Sid", "Platform",
                   openerp_wall_time("CreatedRaw") AS "CreatedAt",
                   substr(openerp_wall_time("CreatedRaw"), 1, 10) AS "CreatedDate",
                   openerp_wall_time("DispatchedRaw") AS "DispatchedAt",
                   trim("CreatedRaw") AS "CreatedRaw", trim("DispatchedRaw") AS "DispatchedRaw"
            FROM "Orders" WHERE "DeletedAt" IS NULL
            """);

    public Task<T> ReadSnapshotAsync<T>(Func<Task<T>> read, CancellationToken ct) =>
        _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // SQLite 的默认显式事务会预占唯一写者位置。延迟事务在首次读取时固定 WAL 快照。
            await _db.Database.OpenConnectionAsync(ct);
            try
            {
                await using var snapshot = _connection.BeginTransaction(IsolationLevel.Serializable, deferred: true);
                await using var transaction = await _db.Database.UseTransactionAsync(snapshot, ct);
                var result = await read();
                await snapshot.CommitAsync(ct);
                return result;
            }
            finally
            {
                await _db.Database.CloseConnectionAsync();
            }
        });
}
