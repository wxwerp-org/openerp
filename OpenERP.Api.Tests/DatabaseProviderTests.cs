using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Migrations;
using OpenERP.Api.Orders;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class DatabaseProviderTests
{
    [Fact]
    public async Task Sqlite_api_and_password_reset_command_use_the_same_instance()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("openerp-instance-");
        var connection = $"Data Source={Path.Combine(directory.FullName, "orders.db")};Pooling=False";
        try
        {
            // Given 只有 SQLite 和本机附件目录的实例，由真正的 API 进程首次创建。
            var run = await AuthenticationStartupTests.RunApiOnDatabaseAsync(connection, null, [], ct,
                configureStart: Configure, verifyListening: async (endpoint, token) =>
                {
                    using var client = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = endpoint };
                    client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
                    await AuthenticationIntegrationTests.RefreshCsrfAsync(client, token);
                    using var initialize = await AuthenticationIntegrationTests.PostJsonAnonymouslyAsync(client,
                        "/api/auth/initialize", new { newPassword = "initial-password" }, token);
                    Assert.Equal(HttpStatusCode.NoContent, initialize.StatusCode);
                    await AuthenticationIntegrationTests.LoginAsync(client, token, "initial-password");
                    using var before = await client.GetAsync("/api/auth/me", token);
                    Assert.Equal(HttpStatusCode.OK, before.StatusCode);

                    // When 同一实例上的独立 CLI 进程重置管理员密码。
                    var reset = await AuthenticationStartupTests.RunApiOnDatabaseAsync(connection, "new-password",
                        ["reset-admin-password", "--stdin"], token, configureStart: Configure);
                    Assert.True(reset.ExitCode == 0, reset.Output);

                    // Then 原 API 进程即时拒绝旧会话，且能用新密码重新登录。
                    using var revoked = await client.GetAsync("/api/auth/me", token);
                    Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
                    await AuthenticationIntegrationTests.LoginAsync(client, token, "new-password");
                    using var current = await client.GetAsync("/api/auth/me", token);
                    Assert.Equal(HttpStatusCode.OK, current.StatusCode);
                });
            Assert.True(run.Started, run.Output);
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        void Configure(ProcessStartInfo start)
        {
            start.Environment["Database__Provider"] = "SQLite";
            start.Environment["ObjectStorage__Provider"] = "Local";
            start.Environment["ObjectStorage__RootPath"] = Path.Combine(directory.FullName, "objects");
            start.Environment.Remove("ConnectionStrings__redis");
            start.Environment.Remove("ConnectionStrings__seaweedfs");
            start.Environment["DOTNET_ENVIRONMENT"] = "Development";
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true";
        }
    }

    [Fact]
    public async Task Sqlite_migrations_create_a_reopenable_database_and_keep_saved_settings()
    {
        // Given 为同一实例选择 SQLite 并指定独立数据库文件。
        var path = Path.Combine(Path.GetTempPath(), $"openerp-{Guid.NewGuid():N}.db");
        string[] args = ["--Database:Provider", "SQLite", "--ConnectionStrings:openerp", $"Data Source={path};Pooling=False"];
        var factory = new AppDbContextFactory();
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await using (var db = factory.CreateDbContext(args))
            {
                Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
                Assert.NotEmpty(db.Database.GetMigrations());
                await db.MigrateDevelopmentDatabaseAsync(ct);
                var result = await new OrderSyncSettingsStore(db).SaveAsync(true, [37, 11, 37], ct);
                Assert.False(result.IsErr);
            }

            // When 重新打开同一个库，再次执行 migrations。
            await using var reopened = factory.CreateDbContext(args);
            await reopened.MigrateDevelopmentDatabaseAsync(ct);

            // Then 已保存的配置完整保留，没有待执行的 migration。
            var settings = await new OrderSyncSettingsStore(reopened).GetAsync(ct);
            Assert.True(settings.Enabled);
            Assert.Equal([11, 37], settings.ShopSids);
            Assert.Empty(await reopened.Database.GetPendingMigrationsAsync(ct));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
