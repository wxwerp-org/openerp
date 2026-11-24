using System.Diagnostics;

namespace OpenERP.Api.Persistence;

/// <summary>本机实例的数据位置和独占租约；数据库、附件和后台作业共用同一个目录。</summary>
internal sealed class NativeDataDirectory : IDisposable
{
    private readonly FileStream _lease;
    internal string Root { get; }

    private NativeDataDirectory(string root, FileStream lease) => (Root, _lease) = (root, lease);

    public void Dispose() => _lease.Dispose();

    internal static string? Resolve(IConfiguration configuration, bool published)
    {
        var root = configuration["Native:DataDirectory"];
        if (string.IsNullOrWhiteSpace(root))
        {
            if (!published)
                return null;
            var userDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrWhiteSpace(userDirectory))
                throw new InvalidOperationException("找不到当前用户的数据目录，请设置 Native:DataDirectory。");
            root = Path.Combine(userDirectory, "OpenERP", "Local");
        }

        if (!Path.IsPathFullyQualified(root))
            throw new InvalidOperationException("Native:DataDirectory 必须是绝对路径。");
        root = Path.GetFullPath(root);
        var relative = Path.GetRelativePath(AppContext.BaseDirectory, root);
        if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("数据目录必须放在程序目录之外，以免更新程序时覆盖数据。");
        return root;
    }

    internal static NativeDataDirectory AcquireLease(string dataDirectory, TimeSpan? waitTimeout = null)
    {
        var timeout = waitTimeout ?? TimeSpan.FromSeconds(20);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);

        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(dataDirectory);
        else
            Directory.CreateDirectory(dataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        return OpenLease(dataDirectory, timeout);
    }

    internal static NativeDataDirectory AcquireExistingLease(string dataDirectory)
    {
        if (!Directory.Exists(dataDirectory))
            throw new DirectoryNotFoundException("本机数据目录不存在。");
        return OpenLease(dataDirectory, TimeSpan.Zero);
    }

    private static NativeDataDirectory OpenLease(string dataDirectory, TimeSpan timeout)
    {
        var lockPath = Path.Combine(dataDirectory, ".openerp-native.lock");
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new NativeDataDirectory(dataDirectory,
                    File.Open(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException error) when (IsSharingViolation(error))
            {
                // Aspire 可能比旧 API 先退出；只等待占用锁的进程，不重试磁盘或权限错误。
                if (timer.Elapsed >= timeout)
                    throw new IOException("OpenERP 数据目录仍被另一实例使用。请先关闭正在运行的实例，再重试。", error);
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
            }
        }
    }

    private static bool IsSharingViolation(IOException error)
    {
        int code = error.HResult & 0xffff;
        // Windows ERROR_SHARING_VIOLATION / ERROR_LOCK_VIOLATION；Unix flock 的 EAGAIN。
        return OperatingSystem.IsWindows() ? code is 32 or 33 : code == (OperatingSystem.IsMacOS() ? 35 : 11);
    }
}
