using System.Diagnostics;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OpenERP.AppHost;

internal static class SingleContainerArtifacts
{
    private const string Platform = "linux/amd64";

    internal static Task BuildBaseImageAsync(string image, string staging, string deploy, CancellationToken cancellationToken)
    {
        var context = Directory.CreateDirectory(Path.Combine(staging, "base-context"));
        return RunDockerAsync(cancellationToken, "build", "--platform", Platform, "--tag", image,
            "--file", Path.Combine(deploy, "base.Dockerfile"), context.FullName);
    }

    internal static async Task RemoveBaseImageAsync(ILogger logger, string image)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await RunDockerAsync(cleanup.Token, "image", "rm", "--no-prune", image);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
        {
            logger.LogWarning(exception, "移除组装用基础镜像 {Image} 失败。", image);
        }
    }

    public static async Task StageAsync(PipelineStepContext context, string staging, string deploy, string apiImage)
    {
        var web = context.Model.Resources.Single(resource => resource.Name == "web");
        if (!web.TryGetContainerImageName(out var webImage))
            throw new InvalidOperationException("缺少本次 Aspire 构建生成的 Web 镜像名。");

        Directory.CreateDirectory(Path.Combine(staging, "api"));
        Directory.CreateDirectory(Path.Combine(staging, "web"));
        File.Copy(Path.Combine(deploy, "Dockerfile"), Path.Combine(staging, "Dockerfile"));
        File.Copy(Path.Combine(deploy, "entrypoint.sh"), Path.Combine(staging, "entrypoint.sh"));

        var output = context.Services.GetRequiredService<IPipelineOutputService>().GetOutputDirectory();
        File.Copy(Path.Combine(output, "efmigrations", "migrations"), Path.Combine(staging, "efbundle"));

        await CopyFromImageAsync(context.Logger, apiImage, context.CancellationToken,
            ("/app/.", Path.Combine(staging, "api")));
        await CopyFromImageAsync(context.Logger, webImage, context.CancellationToken,
            ("/srv/.", Path.Combine(staging, "web")),
            ("/etc/caddy/Caddyfile", Path.Combine(staging, "Caddyfile")));
    }

    private static async Task CopyFromImageAsync(
        ILogger logger, string image, CancellationToken cancellationToken, params (string Source, string Destination)[] files)
    {
        var container = $"openerp-stage-{Guid.NewGuid():N}";
        await RunDockerAsync(cancellationToken, "create", "--platform", Platform, "--pull", "never", "--name", container, image);
        try
        {
            foreach (var (source, destination) in files)
                await RunDockerAsync(cancellationToken, "cp", $"{container}:{source}", destination);
        }
        finally
        {
            // Caddy 声明了匿名卷，取件后与临时容器一起移除。
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await RunDockerAsync(cleanup.Token, "rm", "--force", "--volumes", container);
            }
            catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
            {
                logger.LogWarning(exception, "移除组装用容器 {Container} 失败。", container);
            }
        }
    }

    private static async Task RunDockerAsync(CancellationToken cancellationToken, params string[] arguments)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo("docker") { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 Docker。");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await output;
            var errorText = await error;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"docker {arguments[0]} 失败 ({process.ExitCode})。{errorText}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill();
            throw;
        }
    }
}
