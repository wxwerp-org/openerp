using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class InfrastructureImageTests
{
    [Theory]
    [InlineData("postgres", "docker.io/library/postgres:18-alpine")]
    [InlineData("redis", "docker.io/library/redis:8-alpine")]
    [InlineData("seaweedfs", "docker.io/chrislusf/seaweedfs:4.53")]
    public async Task Infrastructure_uses_the_expected_image(string resourceName, string expectedImage)
    {
        // Given 以仓库实际 AppHost 配置构建资源模型。
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.OpenERP_AppHost>(
            ["NO_PROXY=localhost,127.0.0.1,::1"], TestContext.Current.CancellationToken);

        // When 解析运行时资源镜像，Then 使用该组件约定的镜像名和版本标签。
        var resource = builder.Resources.Single(resource => resource.Name == resourceName);
        Assert.True(resource.TryGetContainerImageName(out var imageName));
        Assert.Equal(expectedImage, imageName);
    }

    [Fact]
    public async Task Postgres_uses_a_separate_persistent_volume_for_Alpine()
    {
        // Given 开发环境已经可能存在 Debian 创建的默认 PostgreSQL 卷。
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.OpenERP_AppHost>(
            ["NO_PROXY=localhost,127.0.0.1,::1"], TestContext.Current.CancellationToken);

        // When 解析 PostgreSQL 数据卷，Then Alpine 使用独立卷及 PostgreSQL 18 挂载点。
        var postgres = builder.Resources.Single(resource => resource.Name == "postgres");
        var volume = Assert.Single(postgres.Annotations.OfType<ContainerMountAnnotation>(),
            mount => mount.Type == ContainerMountType.Volume);
        Assert.EndsWith("-postgres-alpine-data", volume.Source);
        Assert.Equal("/var/lib/postgresql", volume.Target);
    }
}
