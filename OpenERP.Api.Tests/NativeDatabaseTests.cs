using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenERP.Api;
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
        var abandonedStage = Path.Combine(directory.Path, $".{NativeDatabase.DatabaseFileName}.dead.initializing");
        await File.WriteAllTextAsync(abandonedStage, "partial initialization", cancellationToken);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;

        // When 首次初始化，写入本地记录，然后使用新 DbContext 再次打开。
        await using (var db = new AppDbContext(options))
        {
            await NativeDatabase.InitializeAsync(db, cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE NativeProbe (Value TEXT NOT NULL);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("INSERT INTO NativeProbe (Value) VALUES ('keep-me');", cancellationToken);
        }

        await using (var reopened = new AppDbContext(options))
        {
            await NativeDatabase.InitializeAsync(reopened, cancellationToken);
            var value = await reopened.Database.SqlQueryRaw<string>("SELECT Value AS Value FROM NativeProbe")
                .SingleAsync(cancellationToken);

            // Then 再次启动识别同一版本，且原有用户数据仍可读。
            Assert.Equal("keep-me", value);
        }

        await using var markerCheck = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await markerCheck.OpenAsync(cancellationToken);
        Assert.Equal(NativeDatabase.ApplicationId, await ReadPragmaAsync(markerCheck, "application_id", cancellationToken));
        Assert.Equal(NativeDatabase.SchemaVersion, await ReadPragmaAsync(markerCheck, "user_version", cancellationToken));
        Assert.False(File.Exists(abandonedStage));
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

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        await using var db = new AppDbContext(options);

        // When 尝试初始化当前应用。
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NativeDatabase.InitializeAsync(db, cancellationToken));

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
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        await using var db = new AppDbContext(options);

        // When reset-admin-password 以仅打开既有库的模式调用初始化。
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => NativeDatabase.InitializeAsync(db, cancellationToken, createIfMissing: false));

        // Then 不创建目录或空数据库，避免密码重置误生成新实例。
        Assert.False(File.Exists(databasePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(databasePath)));
    }

    [Fact]
    public async Task Current_data_directory_has_an_exclusive_process_lease_that_waits_for_quick_restart()
    {
        // Given 第一个 OpenERP 进程持有数据目录租约。
        var cancellationToken = TestContext.Current.CancellationToken;
        using var directory = new TemporaryDirectory();
        using var lease = NativeDatabase.Acquire(directory.Path);

        // When 旧实例正在正常退出，新实例等待租约短暂释放。
        var waitingInstance = Task.Run(() => NativeDatabase.Acquire(directory.Path), cancellationToken);
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
        using var lease = NativeDatabase.Acquire(directory.Path);

        // When 另一实例等待超过租约等待上限。
        var error = await Assert.ThrowsAsync<IOException>(
            () => Task.Run(
                () => NativeDatabase.Acquire(directory.Path, TimeSpan.FromMilliseconds(250)),
                cancellationToken));

        // Then 超时后清楚拒绝启动，释放旧租约后可再次使用。
        Assert.Contains("仍被另一实例使用", error.Message);
        lease.Dispose();
        using var nextLease = NativeDatabase.Acquire(directory.Path);
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
        _ = NativeDatabase.ConnectionString(newPath);
        _ = NativeDatabase.ConnectionString(existingPath);

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

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        await using var db = new AppDbContext(options);

        // When 旧程序启动该数据库。
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NativeDatabase.InitializeAsync(db, cancellationToken));

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
