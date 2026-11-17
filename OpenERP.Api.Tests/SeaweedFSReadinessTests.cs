using System.Collections.Concurrent;
using System.Net;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class SeaweedFSReadinessTests
{
    [Theory]
    [InlineData(StatusCodes.Status503ServiceUnavailable)]
    [InlineData(StatusCodes.Status403Forbidden)]
    public async Task Storage_model_health_tracks_S3_probe_failure_and_recovery(int failureStatus)
    {
        // Given 实际 AppHost 模型连接到模拟 Master 健康、S3 返回失败状态的 HTTP fixture。
        var ct = TestContext.Current.CancellationToken;
        var fixtureBuilder = WebApplication.CreateBuilder();
        fixtureBuilder.Logging.ClearProviders();
        fixtureBuilder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var fixture = fixtureBuilder.Build();
        var s3Ready = false;
        var s3Requests = new ConcurrentBag<string>();
        fixture.MapGet("/dir/status", () => Results.Text("""{"Topology":{"Max":100}}""", "application/json"));
        fixture.MapGet("/", (HttpContext context) =>
        {
            s3Requests.Add(context.Request.Headers.Authorization.ToString());
            return s3Ready
                ? Results.Text("""<ListAllMyBucketsResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/"><Owner><ID>test</ID><DisplayName>test</DisplayName></Owner><Buckets /></ListAllMyBucketsResult>""", "application/xml")
                : Results.Text("<Error><Code>ServiceUnavailable</Code><Message>测试 S3 尚不可用</Message></Error>", "application/xml", statusCode: failureStatus);
        });
        await fixture.StartAsync(ct);
        var fixtureUri = new Uri(fixture.Urls.Single());

        // 测试显式提供代理绕过项，避免 Testing 将 launch profile 中的大小写别名重复导入。
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.OpenERP_AppHost>(
            ["NO_PROXY=localhost,127.0.0.1,::1"], ct);
        builder.Configuration["Parameters:seaweedfs-secretKey"] = "test-only-secret";
        var storage = builder.Resources.Single(resource => resource.Name == "seaweedfs");
        var api = builder.Resources.Single(resource => resource.Name == "api");
        Assert.Contains(api.Annotations.OfType<WaitAnnotation>(), annotation =>
            ReferenceEquals(annotation.Resource, storage) && annotation.WaitType == WaitType.WaitUntilHealthy);
        foreach (var endpoint in storage.Annotations.OfType<EndpointAnnotation>())
            endpoint.AllocatedEndpoint = new AllocatedEndpoint(endpoint, fixtureUri.Host, fixtureUri.Port);

        // 仅检查模型和探针，不启动 API 或容器，也不验证真实重启或签名有效性。
        builder.Services.RemoveAll<IHostedService>();
        await using var app = await builder.BuildAsync(ct);
        var healthChecks = app.Services.GetRequiredService<HealthCheckService>();
        var keys = storage.Annotations.OfType<HealthCheckAnnotation>().Select(annotation => annotation.Key).ToHashSet();

        // When 直接执行该存储资源在模型中注册的全部健康探针。
        var waiting = await healthChecks.CheckHealthAsync(registration => keys.Contains(registration.Name), ct);

        // Then 聚合状态为不健康，且请求带有预期的签名头前缀。
        Assert.Equal(HealthStatus.Unhealthy, waiting.Status);
        Assert.Contains(waiting.Entries.Values, entry => entry.Status == HealthStatus.Healthy);
        Assert.NotEmpty(s3Requests);
        Assert.All(s3Requests, authorization => Assert.StartsWith("AWS4-HMAC-SHA256 Credential=admin/", authorization));

        // Given fixture 改为返回成功；Then 下一次探测的聚合状态为健康。
        s3Ready = true;
        var ready = await healthChecks.CheckHealthAsync(registration => keys.Contains(registration.Name), ct);
        Assert.Equal(HealthStatus.Healthy, ready.Status);

        // When fixture 再次失败并恢复；Then 后续探测分别得到不健康和健康状态。
        s3Ready = false;
        var restarting = await healthChecks.CheckHealthAsync(registration => keys.Contains(registration.Name), ct);
        Assert.Equal(HealthStatus.Unhealthy, restarting.Status);
        s3Ready = true;
        var recovered = await healthChecks.CheckHealthAsync(registration => keys.Contains(registration.Name), ct);
        Assert.Equal(HealthStatus.Healthy, recovered.Status);
        await fixture.StopAsync(ct);
    }
}
