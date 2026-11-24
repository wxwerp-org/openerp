using OpenERP.Api.ObjectStorage;

namespace OpenERP.Api;

/// <summary>存放在应用对象存储中的文件。</summary>
public sealed record StoredFile(string Key, string FileName, string ContentType, long Size);

internal sealed class FileStore(IObjectStorage storage)
{
    public const long MaxFileSize = 20 * 1024 * 1024;

    public static string? ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".pdf" => "application/pdf",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => null
    };

    public static bool IsValidKey(string key) => key.Length > 33 && key[32] == '_'
        && Guid.TryParseExact(key[..32], "N", out _)
        && !key.Contains('/') && !key.Contains('\\') && !key.Any(char.IsControl)
        && ContentType(key) is not null;

    public async Task<IReadOnlyList<StoredFile>> ListAsync(CancellationToken ct)
    {
        var objects = await storage.ListAsync(ct);
        var files = new List<StoredFile>();
        foreach (var item in objects)
        {
            if (IsValidKey(item.Key))
                files.Add(Describe(item.Key, item.Size));
        }
        return files;
    }

    public async Task<StoredFile> UploadAsync(Stream input, string fileName, string contentType, long size, CancellationToken ct)
    {
        var key = $"{Guid.NewGuid():N}_{fileName}";
        await storage.PutAsync(key, input, contentType, ct);
        return new(key, fileName, contentType, size);
    }

    public async Task<(StoredFile Metadata, OpenedObject Object)?> OpenAsync(string key, CancellationToken ct)
    {
        if (!IsValidKey(key))
            return null;
        var opened = await storage.OpenAsync(key, ct);
        return opened is null ? null : (Describe(key, opened.Size), opened);
    }

    public Task DeleteAsync(string key, CancellationToken ct) => storage.DeleteAsync(key, ct);

    private static StoredFile Describe(string key, long size) => new(key, key[33..], ContentType(key)!, size);
}
