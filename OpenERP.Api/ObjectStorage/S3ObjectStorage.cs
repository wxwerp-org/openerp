using System.Net;
using Amazon.S3;
using Amazon.S3.Model;

namespace OpenERP.Api.ObjectStorage;

internal sealed class S3ObjectStorage(IAmazonS3 s3) : IObjectStorage
{
    private const string Bucket = "openerp-files";

    public async Task InitializeAsync(CancellationToken ct)
    {
        var buckets = await s3.ListBucketsAsync(new ListBucketsRequest(), ct);
        if (buckets.Buckets is null || buckets.Buckets.All(bucket => bucket.BucketName != Bucket))
        {
            await s3.PutBucketAsync(new PutBucketRequest { BucketName = Bucket }, ct);
        }
    }

    public async Task<IReadOnlyList<StoredObject>> ListAsync(CancellationToken ct)
    {
        var objects = new List<StoredObject>();
        string? continuationToken = null;
        do
        {
            var page = await ExecuteAsync(() => s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = Bucket,
                ContinuationToken = continuationToken
            }, ct));
            foreach (var item in page.S3Objects ?? [])
                objects.Add(new(item.Key, item.Size ?? 0));
            continuationToken = page.IsTruncated == true ? page.NextContinuationToken : null;
        } while (continuationToken is not null);
        return objects;
    }

    public Task PutAsync(string key, Stream content, string contentType, CancellationToken ct) =>
        ExecuteAsync(() => s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Bucket,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = contentType
        }, ct));

    public async Task<OpenedObject?> OpenAsync(string key, CancellationToken ct)
    {
        try
        {
            var response = await ExecuteAsync(() =>
                s3.GetObjectAsync(new GetObjectRequest { BucketName = Bucket, Key = key }, ct));
            return new(response.ResponseStream, response.ContentLength, response);
        }
        catch (ObjectStorageException exception) when (exception.NotFound)
        {
            return null;
        }
    }

    // S3 在对象已不存在时也允许删除。保留服务返回的其他错误。
    public Task DeleteAsync(string key, CancellationToken ct) =>
        ExecuteAsync(() => s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = Bucket, Key = key }, ct));

    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (AmazonS3Exception exception)
        {
            throw new ObjectStorageException(exception, exception.StatusCode == HttpStatusCode.NotFound);
        }
        catch (HttpRequestException exception)
        {
            throw new ObjectStorageException(exception);
        }
    }
}
