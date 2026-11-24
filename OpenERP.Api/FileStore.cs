using Amazon.S3;
using Amazon.S3.Model;

namespace OpenERP.Api;

/// <summary>应用存储桶中的文件。</summary>
public sealed record StoredFile(string Key, string FileName, string ContentType, long Size);

public sealed class FileStore
{
    public const string Bucket = "openerp-files";
    public const long MaxFileSize = 20 * 1024 * 1024;

    private readonly IFileBucket bucket;

    public FileStore(IAmazonS3 s3) => bucket = new S3FileBucket(s3);

    public FileStore(DirectoryInfo root, ILogger<FileStore>? logger = null) =>
        bucket = new DirectoryFileBucket(root, logger);

    public Task EnsureBucketAsync(CancellationToken ct) => bucket.EnsureBucketAsync(ct);
    public Task<IReadOnlyList<StoredFile>> ListAsync(CancellationToken ct) => bucket.ListAsync(ct);
    public Task<StoredFile> UploadAsync(IFormFile file, string fileName, string contentType, CancellationToken ct) =>
        bucket.UploadAsync(file, fileName, contentType, ct);
    public Task<GetObjectResponse> OpenAsync(string key, CancellationToken ct) => bucket.OpenAsync(key, ct);
    public Task DeleteAsync(string key, CancellationToken ct) => bucket.DeleteAsync(key, ct);

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
}

internal interface IFileBucket
{
    Task EnsureBucketAsync(CancellationToken ct);
    Task<IReadOnlyList<StoredFile>> ListAsync(CancellationToken ct);
    Task<StoredFile> UploadAsync(IFormFile file, string fileName, string contentType, CancellationToken ct);
    Task<GetObjectResponse> OpenAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

internal sealed class S3FileBucket(IAmazonS3 s3) : IFileBucket
{
    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        var buckets = await s3.ListBucketsAsync(new ListBucketsRequest(), ct);
        if (buckets.Buckets is null || buckets.Buckets.All(bucket => bucket.BucketName != FileStore.Bucket))
        {
            await s3.PutBucketAsync(new PutBucketRequest { BucketName = FileStore.Bucket }, ct);
        }
    }

    public async Task<IReadOnlyList<StoredFile>> ListAsync(CancellationToken ct)
    {
        var files = new List<StoredFile>();
        string? continuationToken = null;
        do
        {
            var page = await s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = FileStore.Bucket,
                ContinuationToken = continuationToken
            }, ct);
            foreach (var item in page.S3Objects ?? [])
            {
                if (FileStore.IsValidKey(item.Key))
                    files.Add(new(item.Key, item.Key[33..], FileStore.ContentType(item.Key)!, item.Size ?? 0));
            }
            continuationToken = page.IsTruncated == true ? page.NextContinuationToken : null;
        } while (continuationToken is not null);
        return files;
    }

    public async Task<StoredFile> UploadAsync(IFormFile file, string fileName, string contentType, CancellationToken ct)
    {
        var key = $"{Guid.NewGuid():N}_{fileName}";
        await using var input = file.OpenReadStream();
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = FileStore.Bucket,
            Key = key,
            InputStream = input,
            ContentType = contentType
        }, ct);
        return new(key, fileName, contentType, file.Length);
    }

    public Task<GetObjectResponse> OpenAsync(string key, CancellationToken ct) =>
        s3.GetObjectAsync(new GetObjectRequest { BucketName = FileStore.Bucket, Key = key }, ct);

    // S3 delete is deliberately idempotent, including when the object is already absent.
    public Task DeleteAsync(string key, CancellationToken ct) =>
        s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = FileStore.Bucket, Key = key }, ct);
}
