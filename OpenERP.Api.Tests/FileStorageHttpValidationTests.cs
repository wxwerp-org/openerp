using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class FileStorageHttpValidationTests
{
    [Theory]
    [InlineData("unsupported.txt", "not an allowed file type")]
    [InlineData("empty.png", "")]
    [InlineData("too-large.png", null)]
    public async Task Upload_validation_cases_work_without_an_AppHost(string fileName, string? value)
    {
        // Given HTTP fixture 返回上传校验错误、健康检查失败，且未提供可用的 AppHost。
        var ct = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        var uploadRequests = 0;
        var healthRequests = 0;
        app.MapPost("/api/files/", async (HttpRequest request) =>
        {
            // 先读完请求体，让超限用例也能收到 fixture 的拒绝响应。
            await request.Body.CopyToAsync(Stream.Null, request.HttpContext.RequestAborted);
            Interlocked.Increment(ref uploadRequests);
            return Results.BadRequest();
        });
        app.MapGet("/health", () =>
        {
            Interlocked.Increment(ref healthRequests);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        });
        await app.StartAsync(ct);

        // When 通过独立客户端执行集成测试共用的上传校验。
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        if (value is null)
            await FileStorageIntegrationTests.AssertOversizedUploadAsync(client, ct);
        else
            await FileStorageIntegrationTests.AssertUnsupportedOrEmptyUploadAsync(client, fileName, value, ct);

        // Then 校验只使用上传入口，无需查询 AppHost 或 API 健康状态。
        Assert.Equal(1, uploadRequests);
        Assert.Equal(0, healthRequests);
    }
}
