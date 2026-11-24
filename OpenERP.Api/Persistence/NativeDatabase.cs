using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace OpenERP.Api.Persistence;

/// <summary>管理本机 SQLite 文件格式及数据目录的独占租约。</summary>
internal static class NativeDatabase
{
    internal const string DatabaseFileName = "local.sqlite";
    internal const int ApplicationId = 0x4F455250; // ASCII "OERP".
    internal const int SchemaVersion = 1;
    private const string LeaseFileName = ".openerp-native.lock";
    private static readonly TimeSpan LeaseWaitTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan LeaseRetryDelay = TimeSpan.FromMilliseconds(100);

    internal static string ConnectionString(string dataDirectory, bool createIfMissing = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var root = Path.GetFullPath(dataDirectory);
        if (createIfMissing)
            EnsureDataDirectory(root);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(root, DatabaseFileName),
            Mode = createIfMissing ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            DefaultTimeout = 5,
            Pooling = false
        };
        return builder.ToString();
    }

    /// <summary>阻止多个 OpenERP 进程同时使用同一数据目录。</summary>
    internal static IDisposable Acquire(string dataDirectory, TimeSpan? waitTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var timeout = waitTimeout ?? LeaseWaitTimeout;
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(waitTimeout));
        var root = Path.GetFullPath(dataDirectory);
        EnsureDataDirectory(root);
        var leasePath = Path.Combine(root, LeaseFileName);
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                return new FileLease(new FileStream(
                    leasePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None));
            }
            catch (IOException exception) when (IsLeaseContention(exception))
            {
                if (System.Diagnostics.Stopwatch.GetElapsedTime(startedAt) >= timeout)
                    throw new IOException("OpenERP 数据目录仍被另一实例使用。请先关闭正在运行的实例，再重试。", exception);
                Thread.Sleep(LeaseRetryDelay);
            }
        }
    }

    /// <summary>初始化空库，校验可兼容的存量库，并拒绝打开未知文件。</summary>
    internal static async Task InitializeAsync(
        AppDbContext db,
        CancellationToken cancellationToken,
        bool createIfMissing = true)
    {
        ArgumentNullException.ThrowIfNull(db);
        var connection = db.Database.GetDbConnection() as SqliteConnection
            ?? throw new InvalidOperationException("本机数据库必须使用 SQLite 连接。");
        var dataSource = connection.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:")
            throw new InvalidOperationException("本机数据库必须使用 local.sqlite 文件。");

        var databasePath = Path.GetFullPath(dataSource);
        if (createIfMissing)
        {
            EnsureDataDirectory(Path.GetDirectoryName(databasePath)!);
            CleanupAbandonedInitializations(databasePath);
        }

        if (!File.Exists(databasePath))
        {
            if (!createIfMissing)
                throw new FileNotFoundException("本机数据库不存在；为避免创建空实例，已停止操作。", databasePath);

            await CreateAndMarkAsync(db, databasePath, overwriteTarget: false, cancellationToken);
            return;
        }

        var file = new FileInfo(databasePath);
        if (file.Length == 0)
        {
            if (!createIfMissing)
                throw new InvalidOperationException("本机数据库文件为空；不会在仅打开模式下初始化。请确认数据目录。");

            await CreateAndMarkAsync(db, databasePath, overwriteTarget: true, cancellationToken);
            return;
        }

        await ValidateExistingAsync(databasePath, db.Model, cancellationToken);
    }

    private static async Task CreateAndMarkAsync(
        AppDbContext sourceDb,
        string databasePath,
        bool overwriteTarget,
        CancellationToken cancellationToken)
    {
        // 暂存库隔离未完成的建表过程；表和版本标记提交后，才将文件原子发布到正式路径。
        var stagingPath = Path.Combine(
            Path.GetDirectoryName(databasePath)!, $".{Path.GetFileName(databasePath)}.{Guid.NewGuid():N}.initializing");
        try
        {
            var connectionString = new SqliteConnectionStringBuilder(
                sourceDb.Database.GetDbConnection().ConnectionString)
            {
                DataSource = stagingPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                ForeignKeys = true,
                DefaultTimeout = 5
            };
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString.ToString())
                .Options;
            await using (var stagingDb = new AppDbContext(options))
            {
                // 只对新建的私有暂存文件调用 EnsureCreated。
                if (!await stagingDb.Database.EnsureCreatedAsync(cancellationToken))
                    throw new InvalidOperationException("本机数据库初始化暂存文件并非空库；为保护数据，已停止初始化。");

                await using var transaction = await stagingDb.Database.BeginTransactionAsync(cancellationToken);
                var connection = (SqliteConnection)stagingDb.Database.GetDbConnection();
                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = (SqliteTransaction)transaction.GetDbTransaction();
                    command.CommandText = $"PRAGMA application_id = {ApplicationId}; PRAGMA user_version = {SchemaVersion};";
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
            }

            File.Move(stagingPath, databasePath, overwrite: overwriteTarget);
        }
        finally
        {
            DeleteIfPresent(stagingPath);
            DeleteIfPresent(stagingPath + "-wal");
            DeleteIfPresent(stagingPath + "-shm");
            DeleteIfPresent(stagingPath + "-journal");
        }
    }

    private static void EnsureDataDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        // 新建目录仅授予当前用户权限；CreateDirectory 不改变已存在目录的权限。
        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static bool IsLeaseContention(IOException exception)
    {
        // Windows 使用 sharing/lock violation 表示占用；Unix 的 flock 冲突返回 EAGAIN。
        var nativeError = exception.HResult & 0xFFFF;
        return OperatingSystem.IsWindows()
            ? nativeError is 32 or 33
            : nativeError == 11;
    }

    private static void CleanupAbandonedInitializations(string databasePath)
    {
        // 仅清理本组件专用的暂存命名；它们从未成为活动数据库，也不会含业务数据。
        var directory = Path.GetDirectoryName(databasePath)!;
        var pattern = $".{Path.GetFileName(databasePath)}.*.initializing";
        foreach (var stagingPath in Directory.EnumerateFiles(directory, pattern))
        {
            DeleteIfPresent(stagingPath);
            DeleteIfPresent(stagingPath + "-wal");
            DeleteIfPresent(stagingPath + "-shm");
            DeleteIfPresent(stagingPath + "-journal");
        }
    }

    private static async Task ValidateExistingAsync(
        string databasePath,
        Microsoft.EntityFrameworkCore.Metadata.IModel model,
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 5
        };

        try
        {
            await using var connection = new SqliteConnection(builder.ToString());
            await connection.OpenAsync(cancellationToken);
            var applicationId = await ReadPragmaAsync(connection, "application_id", cancellationToken);
            var schemaVersion = await ReadPragmaAsync(connection, "user_version", cancellationToken);
            var tables = await ReadTablesAsync(connection, cancellationToken);

            if (applicationId != ApplicationId)
            {
                var detail = tables.Count == 0 && applicationId == 0 && schemaVersion == 0
                    ? "该 SQLite 文件尚未初始化。"
                    : "数据库没有 OpenERP 本机版本标记。";
                throw new InvalidOperationException($"{detail} 为保护现有数据，不会继续写入或自动转换。");
            }

            if (schemaVersion != SchemaVersion)
                throw new InvalidOperationException(
                    $"本机数据库 schema 版本为 {schemaVersion}，当前程序只支持版本 {SchemaVersion}；为保护数据，已拒绝打开。");

            var expectedTables = model.GetEntityTypes()
                .Select(entity => entity.GetTableName())
                .Where(table => !string.IsNullOrWhiteSpace(table))
                .ToHashSet(StringComparer.Ordinal);
            var missingTables = expectedTables.Except(tables, StringComparer.Ordinal).ToArray();
            if (missingTables.Length > 0)
                throw new InvalidOperationException(
                    $"本机数据库标记为版本 {SchemaVersion}，但缺少必要表：{string.Join(", ", missingTables)}。为保护数据，已拒绝打开。");
        }
        catch (SqliteException exception)
        {
            throw new InvalidOperationException(
                "无法读取本机 SQLite 数据库。文件可能损坏或不是 OpenERP 数据库；为保护数据，已拒绝修改。",
                exception);
        }
    }

    private static async Task<int> ReadPragmaAsync(
        SqliteConnection connection,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<HashSet<string>> ReadTablesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            tables.Add(reader.GetString(0));
        return tables;
    }

    private sealed class FileLease(FileStream stream) : IDisposable
    {
        private FileStream? _stream = stream;
        public void Dispose() => Interlocked.Exchange(ref _stream, null)?.Dispose();
    }
}
