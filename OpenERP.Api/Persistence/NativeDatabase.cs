using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenERP.Api.Migrations.Sqlite;

namespace OpenERP.Api.Persistence;

/// <summary>通过 SQLite 专属的 EF 迁移集创建和升级本机数据库。</summary>
internal sealed class NativeDatabase(SqliteAppDbContext db) : IApplicationDatabase
{
    internal const string DatabaseFileName = "local.sqlite";
    internal const int ApplicationId = 0x4F455250; // ASCII "OERP"。
    internal const int SchemaVersion = 2;
    private const int LegacyVersion = 1;
    private enum SchemaState { Legacy, Current, PendingMigrations }

    internal static string ConnectionString(string dataDirectory, bool createIfMissing = true) =>
        ConnectionStringForFile(Path.Combine(dataDirectory, DatabaseFileName),
            createIfMissing ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite);

    private static string ConnectionStringForFile(string path, SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = mode,
            ForeignKeys = true,
            DefaultTimeout = 5,
            Pooling = false
        }.ToString();

    public Task StartAsync(CancellationToken ct) => InitializeAsync(ct, createIfMissing: true);
    public Task OpenExistingAsync(CancellationToken ct) => InitializeAsync(ct, createIfMissing: false);

    private async Task InitializeAsync(CancellationToken ct, bool createIfMissing)
    {
        var file = new FileInfo(db.Database.GetDbConnection().DataSource);
        if (createIfMissing)
            CleanupAbandonedInitializations(file);

        if (file.Exists && file.Length > 0)
        {
            var state = await ValidateExistingAsync(file.FullName, ct);
            if (state == SchemaState.Current)
                return;
            if (!createIfMissing)
            {
                if (state == SchemaState.PendingMigrations)
                    throw new InvalidOperationException("请先正常启动 OpenERP 完成数据库升级，再运行维护命令。");
                return;
            }
            if (state == SchemaState.Legacy)
                await AdoptLegacyAsync(ct);
            await db.Database.MigrateAsync(ct);
            return;
        }

        if (!createIfMissing)
            throw new FileNotFoundException("没有可用的本机数据库，请确认数据目录。", file.FullName);
        await CreateAsync(file, ct);
    }

    private static SqliteAppDbContext OpenFile(string path, SqliteOpenMode mode) =>
        new(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(ConnectionStringForFile(path, mode)).Options);

    private static async Task CreateAsync(FileInfo destination, CancellationToken ct)
    {
        // 在暂存库完成迁移，防止中断留下没有应用标记的正式数据库。
        var stagingPath = Path.Combine(destination.DirectoryName!, $".{destination.Name}.{Guid.NewGuid():N}.initializing");
        try
        {
            await using (var staging = OpenFile(stagingPath, SqliteOpenMode.ReadWriteCreate))
                await staging.Database.MigrateAsync(ct);
            File.Move(stagingPath, destination.FullName, overwrite: destination.Exists);
        }
        finally
        {
            DeleteStagingFiles(stagingPath);
        }
    }

    private static void CleanupAbandonedInitializations(FileInfo file)
    {
        var prefix = $".{file.Name}.";
        const string suffix = ".initializing";
        foreach (var path in Directory.EnumerateFiles(file.DirectoryName!, $"{prefix}*{suffix}"))
        {
            var name = Path.GetFileName(path);
            if (name.Length == prefix.Length + 32 + suffix.Length &&
                Guid.TryParseExact(name.AsSpan(prefix.Length, 32), "N", out _))
                DeleteStagingFiles(path);
        }
    }

    private static void DeleteStagingFiles(string path)
    {
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
        File.Delete(path + "-journal");
        File.Delete(path);
    }

    private static async Task<SchemaState> ValidateExistingAsync(string path, CancellationToken ct)
    {
        await using var existing = OpenFile(path, SqliteOpenMode.ReadOnly);
        try
        {
            var database = existing.Database;
            var application = await database.SqlQueryRaw<int>("SELECT application_id AS Value FROM pragma_application_id").SingleAsync(ct);
            if (application != ApplicationId)
                throw new InvalidOperationException("数据库没有 OpenERP 本机版本标记，已拒绝写入。");

            var version = await database.SqlQueryRaw<int>("SELECT user_version AS Value FROM pragma_user_version").SingleAsync(ct);
            var applied = (await database.GetAppliedMigrationsAsync(ct)).ToArray();
            if (version == LegacyVersion && applied.Length == 0)
            {
                await VerifyLegacySchemaAsync(existing, ct);
                return SchemaState.Legacy;
            }

            var migrations = database.GetMigrations().ToArray();
            if (version != SchemaVersion || applied.Length == 0 || !applied.SequenceEqual(migrations.Take(applied.Length)))
                throw new InvalidOperationException($"不支持本机数据库版本 {version} 或其迁移历史，已拒绝写入。");

            if (applied.Length < migrations.Length)
                return SchemaState.PendingMigrations;

            var tables = await database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_schema WHERE type = 'table'").ToListAsync(ct);
            var missing = existing.Model.GetEntityTypes().Select(entity => entity.GetTableName()).OfType<string>()
                .Except(tables, StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"本机数据库缺少必要表：{string.Join(", ", missing)}。已拒绝写入。");
            return SchemaState.Current;
        }
        catch (SqliteException error)
        {
            throw new InvalidOperationException("无法读取本机 SQLite 数据库。请确认文件完整且属于 OpenERP。", error);
        }
    }

    private static async Task VerifyLegacySchemaAsync(SqliteAppDbContext existing, CancellationToken ct)
    {
        // 旧版使用 EnsureCreated。按首个迁移在内存中还原当时的完整表、约束与索引，再决定能否登记基线。
        await using var baseline = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        await baseline.Database.OpenConnectionAsync(ct);
        await baseline.GetService<IMigrator>().MigrateAsync(nameof(InitialSqlite), ct);
        const string schema = """
            SELECT sql AS Value FROM sqlite_schema WHERE sql IS NOT NULL
              AND name NOT IN ('__EFMigrationsHistory', '__EFMigrationsLock') AND name NOT GLOB 'sqlite_*'
            """;
        var expected = await baseline.Database.SqlQueryRaw<string>(schema).ToListAsync(ct);
        var actual = await existing.Database.SqlQueryRaw<string>(schema).ToListAsync(ct);
        if (expected.Except(actual, StringComparer.Ordinal).Any())
            throw new InvalidOperationException("本机旧版数据库结构与 OpenERP 初始迁移不一致，已拒绝写入。");
    }

    private async Task AdoptLegacyAsync(CancellationToken ct)
    {
        var assembly = db.GetService<IMigrationsAssembly>();
        var initial = assembly.GetMigrationId(nameof(InitialSqlite));
        var history = db.GetService<IHistoryRepository>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await history.CreateIfNotExistsAsync(ct);
        var row = new HistoryRow(initial, db.Model.GetProductVersion()!);
        await db.Database.ExecuteSqlRawAsync(history.GetInsertScript(row), ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA user_version = 2", ct);
        await transaction.CommitAsync(ct);
    }
}
