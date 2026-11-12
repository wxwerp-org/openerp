using System.Data.Common;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OpenERP.Api;

public static class FileStorageExtensions
{
    public static void AddFileStorage(this WebApplicationBuilder builder)
    {
        var connection = new DbConnectionStringBuilder
        {
            ConnectionString = builder.Configuration.GetConnectionString("seaweedfs")
                ?? throw new InvalidOperationException("The seaweedfs connection string is required.")
        };
        builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            (string)connection["AccessKey"], (string)connection["SecretKey"],
            new AmazonS3Config
            {
                ServiceURL = (string)connection["Endpoint"],
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1"
            }));
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
