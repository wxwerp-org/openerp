using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class FileStorageIntegrationTests
{
    private const long MaxFileSize = 20 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static string? ConfiguredApiUrl => Environment.GetEnvironmentVariable("OPENERP_TEST_API_URL");

    [Fact]
    public async Task Files_survive_SeaweedFS_restart_and_support_the_full_lifecycle()
    {
        var runAspire = CreateAspireRunner();
        using var client = await CreateApiClientAsync(ConfiguredApiUrl, runAspire);
        var createdKeys = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var samples = new[]
            {
                new UploadSample("重复名称.png", "image/png", "image-one.png"),
                new UploadSample("重复名称.png", "image/png", "image-two.png"),
                new UploadSample("验收文件.pdf", "application/pdf", "sample.pdf"),
                new UploadSample("验收表格.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "sample.xlsx")
            };
            var uploaded = new List<UploadedSample>();

            foreach (var sample in samples)
            {
                var bytes = await File.ReadAllBytesAsync(GetFixturePath(sample.FixtureName));
                using var form = new MultipartFormDataContent();
                using var content = new ByteArrayContent(bytes);
                content.Headers.ContentType = new("application/octet-stream");
                form.Add(content, "file", sample.FileName);

                using var response = await client.PostAsync("api/files/", form);
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                var file = await response.Content.ReadFromJsonAsync<StoredFileResponse>(JsonOptions);
                Assert.NotNull(file);
                Assert.False(string.IsNullOrWhiteSpace(file.Key));
                createdKeys.Add(file.Key);
                uploaded.Add(new(sample, bytes, file));

                Assert.Equal(sample.FileName, file.FileName);
                Assert.Equal(sample.ContentType, file.ContentType);
                Assert.Equal(bytes.LongLength, file.Size);
            }

            Assert.Equal(uploaded.Count, createdKeys.Count);
            Assert.NotEqual(uploaded[0].StoredFile.Key, uploaded[1].StoredFile.Key);

            await RestartStorageAsync(runAspire);

            using var listResponse = await client.GetAsync("api/files/");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var listedFiles = await listResponse.Content.ReadFromJsonAsync<StoredFileResponse[]>(JsonOptions);
            Assert.NotNull(listedFiles);

            foreach (var uploadedFile in uploaded)
            {
                var listed = Assert.Single(listedFiles, item => item.Key == uploadedFile.StoredFile.Key);
                Assert.Equal(uploadedFile.Sample.FileName, listed.FileName);
                Assert.Equal(uploadedFile.Sample.ContentType, listed.ContentType);
                Assert.Equal(uploadedFile.Bytes.LongLength, listed.Size);

                var path = $"api/files/{Uri.EscapeDataString(listed.Key)}";
                using var download = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, download.StatusCode);
                Assert.Equal(uploadedFile.Sample.ContentType, download.Content.Headers.ContentType?.MediaType);
                var downloadedBytes = await download.Content.ReadAsByteArrayAsync();
                Assert.Equal(uploadedFile.Bytes, downloadedBytes);
                Assert.Equal(SHA256.HashData(uploadedFile.Bytes), SHA256.HashData(downloadedBytes));

                using var attachmentDownload = await client.GetAsync($"{path}?download=true");
                Assert.Equal(HttpStatusCode.OK, attachmentDownload.StatusCode);
                Assert.Equal(uploadedFile.Sample.ContentType, attachmentDownload.Content.Headers.ContentType?.MediaType);
                Assert.Equal("attachment", attachmentDownload.Content.Headers.ContentDisposition?.DispositionType);
                var attachmentBytes = await attachmentDownload.Content.ReadAsByteArrayAsync();
                Assert.Equal(uploadedFile.Bytes, attachmentBytes);
                Assert.Equal(SHA256.HashData(uploadedFile.Bytes), SHA256.HashData(attachmentBytes));
            }

            foreach (var key in createdKeys)
            {
                var path = $"api/files/{Uri.EscapeDataString(key)}";
                using var deleteResponse = await client.DeleteAsync(path);
                Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

                using var repeatedDelete = await client.DeleteAsync(path);
                Assert.Equal(HttpStatusCode.NoContent, repeatedDelete.StatusCode);

                using var missingFile = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.NotFound, missingFile.StatusCode);
            }
        }
        finally
        {
            foreach (var key in createdKeys)
            {
                try
                {
                    using var response = await client.DeleteAsync($"api/files/{Uri.EscapeDataString(key)}");
                }
                catch
                {
                    // 清理失败不能掩盖原始测试失败。
                }
            }
        }
    }

    [Theory]
    [InlineData("unsupported.txt", "not an allowed file type")]
    [InlineData("empty.png", "")]
    public async Task Upload_rejects_unsupported_or_empty_files(string fileName, string value)
    {
        using var client = CreateApiClient();
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(value));
        form.Add(content, "file", fileName);

        using var response = await client.PostAsync("api/files/", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_rejects_files_over_the_size_limit()
    {
        using var client = CreateApiClient();
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(new byte[MaxFileSize + 1]);
        content.Headers.ContentType = new("application/octet-stream");
        form.Add(content, "file", "too-large.png");

        using var response = await client.PostAsync("api/files/", form);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge,
            $"Expected 400 or 413 for an oversized upload, got {(int)response.StatusCode} ({response.StatusCode}).");
    }

    private static HttpClient CreateApiClient()
    {
        var value = ConfiguredApiUrl;
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("请将 OPENERP_TEST_API_URL 设为正在运行的 OpenERP.Api 基地址。");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var apiUri) || apiUri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("OPENERP_TEST_API_URL 必须是绝对 HTTP 或 HTTPS 地址。");

        var baseAddress = value.EndsWith("/", StringComparison.Ordinal) ? apiUri : new Uri(value + "/");
        return new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromMinutes(3) };
    }

    internal static async Task<HttpClient> CreateApiClientAsync(string? value, Func<string[], Task<string>> runAspire)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("请将 OPENERP_TEST_API_URL 设为所选 AppHost 中 api 的 HTTP 或 HTTPS 根地址。");

        var description = await runAspire(["describe", "api", "--format", "Json"]);
        var endpoints = GetApiEndpoints(description);
        if (endpoints.Length == 0)
            throw new InvalidOperationException("所选 AppHost 的 Aspire describe 未返回可访问的 api HTTP 或 HTTPS 根地址。");

        var apiUri = Uri.TryCreate(value, UriKind.Absolute, out var configuredUri) && IsHttpRoot(configuredUri)
            ? endpoints.SingleOrDefault(endpoint => endpoint == configuredUri)
            : null;
        if (apiUri is null)
        {
            throw new InvalidOperationException(
                $"OPENERP_TEST_API_URL 必须与所选 AppHost 中 api 的一个根地址一致。可用地址为 {string.Join<Uri>("、", endpoints)}。");
        }

        await WaitForResourceAsync(runAspire, "api");
        return new HttpClient { BaseAddress = apiUri, Timeout = TimeSpan.FromMinutes(3) };
    }

    private static Uri[] GetApiEndpoints(string description)
    {
        var resources = string.IsNullOrWhiteSpace(description)
            ? []
            : JsonSerializer.Deserialize<AspireDescription>(description, JsonOptions)?.Resources ?? [];
        return resources.Where(resource => (resource.DisplayName ?? resource.Name) == "api")
            .SelectMany(resource => resource.Urls ?? [])
            .Where(endpoint => !endpoint.IsInternal)
            .Select(endpoint => Uri.TryCreate(endpoint.Url, UriKind.Absolute, out var uri) ? uri : null)
            .OfType<Uri>()
            .Where(IsHttpRoot)
            .Distinct()
            .ToArray();
    }

    private static bool IsHttpRoot(Uri uri) => uri.Scheme is "http" or "https" && uri.PathAndQuery == "/" &&
        uri.Fragment.Length == 0 && uri.UserInfo.Length == 0;

    internal static async Task RestartStorageAsync(Func<string[], Task<string>> runAspire)
    {
        await runAspire(["resource", "seaweedfs", "restart"]);
        await WaitForResourceAsync(runAspire, "seaweedfs");
        await WaitForResourceAsync(runAspire, "api");
    }

    private static Task WaitForResourceAsync(Func<string[], Task<string>> runAspire, string resource) =>
        runAspire(["wait", resource, "--timeout", "120"]);

    private static string GetFixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static Func<string[], Task<string>> CreateAspireRunner()
    {
        var (repositoryRoot, appHostPath) = FindAppHost();
        return arguments => RunAspireAsync(repositoryRoot,
            [.. arguments, "--apphost", appHostPath, "--non-interactive", "--nologo"]);
    }

    private static (string RepositoryRoot, string AppHostPath) FindAppHost()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("OPENERP_TEST_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var appHost = Path.GetFullPath(Path.Combine(configuredRoot, "OpenERP.AppHost", "OpenERP.AppHost.csproj"));
            if (!File.Exists(appHost))
                throw new InvalidOperationException($"OPENERP_TEST_REPO_ROOT does not contain OpenERP.AppHost/OpenERP.AppHost.csproj: {configuredRoot}");
            return (Path.GetDirectoryName(Path.GetDirectoryName(appHost)!)!, appHost);
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(Path.GetFullPath(start)); directory is not null; directory = directory.Parent)
            {
                var appHost = Path.Combine(directory.FullName, "OpenERP.AppHost", "OpenERP.AppHost.csproj");
                if (File.Exists(appHost))
                    return (directory.FullName, appHost);
            }
        }

        throw new InvalidOperationException("Could not find OpenERP.AppHost/OpenERP.AppHost.csproj. Set OPENERP_TEST_REPO_ROOT to the repository root.");
    }

    private static async Task<string> RunAspireAsync(string repositoryRoot, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("tool");
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("aspire");
        startInfo.ArgumentList.Add("--");
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the local Aspire CLI.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Aspire command timed out: {string.Join(' ', arguments)}");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Aspire command failed with exit code {process.ExitCode}: {string.Join(' ', arguments)}{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");

        return stdout;
    }

    private sealed record UploadSample(string FileName, string ContentType, string FixtureName);

    private sealed record UploadedSample(UploadSample Sample, byte[] Bytes, StoredFileResponse StoredFile);

    private sealed record AspireDescription(AspireResource[]? Resources);

    private sealed record AspireResource(string Name, string? DisplayName, AspireEndpoint[]? Urls);

    private sealed record AspireEndpoint(string? Url, bool IsInternal);

    private sealed record StoredFileResponse(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("fileName")] string FileName,
        [property: JsonPropertyName("contentType")] string ContentType,
        [property: JsonPropertyName("size")] long Size);
}
