using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using OpenERP.Api.About;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class AboutEndpointTests
{
    [Fact]
    public async Task About_is_anonymous_and_returns_only_three_build_fields()
    {
        // Given 宿主按登录必需配置了 fallback 授权策略，本地构建未注入 CI 版本信息。
        var ct = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        ErpEndpointTestAuth.Configure(builder);
        await using var app = builder.Build();
        app.MapAbout();
        await app.StartAsync(ct);
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        // When 未认证请求关于接口。
        using var response = await http.GetAsync("/api/about", ct);

        // Then 匿名返回 200，且响应只有 version、commit、buildTime，缺字段按约定兜底。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(["version", "commit", "buildTime"],
            body.EnumerateObject().Select(property => property.Name));
        Assert.Equal("0.1.0-dev", body.GetProperty("version").GetString());
        Assert.Equal("", body.GetProperty("commit").GetString());
        Assert.Equal("", body.GetProperty("buildTime").GetString());
    }

    [Fact]
    public void Build_info_parses_injected_release_tag_and_strips_sdk_metadata()
    {
        // Given CI 注入 Release tag（SDK 自动追加 +<sha>）与单独注入的提交和构建时间。
        // When 读取程序集版本信息。
        var info = BuildInfo.FromValues("v1.2.3+ab981fa9c0ffee", "ab981fa", "2026-11-20T07:47:39Z");

        // Then version 去掉元数据后缀，其余字段原样保留。
        Assert.Equal(new AboutInfo("v1.2.3", "ab981fa", "2026-11-20T07:47:39Z"), info);
    }

    [Fact]
    public void Build_info_falls_back_when_values_are_missing()
    {
        // Given CI 没有注入版本字段，或只注入了空白值。
        // When 读取版本信息。
        var missing = BuildInfo.FromValues(null, null, null);
        var blank = BuildInfo.FromValues("  ", "", "   ");

        // Then version 兜底为 0.1.0-dev，commit 与 buildTime 为空字符串。
        Assert.Equal(new AboutInfo("0.1.0-dev", "", ""), missing);
        Assert.Equal(new AboutInfo("0.1.0-dev", "", ""), blank);
    }
}
