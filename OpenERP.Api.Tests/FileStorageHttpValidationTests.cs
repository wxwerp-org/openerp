using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace OpenERP.Api.Tests;

// 被验证的现有用例从进程环境读取目标；独占运行并在 finally 中恢复，避免影响其他用例。
[CollectionDefinition(nameof(FileStorageHttpValidationTests), DisableParallelization = true)]
public sealed class FileStorageHttpValidationCollection;

[Collection(nameof(FileStorageHttpValidationTests))]
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
        app.MapPost("/api/files/", () =>
        {
            Interlocked.Increment(ref uploadRequests);
            return Results.BadRequest();
        });
        app.MapGet("/health", () => Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
        await app.StartAsync(ct);

        var previousUrl = Environment.GetEnvironmentVariable("OPENERP_TEST_API_URL");
        var previousRoot = Environment.GetEnvironmentVariable("OPENERP_TEST_REPO_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("OPENERP_TEST_API_URL", app.Urls.Single());
            Environment.SetEnvironmentVariable("OPENERP_TEST_REPO_ROOT",
                Path.Combine(Path.GetTempPath(), $"openerp-missing-{Guid.NewGuid():N}"));

            // When 执行现有非法格式、空文件或超限上传用例。
            // Then 用例仍能收到并断言 HTTP 拒绝响应，不要求 Aspire 或存储健康。
            var cases = new FileStorageIntegrationTests();
            if (value is null)
                await cases.Upload_rejects_files_over_the_size_limit();
            else
                await cases.Upload_rejects_unsupported_or_empty_files(fileName, value);
            Assert.Equal(1, uploadRequests);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENERP_TEST_API_URL", previousUrl);
            Environment.SetEnvironmentVariable("OPENERP_TEST_REPO_ROOT", previousRoot);
            await app.StopAsync(ct);
        }
    }
}
