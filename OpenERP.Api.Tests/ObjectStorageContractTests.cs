using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenERP.Api.ObjectStorage;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class ObjectStorageContractTests
{
    private const string Key = "00000000000000000000000000000001_example.png";
    private const string ContentType = "image/png";

    [Fact]
    public async Task Local_put_replaces_the_content_at_an_existing_key()
    {
        // Given 同一个键已保存旧内容。
        var ct = TestContext.Current.CancellationToken;
        using var local = new LocalStorageDirectory();
        IObjectStorage storage = local.Storage;
        await using var original = new MemoryStream("old"u8.ToArray());
        await storage.PutAsync(Key, original, ContentType, ct);

        // When 用同一个键上传新内容。
        await using var replacement = new MemoryStream("replacement"u8.ToArray());
        await storage.PutAsync(Key, replacement, ContentType, ct);

        // Then 对象仍只有一份，读取到新的内容和大小。
        await AssertObjectAsync(storage, "replacement", ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_local_replacement_keeps_the_original_object(bool cancel)
    {
        // Given 原对象已保存，替换最多只能写入部分临时内容。
        var ct = TestContext.Current.CancellationToken;
        using var local = new LocalStorageDirectory();
        IObjectStorage storage = local.Storage;
        await using var original = new MemoryStream("old"u8.ToArray());
        await storage.PutAsync(Key, original, ContentType, ct);
        using var replacementLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using var replacement = new InterruptedReplacement(cancel, replacementLifetime);

        // When 替换在写入途中取消或读取失败。
        var exception = await Record.ExceptionAsync(() =>
            storage.PutAsync(Key, replacement, ContentType, replacementLifetime.Token));
        if (cancel)
            Assert.IsAssignableFrom<OperationCanceledException>(exception);
        else
            Assert.IsType<IOException>(Assert.IsType<ObjectStorageException>(exception).InnerException);

        // Then 原对象的内容和大小不变。
        await AssertObjectAsync(storage, "old", ct);
    }

    [Fact]
    public async Task Unreachable_S3_endpoint_returns_service_unavailable()
    {
        // Given 已分配但未监听的本机端口，以及真实 AWS SDK 客户端。
        var ct = TestContext.Current.CancellationToken;
        using var unavailable = new Socket(SocketType.Stream, ProtocolType.Tcp);
        unavailable.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var endpoint = $"http://{unavailable.LocalEndPoint}";
        using var s3 = new AmazonS3Client("test-access", "test-secret", new AmazonS3Config
        {
            ServiceURL = endpoint,
            AuthenticationRegion = "us-east-1",
            ForcePathStyle = true,
            MaxErrorRetry = 0,
            HttpClientFactory = new DirectHttpClientFactory()
        });
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Configuration["ObjectStorage:Provider"] = "S3";
        builder.Configuration["ConnectionStrings:seaweedfs"] = $"Endpoint={endpoint};AccessKey=test-access;SecretKey=test-secret";
        builder.AddFileStorage();
        builder.Services.AddSingleton<IAmazonS3>(s3);
        await using var app = builder.Build();
        app.MapFiles();
        await app.StartAsync(ct);

        // When 正常的文件列表请求访问 S3。
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri(app.Urls.Single())
        };
        using var response = await client.GetAsync("/api/files/", ct);

        // Then API 返回文件存储不可用。
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(ct);
        Assert.Equal("文件存储暂时不可用，请稍后重试。", problem?.Detail);
    }

    private static async Task AssertObjectAsync(IObjectStorage storage, string expected, CancellationToken ct)
    {
        var listed = Assert.Single(await storage.ListAsync(ct));
        Assert.Equal(new StoredObject(Key, expected.Length), listed);
        using var opened = Assert.IsType<OpenedObject>(await storage.OpenAsync(Key, ct));
        using var reader = new StreamReader(opened.Content, Encoding.UTF8, leaveOpen: true);
        Assert.Equal(expected.Length, opened.Size);
        Assert.Equal(expected, await reader.ReadToEndAsync(ct));
    }

    private sealed class LocalStorageDirectory : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("openerp-object-tests-");

        public LocalObjectStorage Storage => new(_root.FullName);

        public void Dispose() => _root.Delete(recursive: true);
    }

    private sealed class InterruptedReplacement(bool cancel, CancellationTokenSource lifetime) : MemoryStream
    {
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken ct)
        {
            await destination.WriteAsync("partial"u8.ToArray(), ct);
            if (cancel)
            {
                lifetime.Cancel();
                return;
            }
            throw new IOException("读取替换内容失败。");
        }
    }

    private sealed class DirectHttpClientFactory : Amazon.Runtime.HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) =>
            new(new HttpClientHandler { UseProxy = false });
    }
}
