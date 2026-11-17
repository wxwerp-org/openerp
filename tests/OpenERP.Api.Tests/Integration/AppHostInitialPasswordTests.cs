using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class AppHostInitialPasswordTests
{
    [Fact]
    public async Task AppHost_does_not_declare_an_initial_administrator_password_parameter()
    {
        // Given 一个隔离的 AppHost 配置；When 构建宿主模型。
        var ct = TestContext.Current.CancellationToken;
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.OpenERP_AppHost>(
            ["NO_PROXY=localhost,127.0.0.1,::1", $"ASPIRE_USER_SECRETS_ID=openerp-password-test-{Guid.NewGuid():N}"], ct);

        // Then 初始化不依赖 AppHost 密码参数。
        Assert.DoesNotContain(builder.Resources.OfType<ParameterResource>(), resource => resource.Name == "admin-initial-password");
    }
}
