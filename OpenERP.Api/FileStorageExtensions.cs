using System.Data.Common;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenERP.Api.ObjectStorage;

namespace OpenERP.Api;

public static class FileStorageExtensions
{
    public static void AddFileStorage(this WebApplicationBuilder builder)
    {
        var provider = builder.Configuration["ObjectStorage:Provider"] ?? "S3";
        if (provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            var rootPath = builder.Configuration["ObjectStorage:RootPath"];
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new InvalidOperationException("本机对象存储需要配置 ObjectStorage:RootPath。");
            builder.Services.AddSingleton<IObjectStorage>(new LocalObjectStorage(
                Path.GetFullPath(rootPath, builder.Environment.ContentRootPath)));
        }
        else if (provider.Equals("S3", StringComparison.OrdinalIgnoreCase))
        {
            builder.AddS3FileStorage();
        }
        else
        {
            throw new InvalidOperationException("ObjectStorage:Provider 必须为 Local 或 S3。");
        }

        builder.Services.AddSingleton<FileStore>();
    }

    public static Task InitializeFileStorageAsync(this IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IObjectStorage>().InitializeAsync(ct);

    private static void AddS3FileStorage(this WebApplicationBuilder builder)
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
        builder.Services.AddSingleton<IObjectStorage, S3ObjectStorage>();
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
