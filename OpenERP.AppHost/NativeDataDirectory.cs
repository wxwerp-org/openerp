using System.Security.Cryptography;
using System.Text;

namespace OpenERP.AppHost;

internal static class NativeDataDirectory
{
    public static string Resolve(string? configured, string appHostDirectory)
    {
        if (configured is not null)
        {
            if (!Path.IsPathFullyQualified(configured))
                throw new InvalidOperationException("OpenERP:DataDirectory 必须是绝对路径。");
            return Path.GetFullPath(configured);
        }

        var userDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(userDirectory))
            throw new InvalidOperationException("找不到当前用户的数据目录，请设置 OpenERP:DataDirectory。");

        // 两份源码可以独立试改，每次重开同一工作区仍能找回原库。
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appHostDirectory));
        var workspaceKey = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(workspaceKey));
        return Path.Combine(userDirectory, "OpenERP", "workspaces", Convert.ToHexStringLower(hash.AsSpan(0, 12)));
    }
}
