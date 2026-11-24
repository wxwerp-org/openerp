using System.Text;
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
            var store = new DirectoryFileBucket(root);
            await store.InitializeAsync(ct);
            const string name = "清单 ：🧾.xlsx";
            var first = await Upload(store, name, "one", ct);
            var second = await Upload(store, name, "different", ct);
            Assert.NotEqual(first.Key, second.Key);
            var reopened = new DirectoryFileBucket(root);
            var listed = await reopened.ListAsync(ct);
            Assert.Equal(2, listed.Count);
            Assert.Contains(first, listed);
            Assert.Contains(second, listed);
            using (var opened = await reopened.OpenAsync(first.Key, ct))
            using (var reader = new StreamReader(opened.Content))
            {
                Assert.Equal(first, opened.File);
                Assert.Equal("one", await reader.ReadToEndAsync(ct));
            }
            // 在区分大小写的测试盘上也让大写别名命中相同清单，复现 Windows 目录查找行为。
            var directory = Path.Combine(root.FullName, "openerp-files", first.Key[..32]);
            var alias = Path.Combine(root.FullName, "openerp-files", first.Key[..32].ToUpperInvariant());
            if (!Directory.Exists(alias))
            {
                Directory.CreateDirectory(alias);
                foreach (var entry in Directory.GetFiles(directory))
                    File.Copy(entry, Path.Combine(alias, Path.GetFileName(entry)));
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
            var store = new DirectoryFileBucket(root);
            await store.InitializeAsync(ct);
            using var bytes = new MemoryStream([42]);
            await Assert.ThrowsAnyAsync<IOException>(() => store.UploadAsync(bytes, "test.pdf", "application/pdf", 10, ct));
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
            var store = new DirectoryFileBucket(root);
            await store.InitializeAsync(ct);
            var healthy = await Upload(store, "good.pdf", "healthy", ct);
            var bad = await Upload(store, "bad.pdf", "original", ct);
            var content = Path.Combine(root.FullName, "openerp-files", bad.Key[..32], "content");
            await File.WriteAllBytesAsync(content, [42], ct);
            Assert.Equal(healthy, Assert.Single(await store.ListAsync(ct)));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenAsync(bad.Key, ct));
        }
        finally { root.Delete(true); }
    }

    private static async Task<StoredFile> Upload(IFileStore store, string name, string text, CancellationToken ct)
    {
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return await store.UploadAsync(bytes, name, StoredFile.GetContentType(name)!, bytes.Length, ct);
    }
}
