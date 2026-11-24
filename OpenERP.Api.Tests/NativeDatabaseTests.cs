using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using OpenERP.Api;
using OpenERP.Api.Migrations.Sqlite;
using OpenERP.Api.Persistence;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class NativeDatabaseTests
{
    [Fact]
    public async Task First_open_creates_versioned_database_and_reopen_preserves_data()
    {
        // Given 一个新的本机数据目录。
        var cancellationToken = TestContext.Current.CancellationToken;
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, NativeDatabase.DatabaseFileName);
        var abandonedStage = Path.Combine(directory.Path, $".{NativeDatabase.DatabaseFileName}.{Guid.NewGuid():N}.initializing");
        var unrelatedFile = Path.Combine(directory.Path, ".local.sqlite.user-backup.initializing");
        await File.WriteAllTextAsync(unrelatedFile, "user content", cancellationToken);
        await File.WriteAllTextAsync(abandonedStage, "partial initialization", cancellationToken);
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;

        // When 首次初始化，写入本地记录，然后使用新 DbContext 再次打开。
        await using (var db = new SqliteAppDbContext(options))
        {
            await new NativeDatabase(db).StartAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE NativeProbe (Value TEXT NOT NULL);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("INSERT INTO NativeProbe (Value) VALUES ('keep-me');", cancellationToken);
        }

        await using (var reopened = new SqliteAppDbContext(options))
        {
            await new NativeDatabase(reopened).StartAsync(cancellationToken);
            var value = await reopened.Database.SqlQueryRaw<string>("SELECT Value AS Value FROM NativeProbe")
                .SingleAsync(cancellationToken);

            // Then 再次启动识别同一版本，且原有用户数据仍可读。
            Assert.Equal("keep-me", value);
            Assert.NotEmpty(reopened.Database.GetMigrations());
            Assert.Equal(reopened.Database.GetMigrations(), await reopened.Database.GetAppliedMigrationsAsync(cancellationToken));
        }

        await using var markerCheck = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await markerCheck.OpenAsync(cancellationToken);
        Assert.Equal(NativeDatabase.ApplicationId, await ReadPragmaAsync(markerCheck, "application_id", cancellationToken));
        Assert.Equal(NativeDatabase.SchemaVersion, await ReadPragmaAsync(markerCheck, "user_version", cancellationToken));
        Assert.False(File.Exists(abandonedStage));
        Assert.Equal("user content", await File.ReadAllTextAsync(unrelatedFile, cancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_database_is_adopted_only_if_the_original_schema_matches(bool missingIndex)
    {
        // Given 旧版使用首个 SQLite 模型建表，以标记识别文件，没有 EF 历史表。
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TemporaryDirectory();
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(NativeDatabase.ConnectionString(directory.Path)).Options;
        await using var db = new SqliteAppDbContext(options);
        await db.GetService<IMigrator>().MigrateAsync(nameof(InitialSqlite), ct);
        await db.Database.ExecuteSqlRawAsync("""
            DROP TABLE "__EFMigrationsHistory";
            DROP TABLE "__EFMigrationsLock";
            PRAGMA user_version = 1;
            INSERT INTO "DataProtectionKeys" ("FriendlyName", "Xml") VALUES ('existing', 'keep');
            """, ct);
        if (missingIndex)
            await db.Database.ExecuteSqlRawAsync("""DROP INDEX "IX_OrderLines_ErpId_Ordinal" """, ct);

        // When 现有安装升级，只有完整的旧版结构才能开始使用 EF 迁移历史。
        var database = new NativeDatabase(db);
        if (missingIndex)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.StartAsync(ct));
            Assert.Empty(await db.Database.GetAppliedMigrationsAsync(ct));
        }
        else
        {
            await database.StartAsync(ct);
            await database.StartAsync(ct);
            Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync(ct));
        }

        // Then 无论能否升级，原有数据均保留。
        Assert.Equal("keep", (await db.DataProtectionKeys.SingleAsync(ct)).Xml);
    }

    [Fact]
    public async Task Current_schema_reopens_without_acquiring_a_migration_lock()
    {
        // Given 数据已经升级完毕，一个中断的外部迁移工具留下 SQLite 迁移锁。
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TemporaryDirectory();
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(NativeDatabase.ConnectionString(directory.Path)).Options;
        await using var db = new SqliteAppDbContext(options);
        var database = new NativeDatabase(db);
        await database.StartAsync(ct);
        await db.Database.ExecuteSqlRawAsync("""INSERT INTO "__EFMigrationsLock" ("Id", "Timestamp") VALUES (1, CURRENT_TIMESTAMP)""", ct);

        // When 再次启动，Then 直接打开当前数据，保留外部工具的锁。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await database.StartAsync(timeout.Token);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*) AS Value FROM "__EFMigrationsLock" """).SingleAsync(ct));
    }

    [Fact]
    public async Task Existing_unmarked_database_is_rejected_without_modification()
    {
        // Given 含有用户数据但没有 OpenERP 版本标记的 SQLite 文件。
        var cancellationToken = TestContext.Current.CancellationToken;
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, NativeDatabase.DatabaseFileName);
        await using (var sqlite = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await sqlite.OpenAsync(cancellationToken);
            await using var command = sqlite.CreateCommand();
            command.CommandText = "CREATE TABLE LegacyData (Value TEXT NOT NULL); INSERT INTO LegacyData VALUES ('keep-me');";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var options = new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        await using var db = new SqliteAppDbContext(options);

        // When 尝试初始化当前应用。
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new NativeDatabase(db).StartAsync(cancellationToken));

        // Then 明确拒绝未知 schema，原表和原数据保持不变。
        Assert.Contains("版本标记", error.Message);
        await using var verify = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await verify.OpenAsync(cancellationToken);
        await using var check = verify.CreateCommand();
        check.CommandText = "SELECT Value FROM LegacyData";
        Assert.Equal("keep-me", await check.ExecuteScalarAsync(cancellationToken));
        Assert.Equal(0, await ReadPragmaAsync(verify, "user_version", cancellationToken));
    }

    [Fact]
    public async Task Reset_style_open_does_not_create_a_missing_database()
    {
        // Given 一个输入错误的、不存在的本机数据目录。
        var cancellationToken = TestContext.Current.CancellationToken;
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "missing", NativeDatabase.DatabaseFileName);
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        await using var db = new SqliteAppDbContext(options);

        // When reset-admin-password 以仅打开既有库的模式调用初始化。
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => new NativeDatabase(db).OpenExistingAsync(cancellationToken));

        // Then 不创建目录或空数据库，避免密码重置误生成新实例。
        Assert.False(File.Exists(databasePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(databasePath)));
    }

    [Fact]
    public async Task Host_keeps_the_directory_locked_after_its_startup_scope_ends()
    {
        using var directory = new TemporaryDirectory();
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Native:DataDirectory"] = directory.Path;
        builder.AddApplicationStorage();

        await using (var app = builder.Build())
        {
            // Given 本机数据库已在启动作用域中打开。
            await using (var scope = app.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<IApplicationDatabase>()
                    .StartAsync(TestContext.Current.CancellationToken);

            // When 作用域已经释放，维护命令仍须等待运行中的宿主退出。
            Assert.Throws<IOException>(() => NativeDataDirectory.AcquireExistingLease(directory.Path));
        }

        // Then 宿主退出后，维护命令可以取得同一数据目录。
        using var lease = NativeDataDirectory.AcquireExistingLease(directory.Path);
    }

    [Fact]
    public async Task Current_data_directory_has_an_exclusive_process_lease_that_waits_for_quick_restart()
    {
        // Given 第一个 OpenERP 进程持有数据目录租约。
        var cancellationToken = TestContext.Current.CancellationToken;
        using var directory = new TemporaryDirectory();
        using var lease = NativeDataDirectory.AcquireLease(directory.Path);

        // When 旧实例正在正常退出，新实例等待租约短暂释放。
        var waitingInstance = Task.Run(() => NativeDataDirectory.AcquireLease(directory.Path), cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        lease.Dispose();

        // Then 快速重启可在有界等待内取得租约。
        using var nextLease = await waitingInstance.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
    }

    [Fact]
    public async Task Long_held_data_directory_lease_is_rejected_after_bounded_wait()
    {
        // Given 某实例持续持有数据目录租约。
        var cancellationToken = TestContext.Current.CancellationToken;
        using var directory = new TemporaryDirectory();
        using var lease = NativeDataDirectory.AcquireLease(directory.Path);

        // When 另一实例等待超过租约等待上限。
        var error = await Assert.ThrowsAsync<IOException>(
            () => Task.Run(
                () => NativeDataDirectory.AcquireLease(directory.Path, TimeSpan.FromMilliseconds(250)),
                cancellationToken));

        // Then 超时后清楚拒绝启动，释放旧租约后可再次使用。
        Assert.Contains("仍被另一实例使用", error.Message);
        lease.Dispose();
        using var nextLease = NativeDataDirectory.AcquireLease(directory.Path);
    }

    [Fact]
    public void New_data_directory_is_private_and_existing_directory_mode_is_preserved()
    {
        // Given 一个新目录和一个已经由用户创建的目录。
        using var directory = new TemporaryDirectory();
        var newPath = Path.Combine(directory.Path, "new-data");
        var existingPath = Path.Combine(directory.Path, "existing-data");
        Directory.CreateDirectory(existingPath);
        if (OperatingSystem.IsWindows())
            return;

        var existingMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
        File.SetUnixFileMode(existingPath, existingMode);

        // When 本机数据目录由 OpenERP 准备。
        using var first = NativeDataDirectory.AcquireLease(newPath);
        using var second = NativeDataDirectory.AcquireLease(existingPath);

        // Then 新目录只有当前用户可访问，已有目录权限不变。
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(newPath));
        Assert.Equal(existingMode, File.GetUnixFileMode(existingPath));
    }

    [Fact]
    public async Task Database_from_an_unknown_schema_version_is_rejected()
    {
        // Given 一个由 OpenERP 标识、但 schema 版本高于当前程序的数据库。
        var cancellationToken = TestContext.Current.CancellationToken;
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, NativeDatabase.DatabaseFileName);
        await using (var sqlite = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await sqlite.OpenAsync(cancellationToken);
            await using var command = sqlite.CreateCommand();
            command.CommandText = $"PRAGMA application_id={NativeDatabase.ApplicationId}; PRAGMA user_version={NativeDatabase.SchemaVersion + 1}; CREATE TABLE FutureData (Value TEXT NOT NULL);";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var options = new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        await using var db = new SqliteAppDbContext(options);

        // When 旧程序启动该数据库。
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new NativeDatabase(db).StartAsync(cancellationToken));

        // Then 拒绝降级打开，schema marker 仍保留原值。
        Assert.Contains("版本", error.Message);
        await using var verify = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await verify.OpenAsync(cancellationToken);
        Assert.Equal(NativeDatabase.SchemaVersion + 1,
            await ReadPragmaAsync(verify, "user_version", cancellationToken));
    }

    private static async Task<int> ReadPragmaAsync(
        SqliteConnection connection,
        string pragma,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "openerp-native-tests", Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
