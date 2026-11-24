using System.Text;
using Microsoft.AspNetCore.Http;
using OpenERP.Api;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class DirectoryFileStoreTests
{
    // Given 私有目录可用，When 同名文件上传两次再打开实例，Then 独立保留字节、名称、类型与长度。
    // Given 用户提供别名或越界路径，When 下载或删除，Then 不允许访问或影响原文件。
    // Given 上传流失败，When 重新列举，Then 不显示部分文件。
    [Fact]
    public async Task FilesSurviveReopenWithTheirExactKeyAndContent()
    {
        var root = Directory.CreateTempSubdirectory();
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var store = Create(root);
            await store.EnsureBucketAsync(ct);
            const string name = "清单 ：🧾.xlsx";
            var first = await Upload(store, name, "one", ct);
            var second = await Upload(store, name, "different", ct);
            Assert.NotEqual(first.Key, second.Key);
            var reopened = Create(root);
            var listed = await reopened.ListAsync(ct);
            Assert.Equal(2, listed.Count);
            Assert.Contains(first, listed);
            Assert.Contains(second, listed);
            using (var opened = await reopened.OpenAsync(first.Key, ct))
            using (var reader = new StreamReader(opened.ResponseStream))
            {
                Assert.Equal(3, opened.ContentLength);
                Assert.Equal("one", await reader.ReadToEndAsync(ct));
            }
            foreach (var invalid in new[] { first.Key[..33] + "alias.xlsx", first.Key.ToUpperInvariant(), "../outside.xlsx" })
            {
                await Assert.ThrowsAsync<FileNotFoundException>(() => reopened.OpenAsync(invalid, ct));
                await reopened.DeleteAsync(invalid, ct);
            }
            await reopened.DeleteAsync(first.Key, ct);
            await reopened.DeleteAsync(first.Key, ct);
            Assert.Equal(second, Assert.Single(await reopened.ListAsync(ct)));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task InterruptedUploadDoesNotPublishPartialFile()
    {
        var root = Directory.CreateTempSubdirectory();
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var store = Create(root);
            await store.EnsureBucketAsync(ct);
            using var bytes = new MemoryStream([42]);
            var file = new FormFile(bytes, 0, 10, "file", "test.pdf");
            await Assert.ThrowsAnyAsync<IOException>(() => store.UploadAsync(file, "test.pdf", "application/pdf", ct));
            Assert.Empty(await store.ListAsync(ct));
        }
        finally { root.Delete(true); }
    }

    // Given 某个附件内容被截断，When 列举和读取，Then 仍能使用健康附件，不返回损坏字节。
    [Fact]
    public async Task DamagedContentDoesNotHideHealthyFilesOrServePartialBytes()
    {
        var root = Directory.CreateTempSubdirectory();
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var store = Create(root);
            await store.EnsureBucketAsync(ct);
            var healthy = await Upload(store, "good.pdf", "healthy", ct);
            var bad = await Upload(store, "bad.pdf", "original", ct);
            var content = Path.Combine(root.FullName, FileStore.Bucket, bad.Key[..32], "content");
            await File.WriteAllBytesAsync(content, [42], ct);
            Assert.Equal(healthy, Assert.Single(await store.ListAsync(ct)));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenAsync(bad.Key, ct));
        }
        finally { root.Delete(true); }
    }

    private static FileStore Create(DirectoryInfo root) =>
        new(root);

    private static async Task<StoredFile> Upload(FileStore store, string name, string text, CancellationToken ct)
    {
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return await store.UploadAsync(new FormFile(bytes, 0, bytes.Length, "file", name), name, FileStore.ContentType(name)!, ct);
    }
}
