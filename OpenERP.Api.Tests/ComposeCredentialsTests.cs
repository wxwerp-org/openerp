using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class ComposeCredentialsTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("openerp-credentials-");

    [Fact]
    public async Task Initializes_distinct_credentials_and_reuses_each_published_group()
    {
        // Given 两个全新部署分别挂载空卷。When 执行同一个镜像入口。
        var first = await RunAsync("first");
        var second = await RunAsync("second");
        Assert.True(first.ExitCode == 0, first.Error);
        Assert.True(second.ExitCode == 0, second.Error);

        // Then 各组凭据一致，各实例之间独立，重复初始化不覆盖原值。
        var original = ReadCredentials("first");
        var other = ReadCredentials("second");
        Assert.True(original.Password != other.Password);
        Assert.True(original.AccessKey != other.AccessKey);
        Assert.True(original.SecretKey != other.SecretKey);
        var repeated = await RunAsync("first");
        Assert.True(repeated.ExitCode == 0, repeated.Error);
        Assert.True(original == ReadCredentials("first"));
    }

    [Fact]
    public async Task Recovers_unpublished_group_after_interruption()
    {
        // Given PostgreSQL 已提交，S3 写卷中断后只剩暂存目录。
        Assert.Equal(0, (await RunAsync("instance")).ExitCode);
        var original = ReadCredentials("instance");
        var storage = Path.Combine(_directory.FullName, "instance", "s3");
        Directory.Move(Path.Combine(storage, "current"), Path.Combine(storage, ".pending-interrupted"));

        // When 重跑初始化。Then 复用数据库密码，补齐缺少的 S3 凭据。
        var result = await RunAsync("instance");
        Assert.True(result.ExitCode == 0, result.Error);
        var recovered = ReadCredentials("instance");
        Assert.True(original.Password == recovered.Password);
        Assert.True(original.SecretKey != recovered.SecretKey);
    }

    [Fact]
    public async Task Passfile_uses_the_published_postgres_connection()
    {
        // Given 发布模型提供了不同的 PostgreSQL 服务端点、数据库和用户。
        const string connection = "Host=db;Port=5433;Database=other;Username=operator";

        // When 用发布模型的连接配置初始化。Then pgpass 使用该连接的四个匹配字段。
        var result = await RunAsync("instance", connection);
        Assert.True(result.ExitCode == 0, result.Error);
        ReadCredentials("instance", "db:5433:other:operator");
    }

    [Fact]
    public async Task Reuses_s3_credentials_when_only_the_permissions_change()
    {
        // Given S3 配置仍使用相同密钥，只将权限列表改成等效的 Admin。
        Assert.Equal(0, (await RunAsync("instance")).ExitCode);
        var original = ReadCredentials("instance");
        var path = Path.Combine(_directory.FullName, "instance", "s3", "current", "s3.json");
        var config = JsonNode.Parse(File.ReadAllText(path))!;
        config["identities"]![0]!["actions"] = new JsonArray(JsonValue.Create("Admin"), JsonValue.Create("List"));
        File.Delete(path);
        File.WriteAllText(path, config.ToJsonString());

        // When 重跑初始化。Then 复用现有密钥并保留权限配置。
        var result = await RunAsync("instance");
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.True(original == ReadCredentials("instance"));
        Assert.Equal(config.ToJsonString(), File.ReadAllText(path));
    }

    [Theory]
    [InlineData("postgres", "pgpass", false)]
    [InlineData("s3", "client/S3__SecretKey", true)]
    public async Task Rejects_incomplete_or_inconsistent_committed_credentials(string volume, string file, bool replace)
    {
        // Given 已发布的一组凭据缺少文件或与同组其他文件冲突。
        Assert.Equal(0, (await RunAsync("instance")).ExitCode);
        var path = Path.Combine(_directory.FullName, "instance", volume, "current", file);
        File.Delete(path);
        if (replace)
            File.WriteAllText(path, "invalid");

        // When 重跑初始化。Then 失败并保留已有文件状态，供运维从备份恢复。
        var failed = await RunAsync("instance");
        Assert.NotEqual(0, failed.ExitCode);
        Assert.Contains("凭据", failed.Error);
        Assert.Equal(replace, File.Exists(path));
        if (replace)
            Assert.Equal("invalid", File.ReadAllText(path));
    }

    private (string Password, string AccessKey, string SecretKey) ReadCredentials(
        string instance, string postgresIdentity = "postgres:5432:openerp:postgres")
    {
        var root = Path.Combine(_directory.FullName, instance);
        var password = File.ReadAllText(Path.Combine(root, "postgres", "current", "password")).TrimEnd();
        Assert.NotEmpty(password);
        var passfile = File.ReadAllText(Path.Combine(root, "postgres", "current", "pgpass")).TrimEnd();
        Assert.True(passfile == $"{postgresIdentity}:{password}");
        var storage = Path.Combine(root, "s3", "current");
        var accessKey = File.ReadAllText(Path.Combine(storage, "client", "S3__AccessKey")).TrimEnd();
        var secretKey = File.ReadAllText(Path.Combine(storage, "client", "S3__SecretKey")).TrimEnd();
        Assert.NotEmpty(accessKey);
        Assert.NotEmpty(secretKey);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(storage, "s3.json")));
        var credential = json.RootElement.GetProperty("identities")[0].GetProperty("credentials")[0];
        Assert.True(accessKey == credential.GetProperty("accessKey").GetString());
        Assert.True(secretKey == credential.GetProperty("secretKey").GetString());
        return (password, accessKey, secretKey);
    }

    private async Task<(int ExitCode, string Error)> RunAsync(
        string instance, string postgresConnection = "Host=postgres;Port=5432;Database=openerp;Username=postgres")
    {
        var root = Path.Combine(_directory.FullName, instance);
        var start = new ProcessStartInfo("dotnet")
        {
            ArgumentList =
            {
                typeof(FileStore).Assembly.Location,
                "initialize-compose-credentials",
                Directory.CreateDirectory(Path.Combine(root, "postgres")).FullName,
                Directory.CreateDirectory(Path.Combine(root, "s3")).FullName
            },
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["ConnectionStrings__openerp"] = postgresConnection;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动凭据初始化测试进程。");
        var error = process.StandardError.ReadToEndAsync(ct);
        var output = process.StandardOutput.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
            await output;
            return (process.ExitCode, await error);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    public void Dispose() => _directory.Delete(recursive: true);
}
