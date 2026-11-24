using System.Net;
using System.Data.Common;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OpenERP.Api;

public static class S3FileStorage
{
    public static void AddS3FileStorage(this WebApplicationBuilder builder)
    {
        var connection = new DbConnectionStringBuilder
        {
            ConnectionString = builder.Configuration.GetConnectionString("seaweedfs")
                ?? throw new InvalidOperationException("The seaweedfs connection string is required.")
        };
        var directory = builder.Configuration["OPENERP_S3_CREDENTIALS_DIRECTORY"];
        if (directory is not null)
        {
            builder.Configuration.AddKeyPerFile(Path.Combine(directory, "current", "client"), optional: false);
            connection["AccessKey"] = builder.Configuration["S3:AccessKey"]
                ?? throw new InvalidOperationException("S3 Access Key 凭据文件缺失。");
            connection["SecretKey"] = builder.Configuration["S3:SecretKey"]
                ?? throw new InvalidOperationException("S3 Secret Key 凭据文件缺失。");
        }
        builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            (string)connection["AccessKey"], (string)connection["SecretKey"],
            new AmazonS3Config
            {
                ServiceURL = (string)connection["Endpoint"],
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1"
            }));
        builder.Services.AddSingleton<IFileStore, S3FileBucket>();
        builder.Services.AddHealthChecks().AddCheck<FileStorageHealthCheck>("seaweedfs");
    }
}

internal sealed class FileStorageHealthCheck(IAmazonS3 s3) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await s3.ListBucketsAsync(new ListBucketsRequest(), cancellationToken);
        return HealthCheckResult.Healthy();
    }
}

internal sealed class S3FileBucket(IAmazonS3 s3) : IFileStore
{
    private const string BucketName = "openerp-files";

    public async Task InitializeAsync(CancellationToken ct)
    {
        var buckets = await SendAsync(s3.ListBucketsAsync(new ListBucketsRequest(), ct));
        if (buckets.Buckets is null || buckets.Buckets.All(bucket => bucket.BucketName != BucketName))
        {
            await SendAsync(s3.PutBucketAsync(new PutBucketRequest { BucketName = BucketName }, ct));
        }
    }

    public async Task<IReadOnlyList<StoredFile>> ListAsync(CancellationToken ct)
    {
        var files = new List<StoredFile>();
        string? continuationToken = null;
        do
        {
            var page = await SendAsync(s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = BucketName,
                ContinuationToken = continuationToken
            }, ct));
            foreach (var item in page.S3Objects ?? [])
            {
                if (StoredFile.IsValidKey(item.Key))
                    files.Add(DescribeObject(item.Key, item.Size ?? 0));
            }
            continuationToken = page.IsTruncated == true ? page.NextContinuationToken : null;
        } while (continuationToken is not null);
        return files;
    }

    public async Task<StoredFile> UploadAsync(Stream content, string name, string contentType, long size, CancellationToken ct)
    {
        var file = StoredFile.Create(name, contentType, size);
        await SendAsync(s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = file.Key,
            ContentType = file.ContentType,
            InputStream = content,
            AutoCloseStream = false
        }, ct));
        return file;
    }

    public async Task<FileDownload> OpenAsync(string key, CancellationToken ct)
    {
        if (!StoredFile.IsValidKey(key)) throw new FileNotFoundException();
        var response = await SendAsync(s3.GetObjectAsync(new GetObjectRequest { BucketName = BucketName, Key = key }, ct));
        return new FileDownload(DescribeObject(key, response.ContentLength), response.ResponseStream, response);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        if (!StoredFile.IsValidKey(key)) return Task.CompletedTask;
        return SendAsync(s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = BucketName, Key = key }, ct));
    }

    private static StoredFile DescribeObject(string key, long size) =>
        new(key, key[33..], StoredFile.GetContentType(key)!, size);

    private static async Task<T> SendAsync<T>(Task<T> request)
    {
        try { return await request; }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("存储对象不存在。", e);
        }
        catch (AmazonS3Exception e)
        {
            throw new IOException("无法访问 S3 文件存储。", e);
        }
    }
}
