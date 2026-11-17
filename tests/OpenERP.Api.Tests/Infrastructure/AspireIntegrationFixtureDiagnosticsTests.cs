using Xunit;

namespace OpenERP.Api.Tests;

[CollectionDefinition(nameof(AspireIntegrationFixtureDiagnosticsCollection), DisableParallelization = true)]
public sealed class AspireIntegrationFixtureDiagnosticsCollection;

[Collection(nameof(AspireIntegrationFixtureDiagnosticsCollection))]
public sealed class AspireIntegrationFixtureDiagnosticsTests
{
    [Fact]
    public async Task Writes_a_private_diagnostic_file_per_fixture_only_when_CI_results_directory_is_configured()
    {
        // Given 未配置结果目录时，本地 fixture 清理不创建诊断文件。
        const string resultsVariable = "OPENERP_CI_RESULTS_DIR";
        const string canaryVariable = "OPENERP_DIAGNOSTIC_ENV_CANARY";
        const string canaryValue = "must-not-appear-in-diagnostics";
        var previousResultsDirectory = Environment.GetEnvironmentVariable(resultsVariable);
        var previousCanary = Environment.GetEnvironmentVariable(canaryVariable);
        var resultsDirectory = Path.Combine(Path.GetTempPath(), $"openerp-fixture-diagnostics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(resultsDirectory);

        try
        {
            Environment.SetEnvironmentVariable(resultsVariable, null);
            await new AspireIntegrationFixture().DisposeAsync();
            Assert.Empty(Directory.EnumerateFiles(resultsDirectory));

            // When CI 提供结果目录，fixture 释放前保存独立诊断，并且不序列化环境或测试密码。
            Environment.SetEnvironmentVariable(resultsVariable, resultsDirectory);
            Environment.SetEnvironmentVariable(canaryVariable, canaryValue);
            var fixtures = new[] { new AspireIntegrationFixture(), new AspireIntegrationFixture() };
            foreach (var fixture in fixtures)
            {
                await fixture.DisposeAsync();
            }

            // Then 文件包含阶段、资源状态与控制台日志分区，但不包含环境变量或秘密值。
            var files = Directory.EnumerateFiles(resultsDirectory).ToArray();
            Assert.Equal(fixtures.Length, files.Length);
            Assert.Equal(fixtures.Length, files.Select(Path.GetFileName).Distinct(StringComparer.Ordinal).Count());
            var contents = new List<string>();
            foreach (var file in files)
            {
                contents.Add(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
            }

            var diagnostics = string.Join(Environment.NewLine, contents);
            Assert.Contains("阶段: 清理前", diagnostics, StringComparison.Ordinal);
            Assert.Contains("资源状态:", diagnostics, StringComparison.Ordinal);
            Assert.Contains("控制台日志:", diagnostics, StringComparison.Ordinal);
            Assert.DoesNotContain(canaryVariable, diagnostics, StringComparison.Ordinal);
            Assert.DoesNotContain(canaryValue, diagnostics, StringComparison.Ordinal);
            Assert.All(fixtures, fixture => Assert.DoesNotContain(fixture.AdminPassword.Trim(), diagnostics, StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable(resultsVariable, previousResultsDirectory);
            Environment.SetEnvironmentVariable(canaryVariable, previousCanary);
            Directory.Delete(resultsDirectory, recursive: true);
        }
    }
}
