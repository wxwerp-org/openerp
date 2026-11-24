using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class AppHostStorageSelectionTests
{
    [Fact]
    public async Task Native_launch_profile_selects_local_storage_and_creates_database_directory()
    {
        // Given 从 AppHost 实际 native launch profile 读取配置，并将数据根目录替换为隔离的临时目录。
        var ct = TestContext.Current.CancellationToken;
        var launchSettingsPath = Path.Combine(Projects.OpenERP_AppHost.ProjectPath, "Properties", "launchSettings.json");
        using var launchSettings = JsonDocument.Parse(await File.ReadAllTextAsync(launchSettingsPath, ct));
        var profileEnvironment = launchSettings.RootElement.GetProperty("profiles").GetProperty("native")
            .GetProperty("environmentVariables").EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString()!);

        Assert.Equal("../.openerp/native", profileEnvironment["NativeDataDirectory"]);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"openerp-native-profile-{Guid.NewGuid():N}");
        var dataDirectory = Path.Combine(temporaryRoot, "native");
        profileEnvironment["NativeDataDirectory"] = dataDirectory;
        var args = profileEnvironment.Select(pair =>
            $"--{pair.Key.Replace("__", ":", StringComparison.Ordinal)}={pair.Value}").ToArray();

        try
        {
            await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.OpenERP_AppHost>(args, ct);

            // When 构建真实 AppHost 资源图，Then 不创建容器资源，API 使用本机 SQLite 和附件目录。
            Assert.Empty(builder.Resources.OfType<ContainerResource>());
            var api = Assert.IsAssignableFrom<IResourceWithEnvironment>(
                builder.Resources.Single(resource => resource.Name == "api"));
            await using var app = await builder.BuildAsync(ct);
            var executionContext = app.Services.GetRequiredService<DistributedApplicationExecutionContext>();
            var resourceLogger = app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger<AppHostStorageSelectionTests>();
            var executionConfiguration = await ExecutionConfigurationBuilder.Create(api)
                .WithEnvironmentVariablesConfig()
                .BuildAsync(executionContext, resourceLogger, ct);
            Assert.Null(executionConfiguration.Exception);
            var apiEnvironment = executionConfiguration.EnvironmentVariables
                .ToDictionary(variable => variable.Key, variable => variable.Value);

            Assert.Equal("Sqlite", apiEnvironment["Database__Provider"]);
            var connectionString = Assert.IsType<string>(apiEnvironment["ConnectionStrings__openerp"]);
            var sqlite = new SqliteConnectionStringBuilder(connectionString);
            Assert.Equal(Path.Combine(dataDirectory, "openerp.db"), sqlite.DataSource);
            Assert.Equal(string.Empty, apiEnvironment["ConnectionStrings__redis"]);
            Assert.Equal("Local", apiEnvironment["ObjectStorage__Provider"]);
            Assert.Equal(Path.Combine(dataDirectory, "objects"), apiEnvironment["ObjectStorage__RootPath"]);
            Assert.True(Directory.Exists(Path.GetDirectoryName(sqlite.DataSource)));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("Database:Provider", "Sqlite", "postgres", "redis", "seaweedfs")]
    [InlineData("Cache:Provider", "Memory", "redis", "postgres", "seaweedfs")]
    [InlineData("ObjectStorage:Provider", "Local", "seaweedfs", "postgres", "redis")]
    public async Task Selects_each_local_provider_independently(
        string key, string provider, string omittedResource, string firstRetained, string secondRetained)
    {
        // Given 开发宿主配置中只切换一个 provider；When 构建实际 AppHost 资源图。
        var ct = TestContext.Current.CancellationToken;
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.OpenERP_AppHost>(
            ["NO_PROXY=localhost,127.0.0.1,::1", $"--{key}={provider}",
                "--ConnectionStrings:openerp=Data Source=openerp-test.db", "--ObjectStorage:RootPath=openerp-test-objects"], ct);

        // Then 对应的容器资源消失，其余依赖及应用资源保持可用。
        var names = builder.Resources.Select(resource => resource.Name).ToArray();
        Assert.DoesNotContain(omittedResource, names);
        Assert.Contains(firstRetained, names);
        Assert.Contains(secondRetained, names);
        Assert.Contains("api", names);
        Assert.Contains("web", names);
    }
}
