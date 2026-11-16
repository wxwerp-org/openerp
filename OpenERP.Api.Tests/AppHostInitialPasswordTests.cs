using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class AppHostInitialPasswordTests
{
    [Fact]
    public async Task Missing_initial_password_is_generated_and_persisted_across_apphost_recreation()
    {
        // Given 全新的测试专用 Secret 存储，用户没有手工配置管理员密码。
        var secretsId = $"openerp-password-test-{Guid.NewGuid():N}";
        string[] args = ["NO_PROXY=localhost,127.0.0.1,::1", $"ASPIRE_USER_SECRETS_ID={secretsId}"];
        string? secretsPath = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            string? first;
            await using (var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.OpenERP_AppHost>(args, timeout.Token))
            {
                secretsPath = builder.UserSecretsManager.FilePath;
                Assert.Equal(secretsId, new FileInfo(secretsPath!).Directory!.Name);
                var parameter = Assert.Single(builder.Resources.OfType<ParameterResource>(), p => p.Name == "admin-initial-password");
                Assert.True(parameter.Secret);
                // When Aspire 解析首次启动参数，Then 无须人工输入即可得到符合密码规则的随机值。
                first = await parameter.GetValueAsync(timeout.Token);
                Assert.NotNull(first);
                Assert.InRange(first.Length, 6, 20);
            }

            // When 重新构建 AppHost，Then 从 Secret 存储恢复同一个值，不更换临时密码。
            await using var second = await DistributedApplicationTestingBuilder.CreateAsync<Projects.OpenERP_AppHost>(args, timeout.Token);
            var restored = Assert.Single(second.Resources.OfType<ParameterResource>(), p => p.Name == "admin-initial-password");
            Assert.Equal(first, await restored.GetValueAsync(timeout.Token));
        }
        finally
        {
            if (secretsPath is not null && new FileInfo(secretsPath).Directory is { Exists: true } directory && directory.Name == secretsId)
                directory.Delete(recursive: true);
        }
    }
}
