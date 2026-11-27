namespace OpenERP.Api.ObjectStorage;

internal interface IObjectStorage
{
    Task InitializeAsync(CancellationToken ct);
    Task<IReadOnlyList<StoredObject>> ListAsync(CancellationToken ct);
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct);
    Task<OpenedObject?> OpenAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

internal sealed record StoredObject(string Key, long Size);

internal sealed class OpenedObject(Stream content, long size, IDisposable owner) : IDisposable
{
    public Stream Content { get; } = content;
    public long Size { get; } = size;

    public void Dispose() => owner.Dispose();
}

internal sealed class ObjectStorageException(Exception cause, bool notFound = false)
    : Exception("对象存储操作失败。", cause)
{
    public bool NotFound { get; } = notFound;
}
