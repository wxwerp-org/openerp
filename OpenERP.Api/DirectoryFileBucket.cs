using System.Text.Json;

namespace OpenERP.Api;

/// <summary>每个已发布对象独占一个 GUID 目录，显示名称只保存在清单中。</summary>
internal sealed class DirectoryFileBucket(DirectoryInfo root, ILogger<DirectoryFileBucket>? logger = null) : IFileStore
{
    private readonly string bucket = Path.Combine(root.FullName, "openerp-files");

    public Task InitializeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(bucket);
        else
            Directory.CreateDirectory(bucket, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        CheckRegularPath(bucket);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<StoredFile>> ListAsync(CancellationToken ct)
    {
        CheckRegularPath(bucket);
        var files = new List<StoredFile>();
        foreach (var directory in Directory.EnumerateDirectories(bucket))
        {
            ct.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
            try
            {
                var file = await ReadManifestAsync(directory, ct);
                if (file is not null) files.Add(file);
            }
            catch (InvalidDataException exception)
            {
                logger?.LogWarning(exception, "跳过已损坏的本机附件 {ObjectId}", Path.GetFileName(directory));
            }
        }
        return files;
    }

    public async Task<StoredFile> UploadAsync(Stream content, string name, string contentType, long size, CancellationToken ct)
    {
        var file = StoredFile.Create(name, contentType, size);
        CheckRegularPath(bucket);
        var id = file.Key[..32];
        var pending = Path.Combine(bucket, ".upload-" + id);
        Directory.CreateDirectory(pending);
        try
        {
            await using (var output = File.Create(Path.Combine(pending, "content")))
            {
                await content.CopyToAsync(output, ct);
                if (output.Length != file.Size) throw new IOException("上传内容不完整。");
                output.Flush(flushToDisk: true);
            }
            await using (var manifest = File.Create(Path.Combine(pending, "metadata.json")))
            {
                await JsonSerializer.SerializeAsync(manifest, file, cancellationToken: ct);
                manifest.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            Directory.Move(pending, Path.Combine(bucket, id));
            return file;
        }
        finally
        {
            if (Directory.Exists(pending)) Directory.Delete(pending, recursive: true);
        }
    }

    public async Task<FileDownload> OpenAsync(string key, CancellationToken ct)
    {
        var (directory, file) = await ResolveAsync(key, ct) ?? throw new FileNotFoundException();
        var content = Path.Combine(directory, "content");
        CheckRegularPath(content);
        var input = new FileStream(content, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return new FileDownload(file, input);
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        if (await ResolveAsync(key, ct) is not { } found) return;
        var trash = Path.Combine(bucket, ".delete-" + Guid.NewGuid().ToString("N"));
        try { Directory.Move(found.Directory, trash); }
        catch (DirectoryNotFoundException) { return; }
        Directory.Delete(trash, recursive: true);
    }

    private async Task<(string Directory, StoredFile File)?> ResolveAsync(string key, CancellationToken ct)
    {
        if (!StoredFile.IsValidKey(key)) return null;
        CheckRegularPath(bucket);
        var directory = Path.Combine(bucket, key[..32].ToLowerInvariant());
        var file = await ReadManifestAsync(directory, ct);
        return file?.Key == key ? (directory, file) : null;
    }

    private static async Task<StoredFile?> ReadManifestAsync(string directory, CancellationToken ct)
    {
        try
        {
            CheckRegularPath(directory);
            var path = Path.Combine(directory, "metadata.json");
            CheckRegularPath(path);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var file = await JsonSerializer.DeserializeAsync<StoredFile>(input, cancellationToken: ct);
            if (file is null || file.Key != Path.GetFileName(directory) + "_" + file.FileName
                || !StoredFile.IsValidKey(file.Key) || StoredFile.GetContentType(file.Key) != file.ContentType)
                throw new InvalidDataException("附件清单不匹配。");
            var content = Path.Combine(directory, "content");
            CheckRegularPath(content);
            if (new FileInfo(content).Length != file.Size)
                throw new InvalidDataException("附件内容长度与清单不匹配。");
            return file;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (JsonException exception) { throw new InvalidDataException("附件清单无法读取。", exception); }
    }

    private static void CheckRegularPath(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("文件存储目录中出现了链接。");
    }
}
