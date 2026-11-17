using System.Data;
using System.Data.Common;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace OpenERP.Api.Deployment;

internal static class ContainerCommands
{
    private const string RuntimeConfiguration = "OPENERP_RUNTIME_CONFIG";
    private const string SeaweedEndpoint = "http://127.0.0.1:8333";
    private const string SchemaIncompatible = "当前 PostgreSQL 结构与 OpenERP 迁移记录不兼容。";
    private const int Incompatible = 12;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static async Task<int> RunAsync(string[] args)
    {
        var runtimeDirectory = Environment.GetEnvironmentVariable("OPENERP_RUNTIME_DIRECTORY") ?? "/run/openerp";
        switch (args)
        {
            case ["container-config"]:
                return WriteConfiguration(
                    Environment.GetEnvironmentVariable("OPENERP_SECRETS_DIRECTORY") ?? "/run/secrets", runtimeDirectory);
            case ["container-schema"]:
                return await CheckSchemaAsync(runtimeDirectory);
            case ["container-storage-ready"]:
                return await CheckStorageAsync(runtimeDirectory);
            default:
                Console.Error.WriteLine("容器命令或参数无效。");
                return 1;
        }
    }

    public static IConfigurationBuilder AddRuntimeConfiguration(IConfigurationBuilder builder)
    {
        var path = Environment.GetEnvironmentVariable(RuntimeConfiguration);
        if (string.IsNullOrEmpty(path))
            return builder;
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("OPENERP_RUNTIME_CONFIG 必须使用绝对路径。");
        return builder.AddJsonFile(path, optional: false);
    }

    internal static int WriteConfiguration(string secretsDirectory, string runtimeDirectory)
    {
        try
        {
            if (!TryReadSecret(secretsDirectory, "postgres-password", required: true, out var postgres)
                || !TryReadSecret(secretsDirectory, "redis-password", required: true, out var redis)
                || !TryReadSecret(secretsDirectory, "s3-access-key", required: true, out var accessKey)
                || !TryReadSecret(secretsDirectory, "s3-secret-key", required: true, out var secretKey)
                || !TryReadSecret(secretsDirectory, "admin-initial-password", required: false, out var initialPassword)
                || !TryReadSecret(secretsDirectory, "erp-access-token", required: false, out var erpToken))
                return 1;

            var postgresConnection = new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1",
                Database = "openerp",
                Username = "openerp",
                Password = postgres,
                SearchPath = "public"
            };
            var seaweedConnection = new DbConnectionStringBuilder
            {
                ["Endpoint"] = SeaweedEndpoint,
                ["AccessKey"] = accessKey,
                ["SecretKey"] = secretKey
            };
            var settings = JsonSerializer.Serialize(new
            {
                ConnectionStrings = new
                {
                    openerp = postgresConnection.ConnectionString,
                    redis = "127.0.0.1:6379",
                    seaweedfs = seaweedConnection.ConnectionString
                },
                Deployment = new { RedisPassword = redis },
                Authentication = new { InitialPassword = initialPassword },
                Erp = new { AccessToken = erpToken }
            });
            var s3 = JsonSerializer.Serialize(new
            {
                identities = new[]
                {
                    new
                    {
                        name = "openerp",
                        credentials = new[] { new { accessKey, secretKey } },
                        actions = new[] { "Admin", "Read", "Write", "List", "Tagging" }
                    }
                }
            });
            var redisPassword = redis!.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var redisConfig = $"""
                bind 127.0.0.1
                port 6379
                dir /var/lib/redis
                save 60 1
                requirepass "{redisPassword}"
                """ + "\n";

            Directory.CreateDirectory(runtimeDirectory);
            WritePrivateFile(runtimeDirectory, "appsettings.json", settings);
            WritePrivateFile(runtimeDirectory, "redis.conf", redisConfig);
            WritePrivateFile(runtimeDirectory, "s3.json", s3);
            WritePrivateFile(runtimeDirectory, "postgres-password", postgres!);
            WritePrivateFile(runtimeDirectory, "redis-password", redis);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("无法生成容器运行配置，请检查密钥文件或运行目录。");
            return 1;
        }
    }

    private static bool TryReadSecret(string directory, string name, bool required, out string? value)
    {
        var path = Path.Combine(directory, name);
        value = null;
        if (File.Exists(path))
        {
            value = StrictUtf8.GetString(File.ReadAllBytes(path));
            if (value.EndsWith("\r\n", StringComparison.Ordinal))
                value = value[..^2];
            else if (value.EndsWith('\n'))
                value = value[..^1];
        }

        if ((value is null && !required)
            || (value is not null && (!required || value.Length != 0) && value.IndexOfAny(['\r', '\n', '\0']) < 0))
            return true;

        Console.Error.WriteLine($"密钥文件 {name} 缺失、为空或包含无效的换行符或 NUL。");
        return false;
    }

    private static void WritePrivateFile(string directory, string name, string contents)
    {
        var temporary = Path.Combine(directory, $".{name}.{Guid.NewGuid():N}");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        try
        {
            using (var writer = new StreamWriter(temporary, StrictUtf8, options))
                writer.Write(contents);
            File.Move(temporary, Path.Combine(directory, name), overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static IConfigurationRoot LoadRuntimeConfiguration(string directory)
    {
        var builder = new ConfigurationBuilder();
        return string.IsNullOrEmpty(Environment.GetEnvironmentVariable(RuntimeConfiguration))
            ? builder.AddJsonFile(Path.GetFullPath(Path.Combine(directory, "appsettings.json"))).Build()
            : AddRuntimeConfiguration(builder).Build();
    }

    private static async Task<int> CheckSchemaAsync(string runtimeDirectory)
    {
        try
        {
            var configuration = LoadRuntimeConfiguration(runtimeDirectory);
            var connectionString = configuration.GetConnectionString("openerp");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.Error.WriteLine("缺少 PostgreSQL 连接配置。");
                return 1;
            }

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            await using var context = new AppDbContext(options);
            var migrations = context.GetService<IMigrationsAssembly>();
            var expectedMigrations = migrations.Migrations.Keys.Order(StringComparer.Ordinal).ToArray();
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var cancellation = timeout.Token;
            await connection.OpenAsync(cancellation);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellation);
            await using var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction);
            await readOnly.ExecuteNonQueryAsync(cancellation);

            // 包括业务表、视图及独立序列，忽略 PostgreSQL 系统对象和列自带的序列。
            const string catalogSql = """
                SELECT n.nspname, c.relname, c.relkind::text, a.attname
                FROM pg_catalog.pg_class c
                JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                LEFT JOIN pg_catalog.pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
                WHERE left(n.nspname, 3) <> 'pg_' AND n.nspname <> 'information_schema'
                  AND c.relkind IN ('r', 'p', 'f', 'v', 'm', 'S')
                  AND (c.relkind <> 'S' OR NOT EXISTS (
                    SELECT FROM pg_catalog.pg_depend d
                    WHERE d.classid = 'pg_catalog.pg_class'::regclass AND d.objid = c.oid
                      AND d.refclassid = 'pg_catalog.pg_class'::regclass AND d.refobjsubid > 0 AND d.deptype IN ('a', 'i')
                  ))
                """;
            var objects = new HashSet<(string Schema, string Table, string Kind, string? Column)>();
            await using var catalog = new NpgsqlCommand(catalogSql, connection, transaction);
            await using (var reader = await catalog.ExecuteReaderAsync(cancellation))
            {
                while (await reader.ReadAsync(cancellation))
                    objects.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3)));
            }

            if (objects.Count == 0)
            {
                // 依赖 public 的对象还包括域、类型、例程等无关系对象的定义。
                const string pristineSql = """
                    SELECT NOT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_namespace
                        WHERE left(nspname, 3) <> 'pg_' AND nspname NOT IN ('public', 'information_schema')
                        UNION ALL
                        SELECT 1 FROM pg_catalog.pg_depend d
                        JOIN pg_catalog.pg_namespace n ON n.oid = d.refobjid
                        WHERE d.refclassid = 'pg_catalog.pg_namespace'::regclass AND n.nspname = 'public'
                        UNION ALL
                        SELECT 1 FROM pg_catalog.pg_extension WHERE extname <> 'plpgsql'
                    )
                    """;
                await using var pristine = new NpgsqlCommand(pristineSql, connection, transaction);
                return await pristine.ExecuteScalarAsync(cancellation) is true
                    ? ClassifySchema(expectedMigrations, [], hasExpectedStructure: true)
                    : RejectSchema();
            }

            var history = objects.Where(item => item is { Schema: "public", Table: "__EFMigrationsHistory" }).ToHashSet();
            if (!history.SetEquals([
                ("public", "__EFMigrationsHistory", "r", "MigrationId"),
                ("public", "__EFMigrationsHistory", "r", "ProductVersion")]))
                return RejectSchema();

            const string appliedSql = """
                SELECT "MigrationId"::text FROM public."__EFMigrationsHistory" ORDER BY ("MigrationId"::text) COLLATE "C"
                """;
            var applied = new List<string>();
            await using var readApplied = new NpgsqlCommand(appliedSql, connection, transaction);
            await using (var reader = await readApplied.ExecuteReaderAsync(cancellation))
            {
                while (await reader.ReadAsync(cancellation))
                {
                    if (reader.IsDBNull(0))
                        return RejectSchema();
                    applied.Add(reader.GetString(0));
                }
            }
            if (applied.Count == 0 || applied.Count > expectedMigrations.Length)
                return RejectSchema();

            var since = expectedMigrations[applied.Count - 1];
            var target = migrations.CreateMigration(migrations.Migrations[since], context.Database.ProviderName!).TargetModel;
            objects.ExceptWith(history);
            var expected = target.GetEntityTypes().SelectMany(entity =>
            {
                var table = StoreObjectIdentifier.Create(entity, StoreObjectType.Table);
                if (table is not { } store)
                    return Enumerable.Empty<(string, string, string, string?)>();
                return entity.GetProperties()
                    .Select(property => property.GetColumnName(store))
                    .Where(column => column is not null)
                    .Select(column => (store.Schema ?? "public", store.Name, "r", column));
            });
            var result = ClassifySchema(expectedMigrations, applied, objects.SetEquals(expected));
            return result == Incompatible ? RejectSchema() : result;
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UndefinedColumn
            or PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.DatatypeMismatch)
        {
            return RejectSchema();
        }
        catch (Exception exception) when (exception is NpgsqlException or IOException or InvalidDataException or FormatException
            or ArgumentException or UnauthorizedAccessException or OperationCanceledException)
        {
            Console.Error.WriteLine("无法读取 PostgreSQL 结构，请检查连接和配置。");
            return 1;
        }
    }

    internal static int ClassifySchema(
        string[] expectedMigrations, IReadOnlyList<string> appliedMigrations, bool hasExpectedStructure)
    {
        if (!hasExpectedStructure
            || !expectedMigrations.Take(appliedMigrations.Count).SequenceEqual(appliedMigrations, StringComparer.Ordinal))
            return Incompatible;

        if (appliedMigrations.Count == 0)
            return 10;

        return appliedMigrations.Count == expectedMigrations.Length ? 0 : 11;
    }

    private static int RejectSchema()
    {
        Console.Error.WriteLine(SchemaIncompatible);
        return Incompatible;
    }

    private static async Task<int> CheckStorageAsync(string runtimeDirectory)
    {
        try
        {
            var configuration = LoadRuntimeConfiguration(runtimeDirectory);
            var s3Connection = new DbConnectionStringBuilder
            {
                ConnectionString = configuration.GetConnectionString("seaweedfs")
            };
            if (!s3Connection.TryGetValue("AccessKey", out var accessKey)
                || !s3Connection.TryGetValue("SecretKey", out var secretKey)
                || !s3Connection.TryGetValue("Endpoint", out var endpoint))
                return 1;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var http = new HttpClient();
            var topology = await http.GetFromJsonAsync<JsonElement>(
                "http://127.0.0.1:9333/dir/status", timeout.Token);
            if (!HasRegisteredVolume(topology))
                return 1;

            using var s3 = new AmazonS3Client((string)accessKey, (string)secretKey, new AmazonS3Config
            {
                ServiceURL = (string)endpoint,
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
                MaxErrorRetry = 0
            });
            await s3.ListBucketsAsync(new ListBucketsRequest(), timeout.Token);
            return 0;
        }
        catch (Exception exception) when (exception is AmazonClientException or AmazonServiceException
            or ArgumentException or HttpRequestException or IOException or InvalidDataException or FormatException or JsonException
            or UnauthorizedAccessException or OperationCanceledException)
        {
            return 1;
        }
    }

    internal static bool HasRegisteredVolume(JsonElement status) =>
        status.ValueKind == JsonValueKind.Object
        && status.TryGetProperty("Topology", out var topology)
        && topology.ValueKind == JsonValueKind.Object
        && topology.TryGetProperty("Max", out var max)
        && max.ValueKind == JsonValueKind.Number
        && max.TryGetInt64(out var capacity)
        && capacity > 0;
}
