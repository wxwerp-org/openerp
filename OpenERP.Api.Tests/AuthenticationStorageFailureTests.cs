using System.Data.Common;
using OpenERP.Api.Authentication;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class AuthenticationStorageFailureTests
{
    [Fact]
    public void Authentication_recognizes_a_wrapped_database_error_from_another_provider()
    {
        // Given 数据库驱动的异常被外层重试策略包装。
        var error = new InvalidOperationException("重试已耗尽。", new TestDatabaseException());

        // When 认证检查失败原因；Then 识别为存储故障，供中间件返回 503。
        Assert.True(AuthenticationEndpoints.IsDatabaseFailure(error));
        Assert.False(AuthenticationEndpoints.IsDatabaseFailure(new InvalidOperationException("其他错误。")));
    }

    private sealed class TestDatabaseException : DbException;
}
