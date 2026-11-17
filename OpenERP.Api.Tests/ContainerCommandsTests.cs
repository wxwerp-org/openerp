using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using OpenERP.Api.Deployment;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class ContainerCommandsTests : IDisposable
{
    private readonly DirectoryInfo _temporary = Directory.CreateTempSubdirectory("openerp-container-tests-");
    private string SecretsDirectory => Path.Combine(_temporary.FullName, "secrets");
    private string RuntimeDirectory => Path.Combine(_temporary.FullName, "runtime");

    [Fact]
    public void Given_有效单行密钥_When_生成运行配置_Then_保留原值且限制文件权限()
    {
        const string postgresPassword = """db;Password="test" """;
        const string redisPassword = """ redis,="\test" """;
        const string s3AccessKey = """access;Endpoint="test" """;
        const string s3SecretKey = """ secret;="test" """;
        WriteSecrets(postgresPassword, redisPassword, s3AccessKey, s3SecretKey);
        File.WriteAllText(Path.Combine(SecretsDirectory, "admin-initial-password"), " temp password \r\n");
        File.WriteAllText(Path.Combine(SecretsDirectory, "erp-access-token"), " token \n");

        Assert.Equal(0, ContainerCommands.WriteConfiguration(SecretsDirectory, RuntimeDirectory));

        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(RuntimeDirectory, "appsettings.json")));
        var connections = settings.RootElement.GetProperty("ConnectionStrings");
        var database = new NpgsqlConnectionStringBuilder(connections.GetProperty("openerp").GetString());
        Assert.Equal("127.0.0.1", database.Host);
        Assert.Equal("openerp", database.Database);
        Assert.Equal("openerp", database.Username);
        Assert.Equal(postgresPassword, database.Password);
        Assert.Equal("127.0.0.1:6379", connections.GetProperty("redis").GetString());
        Assert.Equal(redisPassword, settings.RootElement.GetProperty("Deployment").GetProperty("RedisPassword").GetString());
        var seaweed = new DbConnectionStringBuilder { ConnectionString = connections.GetProperty("seaweedfs").GetString() };
        Assert.Equal("http://127.0.0.1:8333", seaweed["Endpoint"]);
        Assert.Equal(s3AccessKey, seaweed["AccessKey"]);
        Assert.Equal(s3SecretKey, seaweed["SecretKey"]);
        Assert.Equal(" temp password ", settings.RootElement.GetProperty("Authentication").GetProperty("InitialPassword").GetString());
        Assert.Equal(" token ", settings.RootElement.GetProperty("Erp").GetProperty("AccessToken").GetString());
        Assert.Equal(postgresPassword, File.ReadAllText(Path.Combine(RuntimeDirectory, "postgres-password")));
        Assert.Equal(redisPassword, File.ReadAllText(Path.Combine(RuntimeDirectory, "redis-password")));

        var redis = File.ReadAllText(Path.Combine(RuntimeDirectory, "redis.conf"));
        Assert.Contains("dir /var/lib/redis\n", redis);
        Assert.Contains("bind 127.0.0.1\n", redis);
        Assert.Contains("save 60 1\n", redis);
        Assert.Contains("requirepass \" redis,=\\\"\\\\test\\\" \"\n", redis);
        using var s3 = JsonDocument.Parse(File.ReadAllText(Path.Combine(RuntimeDirectory, "s3.json")));
        var identity = Assert.Single(s3.RootElement.GetProperty("identities").EnumerateArray());
        var credentials = Assert.Single(identity.GetProperty("credentials").EnumerateArray());
        Assert.Equal(s3AccessKey, credentials.GetProperty("accessKey").GetString());
        Assert.Equal(s3SecretKey, credentials.GetProperty("secretKey").GetString());
        Assert.Equal(["Admin", "Read", "Write", "List", "Tagging"],
            identity.GetProperty("actions").EnumerateArray().Select(action => action.GetString()));

        if (!OperatingSystem.IsWindows())
        {
            foreach (var file in Directory.GetFiles(RuntimeDirectory))
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
    }

    [Fact]
    public void Given_未提供可选密钥_When_生成运行配置_Then_首次密码和ERP令牌留空()
    {
        WriteSecrets();

        Assert.Equal(0, ContainerCommands.WriteConfiguration(SecretsDirectory, RuntimeDirectory));

        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(RuntimeDirectory, "appsettings.json")));
        Assert.Equal(JsonValueKind.Null, settings.RootElement.GetProperty("Authentication").GetProperty("InitialPassword").ValueKind);
        Assert.Equal(JsonValueKind.Null, settings.RootElement.GetProperty("Erp").GetProperty("AccessToken").ValueKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("first\nsecond")]
    [InlineData("first\rsecond")]
    [InlineData("first\0second")]
    [InlineData("first\n\n")]
    [InlineData(null)]
    public void Given_必需密钥缺失或无效_When_生成运行配置_Then_返回失败且不写入文件(string? secret)
    {
        WriteSecrets();
        var file = Path.Combine(SecretsDirectory, "s3-secret-key");
        if (secret is null)
            File.Delete(file);
        else
            File.WriteAllText(file, secret);

        Assert.Equal(1, ContainerCommands.WriteConfiguration(SecretsDirectory, RuntimeDirectory));
        Assert.False(Directory.Exists(RuntimeDirectory));
    }

    [Fact]
    public void Given_密钥文件包含无效UTF8_When_生成运行配置_Then_返回失败且不写入文件()
    {
        WriteSecrets();
        File.WriteAllBytes(Path.Combine(SecretsDirectory, "postgres-password"), [0xff, 0xfe, 0x61, 0x00]);

        Assert.Equal(1, ContainerCommands.WriteConfiguration(SecretsDirectory, RuntimeDirectory));
        Assert.False(Directory.Exists(RuntimeDirectory));
    }

    [Theory]
    [InlineData("", true, 10)]
    [InlineData("first", true, 11)]
    [InlineData("first,second", true, 0)]
    [InlineData("second", true, 12)]
    [InlineData("second,first", true, 12)]
    [InlineData("first,second,future", true, 12)]
    [InlineData("", false, 12)]
    [InlineData("first", false, 12)]
    [InlineData("first,second", false, 12)]
    public void Given_迁移记录与目录结构_When_检查数据库_Then_返回对应部署状态(
        string migrations, bool hasExpectedStructure, int expected)
    {
        Assert.Equal(expected, ContainerCommands.ClassifySchema(
            ["first", "second"], migrations.Split(',', StringSplitOptions.RemoveEmptyEntries), hasExpectedStructure));
    }

    [Theory]
    [InlineData("""{"Topology":{"Max":8,"Free":0}}""", true)]
    [InlineData("""{"Topology":{"Max":0,"Free":0}}""", false)]
    [InlineData("""{"Version":"4.53"}""", false)]
    [InlineData("""{"Topology":{"Max":"unknown"}}""", false)]
    public void Given_Master拓扑状态_When_检查存储就绪_Then_要求已注册卷服务器(string status, bool expected)
    {
        using var topology = JsonDocument.Parse(status);
        Assert.Equal(expected, ContainerCommands.HasRegisteredVolume(topology.RootElement));
    }

    public void Dispose() => _temporary.Delete(recursive: true);

    private void WriteSecrets(
        string postgres = "postgres-password",
        string redis = "redis-password",
        string access = "s3-access-key",
        string secret = "s3-secret-key")
    {
        Directory.CreateDirectory(SecretsDirectory);
        File.WriteAllText(Path.Combine(SecretsDirectory, "postgres-password"), postgres + "\r\n");
        File.WriteAllText(Path.Combine(SecretsDirectory, "redis-password"), redis + "\n");
        File.WriteAllText(Path.Combine(SecretsDirectory, "s3-access-key"), access);
        File.WriteAllText(Path.Combine(SecretsDirectory, "s3-secret-key"), secret);
    }
}

[Collection(nameof(AspireIntegrationCollection))]
public sealed class ContainerSchemaPostgresTests(AspireIntegrationFixture fixture)
{
    [Theory]
    [InlineData("CREATE SCHEMA imported")]
    [InlineData("CREATE DOMAIN public.imported_code AS text")]
    [InlineData("CREATE TYPE public.imported_state AS ENUM ('new')")]
    [InlineData("CREATE FUNCTION public.imported_count() RETURNS integer LANGUAGE sql AS 'SELECT 1'")]
    public async Task Given_数据库已有非表对象_When_检查是否可首启迁移_Then_返回结构不兼容(string createObject)
    {
        var ct = TestContext.Current.CancellationToken;
        var database = $"openerp_schema_test_{Guid.NewGuid():N}";
        var adminConnectionString = await fixture.GetPostgresConnectionStringAsync(ct);
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync(ct);
        await using var create = new NpgsqlCommand($"""CREATE DATABASE "{database}" """, admin);
        await create.ExecuteNonQueryAsync(ct);
        using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var config = Directory.CreateTempSubdirectory("openerp-schema-test-");
        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = database,
                Pooling = false
            }.ConnectionString;
            var settings = Path.Combine(config.FullName, "appsettings.json");
            await File.WriteAllTextAsync(settings,
                JsonSerializer.Serialize(new { ConnectionStrings = new { openerp = connectionString } }), ct);

            Assert.Equal(10, await CheckSchemaAsync(settings, ct));
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync(ct);
                await using var createUnknown = new NpgsqlCommand(createObject, connection);
                await createUnknown.ExecuteNonQueryAsync(ct);
            }

            Assert.Equal(12, await CheckSchemaAsync(settings, ct));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"""DROP DATABASE "{database}" """, admin);
            await drop.ExecuteNonQueryAsync(cleanup.Token);
            config.Delete(recursive: true);
        }
    }

    private static async Task<int> CheckSchemaAsync(string settings, CancellationToken ct)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            ArgumentList = { typeof(AppDbContext).Assembly.Location, "container-schema" }
        };
        start.Environment["OPENERP_RUNTIME_CONFIG"] = settings;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动容器结构检查命令。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }

        return process.ExitCode;
    }
}
