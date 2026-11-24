using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class LocalFileStorageHttpTests
{
    [Fact]
    public async Task Local_files_keep_the_HTTP_contract_across_api_restarts()
    {
        // Given 本地存储目录和名称相同的两个文件，文件名的 UTF-8 长度超过常见文件系统的单段限制。
        var ct = TestContext.Current.CancellationToken;
        using var data = new TemporaryStorageDirectory();
        var name = new string('文', 90) + "A.png";
        var uploads = new List<StoredFile>();
        await using (var app = await StartApiAsync(data.Path, ct))
        {
            using var client = CreateClient(app);

            foreach (var contents in new[] { "first file", "second file" })
            {
                using var form = new MultipartFormDataContent();
                using var input = new StringContent(contents, Encoding.UTF8, "application/octet-stream");
                form.Add(input, "file", name);
                using var response = await client.PostAsync("/api/files/", form, ct);
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                var file = Assert.IsType<StoredFile>(await response.Content.ReadFromJsonAsync<StoredFile>(ct));
                Assert.Matches("^[0-9a-f]{32}_" + System.Text.RegularExpressions.Regex.Escape(name) + "$", file.Key);
                Assert.Equal($"/api/files/{Uri.EscapeDataString(file.Key)}", response.Headers.Location?.OriginalString);
                Assert.Equal(name, file.FileName);
                Assert.Equal("image/png", file.ContentType);
                Assert.Equal(Encoding.UTF8.GetByteCount(contents), file.Size);
                uploads.Add(file);
            }
        }

        // When API 使用同一个本地存储目录重新启动。
        await using (var app = await StartApiAsync(data.Path, ct))
        {
            using var client = CreateClient(app);
            using var response = await client.GetAsync("/api/files/", ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var files = Assert.IsType<StoredFile[]>(await response.Content.ReadFromJsonAsync<StoredFile[]>(ct));

            // Then 重名文件保留各自的键、元数据和内容，删除保持幂等。
            Assert.NotEqual(uploads[0].Key, uploads[1].Key);
            Assert.Equal(uploads.OrderBy(x => x.Key, StringComparer.Ordinal), files.OrderBy(x => x.Key, StringComparer.Ordinal));
            for (var i = 0; i < uploads.Count; i++)
            {
                var file = uploads[i];
                var path = $"/api/files/{Uri.EscapeDataString(file.Key)}";
                using var download = await client.GetAsync($"{path}?download=true", ct);
                Assert.Equal(HttpStatusCode.OK, download.StatusCode);
                Assert.Equal(file.Size, download.Content.Headers.ContentLength);
                Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
                Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
                Assert.Equal(name, download.Content.Headers.ContentDisposition?.FileNameStar);
                Assert.Equal("no-store", download.Headers.CacheControl?.ToString());
                Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
                Assert.Equal(i == 0 ? "first file" : "second file", await download.Content.ReadAsStringAsync(ct));
                using var wrongCase = await client.GetAsync($"/api/files/{Uri.EscapeDataString(file.Key.Replace("A.png", "a.png", StringComparison.Ordinal))}", ct);
                Assert.Equal(HttpStatusCode.NotFound, wrongCase.StatusCode);
                using var deleted = await client.DeleteAsync(path, ct);
                Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
                using var deletedAgain = await client.DeleteAsync(path, ct);
                Assert.Equal(HttpStatusCode.NoContent, deletedAgain.StatusCode);
                using var missing = await client.GetAsync(path, ct);
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            }
            Assert.Empty((await client.GetFromJsonAsync<StoredFile[]>("/api/files/", ct))!);
        }
    }

    [Fact]
    public async Task Unavailable_local_directory_returns_service_unavailable()
    {
        // Given API 已启动，本地存储目录后来被文件替换，无法继续列出对象。
        var ct = TestContext.Current.CancellationToken;
        using var data = new TemporaryStorageDirectory();
        await using var app = await StartApiAsync(data.Path, ct);
        Directory.Delete(data.Path);
        await File.WriteAllTextAsync(data.Path, "not a directory", ct);
        using var client = CreateClient(app);

        // When 客户端访问文件列表。
        using var response = await client.GetAsync("/api/files/", ct);

        // Then 返回文件存储的 503 响应。
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(ct);
        Assert.Equal("文件存储暂时不可用，请稍后重试。", problem?.Detail);
    }

    private static HttpClient CreateClient(WebApplication app) =>
        new() { BaseAddress = new Uri(app.Urls.Single()) };

    private static async Task<WebApplication> StartApiAsync(string path, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["ObjectStorage:Provider"] = "Local";
        builder.Configuration["ObjectStorage:RootPath"] = path;
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.AddFileStorage();
        var app = builder.Build();
        try
        {
            app.MapFiles();
            await app.Services.InitializeFileStorageAsync(ct);
            await app.StartAsync(ct);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private sealed class TemporaryStorageDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("openerp-file-tests-").FullName;

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
            else
                File.Delete(Path);
        }
    }
}
