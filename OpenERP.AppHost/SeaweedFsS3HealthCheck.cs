using Amazon.S3;
using Amazon.S3.Model;
using Aspire.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OpenERP.AppHost;

internal sealed class SeaweedFsS3HealthCheck(SeaweedFSContainerResource resource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var s3 = new AmazonS3Client(
            await resource.AccessKey.GetValueAsync(cancellationToken),
            await resource.SecretKey.GetValueAsync(cancellationToken),
            new AmazonS3Config
            {
                ServiceURL = await resource.PrimaryEndpoint.GetValueAsync(cancellationToken),
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
                // Aspire 会周期性执行探针，无需在单次检查中再次重试。
                MaxErrorRetry = 0
            });

        await s3.ListBucketsAsync(new ListBucketsRequest(), cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
