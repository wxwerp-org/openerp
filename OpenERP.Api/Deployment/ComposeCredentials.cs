using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace OpenERP.Api.Deployment;

internal static class ComposeCredentials
{
    private const string PasswordFile = "password";
    private const string Passfile = "pgpass";
    private const string S3Config = "s3.json";
    private const string S3AccessKey = "client/S3__AccessKey";
    private const string S3SecretKey = "client/S3__SecretKey";

    internal static int Run(string[] args)
    {
        if (args is not [_, var postgresDirectory, var s3Directory])
        {
            Console.Error.WriteLine("用法：initialize-compose-credentials <PostgreSQL 凭据目录> <S3 凭据目录>");
            return 1;
        }

        var prefix = ReadPostgresPassfilePrefix();
        if (prefix is null)
        {
            Console.Error.WriteLine("初始化 PostgreSQL 凭据需要有效的 ConnectionStrings__openerp 连接配置。");
            return 1;
        }

        return EnsureGroup("PostgreSQL", postgresDirectory, directory => CreatePostgres(directory, prefix),
                   directory => ValidatePostgres(directory, prefix)) &&
               EnsureGroup("S3", s3Directory, CreateS3, ValidateS3) ? 0 : 1;
    }

    private static string? ReadPostgresPassfilePrefix()
    {
        NpgsqlConnectionStringBuilder connection;
        try
        {
            connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ConnectionStrings__openerp"));
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database) ||
            string.IsNullOrWhiteSpace(connection.Username))
            return null;

        // pgpass 按连接参数匹配。冒号和反斜杠在文件字段中需要转义。
        static string Escape(string value) => value.Replace("\\", "\\\\").Replace(":", "\\:");

        return FormattableString.Invariant($"{Escape(connection.Host)}:{connection.Port}:{Escape(connection.Database)}:{Escape(connection.Username)}:");
    }

    private static bool EnsureGroup(string name, string volume, Action<string> create, Func<string, bool> validate)
    {
        var current = Path.Combine(volume, "current");
        try
        {
            if (!Directory.Exists(current))
            {
                // 暂存目录和目标位于同一卷。进程中断时消费者不会看到半组文件。
                var pending = Path.Combine(volume, $".pending-{Guid.NewGuid():N}");
                Directory.CreateDirectory(pending);
                try
                {
                    create(pending);
                    try
                    {
                        Directory.Move(pending, current);
                    }
                    catch (IOException) when (Directory.Exists(current))
                    {
                        // 如果另一个初始化进程先发布，继续验证已发布的版本。
                    }
                }
                finally
                {
                    if (Directory.Exists(pending))
                        Directory.Delete(pending, recursive: true);
                }
            }

            if (validate(current))
                return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // 凭据来自文件，错误消息只提供恢复指引。
        }

        Console.Error.WriteLine($"{name} 凭据初始化失败，请检查卷权限和文件完整性，必要时从备份恢复凭据卷。");
        return false;
    }

    private static void CreatePostgres(string directory, string prefix)
    {
        var password = Generate(bytes: 32);
        Write(directory, PasswordFile, password);
        Write(directory, Passfile, prefix + password + "\n");
    }

    private static bool ValidatePostgres(string directory, string prefix)
    {
        var password = Read(directory, PasswordFile);
        return IsHex(password, 64) && Read(directory, Passfile) == prefix + password;
    }

    private static void CreateS3(string directory)
    {
        Directory.CreateDirectory(Path.Combine(directory, "client"));
        var accessKey = Generate(bytes: 10);
        var secretKey = Generate(bytes: 32);
        Write(directory, S3Config, JsonSerializer.Serialize(CreateS3Config(accessKey, secretKey)));
        Write(directory, S3AccessKey, accessKey);
        Write(directory, S3SecretKey, secretKey);
    }

    private static bool ValidateS3(string directory)
    {
        var accessKey = Read(directory, S3AccessKey);
        var secretKey = Read(directory, S3SecretKey);
        return IsHex(accessKey, 20) && IsHex(secretKey, 64) &&
               JsonNode.Parse(Read(directory, S3Config)) is JsonObject config &&
               config["identities"] is JsonArray identities && identities is [JsonObject identity] &&
               identity["actions"] is JsonArray { Count: > 0 } &&
               identity["credentials"] is JsonArray credentials && credentials is [JsonObject credential] &&
               JsonNode.DeepEquals(credential["accessKey"], JsonValue.Create(accessKey)) &&
               JsonNode.DeepEquals(credential["secretKey"], JsonValue.Create(secretKey));
    }

    private static object CreateS3Config(string accessKey, string secretKey) => new
    {
        identities = new[]
        {
            new
            {
                name = "openerp",
                credentials = new[] { new { accessKey, secretKey } },
                actions = new[] { "Admin" }
            }
        }
    };

    private static string Generate(int bytes) => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(bytes));

    private static bool IsHex(string value, int length) => value.Length == length && value.All(char.IsAsciiHexDigit);

    private static string Read(string directory, string file) => File.ReadAllText(Path.Combine(directory, file)).TrimEnd('\r', '\n');

    private static void Write(string directory, string file, string value)
    {
        var path = Path.Combine(directory, file);
        File.WriteAllText(path, value);
        // 相关镜像的运行 UID 不同。只把该卷读权限交给对应的服务。
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    }
}
