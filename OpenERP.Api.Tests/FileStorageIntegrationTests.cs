using System.Net;
using System.Net.Http.Json;
using System.Text;
using Xunit;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class FileStorageIntegrationTests(AspireIntegrationFixture fixture)
{
    private const long MaxFileSize = 20 * 1024 * 1024;

    [Fact]
    public async Task Files_survive_SeaweedFS_restart_and_support_the_full_lifecycle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateApiClient();
        // 文件请求和重启操作都属于本次测试启动的 AppHost。
        Assert.Equal(fixture.ApiEndpoint, client.BaseAddress);
        var createdKeys = new HashSet<string>(StringComparer.Ordinal);

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
            var bytes = await File.ReadAllBytesAsync(GetFixturePath(sample.FixtureName), cancellationToken);
            using var form = new MultipartFormDataContent();
            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new("application/octet-stream");
            form.Add(content, "file", sample.FileName);

            using var response = await client.PostAsync("api/files/", form, cancellationToken);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var file = await response.Content.ReadFromJsonAsync<StoredFileResponse>(cancellationToken: cancellationToken);
            Assert.NotNull(file);
            Assert.False(string.IsNullOrWhiteSpace(file.Key));
            createdKeys.Add(file.Key);
            uploaded.Add(new(sample, bytes, file));

            Assert.Equal(sample.FileName, file.FileName);
            Assert.Equal(sample.ContentType, file.ContentType);
            Assert.Equal(bytes.LongLength, file.Size);
        }

        Assert.Equal(uploaded.Count, createdKeys.Count);

        await fixture.RestartStorageAsync(cancellationToken);

        using var listResponse = await client.GetAsync("api/files/", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listedFiles = await listResponse.Content.ReadFromJsonAsync<StoredFileResponse[]>(cancellationToken: cancellationToken);
        Assert.NotNull(listedFiles);

        foreach (var uploadedFile in uploaded)
        {
            var listed = Assert.Single(listedFiles, item => item.Key == uploadedFile.StoredFile.Key);
            Assert.Equal(uploadedFile.Sample.FileName, listed.FileName);
            Assert.Equal(uploadedFile.Sample.ContentType, listed.ContentType);
            Assert.Equal(uploadedFile.Bytes.LongLength, listed.Size);

            var path = $"api/files/{Uri.EscapeDataString(listed.Key)}";
            using var download = await client.GetAsync(path, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(uploadedFile.Sample.ContentType, download.Content.Headers.ContentType?.MediaType);
            var downloadedBytes = await download.Content.ReadAsByteArrayAsync(cancellationToken);
            Assert.Equal(uploadedFile.Bytes, downloadedBytes);

            using var attachmentDownload = await client.GetAsync($"{path}?download=true", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, attachmentDownload.StatusCode);
            Assert.Equal(uploadedFile.Sample.ContentType, attachmentDownload.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", attachmentDownload.Content.Headers.ContentDisposition?.DispositionType);
            var attachmentBytes = await attachmentDownload.Content.ReadAsByteArrayAsync(cancellationToken);
            Assert.Equal(uploadedFile.Bytes, attachmentBytes);
        }

        foreach (var key in createdKeys)
        {
            var path = $"api/files/{Uri.EscapeDataString(key)}";
            using var deleteResponse = await client.DeleteAsync(path, cancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            using var repeatedDelete = await client.DeleteAsync(path, cancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, repeatedDelete.StatusCode);

            using var missingFile = await client.GetAsync(path, cancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, missingFile.StatusCode);
        }
    }

    [Theory]
    [InlineData("unsupported.txt", "not an allowed file type")]
    [InlineData("empty.png", "")]
    public async Task Upload_rejects_unsupported_or_empty_files(string fileName, string value)
    {
        using var client = fixture.CreateApiClient();
        await AssertUnsupportedOrEmptyUploadAsync(client, fileName, value, TestContext.Current.CancellationToken);
    }

    internal static async Task AssertUnsupportedOrEmptyUploadAsync(HttpClient client, string fileName, string value,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(value));
        form.Add(content, "file", fileName);

        using var response = await client.PostAsync("api/files/", form, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_rejects_files_over_the_size_limit()
    {
        using var client = fixture.CreateApiClient();
        await AssertOversizedUploadAsync(client, TestContext.Current.CancellationToken);
    }

    internal static async Task AssertOversizedUploadAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(new byte[MaxFileSize + 1]);
        content.Headers.ContentType = new("application/octet-stream");
        form.Add(content, "file", "too-large.png");

        using var response = await client.PostAsync("api/files/", form, cancellationToken);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge });
    }

    private static string GetFixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private sealed record UploadSample(string FileName, string ContentType, string FixtureName);

    private sealed record UploadedSample(UploadSample Sample, byte[] Bytes, StoredFileResponse StoredFile);

    private sealed record StoredFileResponse(string Key, string FileName, string ContentType, long Size);
}
