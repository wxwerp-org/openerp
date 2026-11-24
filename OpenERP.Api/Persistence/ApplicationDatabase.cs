using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace OpenERP.Api.Persistence;

internal interface IApplicationDatabase
{
    Task StartAsync(CancellationToken cancellationToken);
    Task OpenExistingAsync(CancellationToken cancellationToken);
}

internal sealed class PostgresDatabase(AppDbContext db, IHostEnvironment environment) : IApplicationDatabase
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        environment.IsDevelopment() ? MigrateAsync(db, cancellationToken) : Task.CompletedTask;

    // 正式环境由发布的 EF 迁移包升级，维护命令只访问既有数据库。
    public Task OpenExistingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static async Task MigrateAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        // Npgsql 在迁移前读取历史表；为空库准备它（npgsql/efcore.pg#3407）。
        if (await db.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken))
            await db.GetService<IHistoryRepository>().CreateIfNotExistsAsync(cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);
    }
}
