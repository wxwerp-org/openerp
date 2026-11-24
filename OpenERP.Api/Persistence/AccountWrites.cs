using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OpenERP.Api.Authentication;

namespace OpenERP.Api.Persistence;

internal abstract class AccountWrites : IAccountWrites
{
    public abstract AppDbContext CreateDbContext();
    public abstract IQueryable<UserAccount> UsersForUpdate(AppDbContext write);
    protected abstract bool IsUsernameConflict(DbUpdateException exception);

    public async Task EnsureExistsAsync(UserAccount account, CancellationToken cancellationToken)
    {
        await using var db = CreateDbContext();
        var existing = db.Users.Where(user => user.Username == account.Username);
        if (await existing.AnyAsync(cancellationToken))
            return;

        db.Users.Add(account);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException error) when (IsUsernameConflict(error))
        {
            // 两个实例可能同时完成首次检查，只有目标账号确已由另一实例创建才算成功。
            if (!await existing.AnyAsync(cancellationToken))
                throw;
        }
    }
}

internal sealed class PostgresAccountWrites(DbContextOptions<AppDbContext> options) : AccountWrites
{
    // 连接在提交时中断可能已有写入结果，避免自动重放整笔认证事务。
    public override AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>(options)
        .UseNpgsql(pg => pg.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies))).Options);

    public override IQueryable<UserAccount> UsersForUpdate(AppDbContext write) =>
        write.Users.FromSqlRaw("""SELECT * FROM "Users" FOR UPDATE""");

    protected override bool IsUsernameConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Users_Username" };
}

internal sealed class SqliteAccountWrites(DbContextOptions<SqliteAppDbContext> options) : AccountWrites
{
    public override AppDbContext CreateDbContext() => new SqliteAppDbContext(options);

    // SQLite 的默认写事务已取得写锁；EF 继续应用账号筛选和软删除条件。
    public override IQueryable<UserAccount> UsersForUpdate(AppDbContext write) => write.Users;

    protected override bool IsUsernameConflict(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 }; // SQLITE_CONSTRAINT_UNIQUE
}
