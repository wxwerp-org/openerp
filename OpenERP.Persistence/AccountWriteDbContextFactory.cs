using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace OpenERP.Api.Persistence;

internal sealed class AccountWriteDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    // 连接在提交时中断可能已有写入结果，避免自动重放整笔认证事务。
    private readonly DbContextOptions<AppDbContext> _writeOptions = new DbContextOptionsBuilder<AppDbContext>(options)
        .ReplaceService<IExecutionStrategyFactory, AccountWriteExecutionStrategyFactory>()
        .Options;

    public AppDbContext CreateDbContext() => new(_writeOptions);

    private sealed class AccountWriteExecutionStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new NonRetryingExecutionStrategy(dependencies);
    }
}
