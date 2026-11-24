namespace OpenERP.Api;

/// <summary>附件标识、原始文件名及下载元数据。</summary>
public sealed record StoredFile(string Key, string FileName, string ContentType, long Size)
{
    public const long MaxFileSize = 20 * 1024 * 1024;

    internal static StoredFile Create(string name, string contentType, long size) =>
        new($"{Guid.NewGuid():N}_{name}", name, contentType, size);

    public static string? GetContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
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
        && GetContentType(key) is not null;
}

public sealed class FileDownload(StoredFile file, Stream content, IDisposable? owner = null) : IDisposable
{
    public StoredFile File { get; } = file;
    public Stream Content { get; } = content;
    public void Dispose() => (owner ?? Content).Dispose();
}

public interface IFileStore
{
    Task InitializeAsync(CancellationToken ct);
    Task<IReadOnlyList<StoredFile>> ListAsync(CancellationToken ct);
    Task<StoredFile> UploadAsync(Stream content, string name, string contentType, long size, CancellationToken ct);
    Task<FileDownload> OpenAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}
