using System.Reflection;

namespace OpenERP.Api.About;

/// <summary>只读构建版本信息；CI 在 API 构建步骤注入。</summary>
public static class AboutEndpoints
{
    public static void MapAbout(this WebApplication app) =>
        app.MapGet("/api/about", () => TypedResults.Ok(BuildInfo.Read(typeof(AboutEndpoints).Assembly)))
            .AllowAnonymous()
            .WithSummary("返回只读构建版本信息");
}

/// <summary>读取程序集上的构建版本字段。</summary>
internal static class BuildInfo
{
    internal const string CommitMetadataKey = "OpenERP.Commit";
    internal const string BuildTimeMetadataKey = "OpenERP.BuildTime";
    internal const string FallbackVersion = "0.1.0-dev";

    internal static AboutInfo Read(Assembly assembly)
    {
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);
        return FromValues(
            informationalVersion,
            metadata.GetValueOrDefault(CommitMetadataKey),
            metadata.GetValueOrDefault(BuildTimeMetadataKey));
    }

    /// <summary>SDK 会在 InformationalVersion 后追加 +&lt;sha&gt;，只保留显式设置的版本。</summary>
    internal static AboutInfo FromValues(string? informationalVersion, string? commit, string? buildTime)
    {
        var version = informationalVersion?.Trim();
        if (version is { Length: > 0 })
        {
            var metadataSeparator = version.IndexOf('+', StringComparison.Ordinal);
            if (metadataSeparator >= 0)
                version = version[..metadataSeparator].TrimEnd();
        }

        return new AboutInfo(
            string.IsNullOrEmpty(version) ? FallbackVersion : version,
            commit?.Trim() ?? "",
            buildTime?.Trim() ?? "");
    }
}

/// <summary>关于接口的响应；只包含版本、提交与构建时间。</summary>
public sealed record AboutInfo(string Version, string Commit, string BuildTime);
