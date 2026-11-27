using System.Security.Cryptography;
using System.Text;

namespace OpenERP.Api.ObjectStorage;

internal sealed class LocalObjectStorage(string rootPath) : IObjectStorage
{
    public Task InitializeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(rootPath);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredObject>> ListAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var objects = new List<StoredObject>();
            foreach (var path in Directory.EnumerateFiles(rootPath, "*.object"))
            {
                ct.ThrowIfCancellationRequested();
                using var content = TryOpen(path);
                if (content is not null)
                {
                    var key = ReadKey(content);
                    objects.Add(new(key, content.Length - content.Position));
                }
            }
            objects.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
            return Task.FromResult<IReadOnlyList<StoredObject>>(objects);
        }
        catch (Exception exception) when (IsStorageError(exception))
        {
            throw new ObjectStorageException(exception);
        }
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        var temporaryPath = Path.Combine(rootPath, $".{Guid.NewGuid():N}.tmp");
        var temporaryCreated = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            await using (var temporary = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                temporaryCreated = true;
                // 对象名和内容一并提交，长文件名也能保持原来的内容和拼写。
                using var header = new BinaryWriter(temporary, Encoding.UTF8, leaveOpen: true);
                header.Write(key);
                await content.CopyToAsync(temporary, ct);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporaryPath, GetPath(key), overwrite: true);
            temporaryCreated = false;
        }
        catch (Exception exception) when (IsStorageError(exception))
        {
            throw new ObjectStorageException(exception);
        }
        finally
        {
            if (temporaryCreated)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (IsStorageError(exception))
                {
                    // 目录失去访问权限时保留原错误，列表不会读取临时文件。
                }
            }
        }
    }

    public Task<OpenedObject?> OpenAsync(string key, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var content = TryOpen(GetPath(key));
            if (content is null)
                return Task.FromResult<OpenedObject?>(null);

            try
            {
                if (!string.Equals(ReadKey(content), key, StringComparison.Ordinal))
                    throw new InvalidDataException("本地对象的键与文件内容不一致。");
                return Task.FromResult<OpenedObject?>(new(content, content.Length - content.Position, content));
            }
            catch
            {
                content.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (IsStorageError(exception))
        {
            throw new ObjectStorageException(exception);
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            File.Delete(GetPath(key));
            return Task.CompletedTask;
        }
        catch (Exception exception) when (IsStorageError(exception))
        {
            throw new ObjectStorageException(exception);
        }
    }

    private string GetPath(string key) =>
        Path.Combine(rootPath, $"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))}.object");

    private static string ReadKey(Stream content)
    {
        using var header = new BinaryReader(content, Encoding.UTF8, leaveOpen: true);
        return header.ReadString();
    }

    private static FileStream? TryOpen(string path)
    {
        try
        {
            return new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static bool IsStorageError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;
}
