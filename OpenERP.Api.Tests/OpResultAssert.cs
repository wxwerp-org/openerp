using OpResult;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

/// <summary>在断言里安全读取 <see cref="OpResult{T}"/> 的值或错误：先证明状态，再读取。</summary>
internal static class OpResultAssert
{
    public static T Ok<T>(OpResult<T> result) where T : notnull
    {
        if (result.IsErr)
            throw new XunitException($"期望成功，实际失败：{result.Error.Message}");
        return result.Value;
    }

    public static string Err<T>(OpResult<T> result) where T : notnull
    {
        if (!result.IsErr)
            throw new XunitException("期望失败，实际成功。");
        return result.Error.Message;
    }
}
