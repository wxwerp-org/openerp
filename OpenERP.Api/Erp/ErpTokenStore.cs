using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpResult;
using OpenERP.Api.Persistence;

namespace OpenERP.Api.Erp;

public interface IErpTokenStore
{
    Task<OpResult<ErpConfiguration>> GetConfigurationAsync(CancellationToken cancellationToken);

    Task<OpResult<ErpAccessToken>> GetAsync(CancellationToken cancellationToken);

    Task<OpResult.OpResult> SaveAsync(string accessToken, CancellationToken cancellationToken);
}

internal sealed class ErpTokenStore(AppDbContext db, IDataProtectionProvider protection) : IErpTokenStore
{
    private readonly IDataProtector _protector = protection.CreateProtector("OpenERP.Erp.AccessToken");

    public async Task<OpResult<ErpConfiguration>> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var configured = await db.ErpSettings.AnyAsync(cancellationToken);
            return new ErpConfiguration(configured, ErpConfiguration.UpstreamBaseUrl);
        }
        catch (Exception exception) when (IsDatabaseReadFailure(exception))
        {
            return OpResults.Err("读取私有 ERP 配置失败，请稍后重试。");
        }
    }

    public async Task<OpResult<ErpAccessToken>> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var value = await db.ErpSettings
                .Select(settings => settings.ProtectedAccessToken)
                .SingleOrDefaultAsync(cancellationToken);
            return value is null
                ? OpResults.Err("尚未配置私有 ERP AccessToken。")
                : new ErpAccessToken(_protector.Unprotect(value));
        }
        catch (Exception exception) when (exception is CryptographicException || IsDatabaseReadFailure(exception))
        {
            return OpResults.Err("读取私有 ERP AccessToken 失败，请稍后重试。");
        }
    }

    // Npgsql 未启用重试时会包装瞬时连接错误，重试耗尽时则由 EF Core 包装。
    private static bool IsDatabaseReadFailure(Exception exception) =>
        exception is DbException or RetryLimitExceededException
            or InvalidOperationException { InnerException: DbException or TimeoutException };

    public async Task<OpResult.OpResult> SaveAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            var protectedToken = _protector.Protect(accessToken);
            await SingletonSettings.SaveAsync(db, new ErpSettings { ProtectedAccessToken = protectedToken },
                ct => db.ErpSettings.IgnoreQueryFilters().ExecuteUpdateAsync(setters => setters
                    .SetProperty(settings => settings.ProtectedAccessToken, protectedToken)
                    .SetProperty(settings => settings.DeletedAt, (DateTimeOffset?)null), ct), cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or CryptographicException
            || IsDatabaseReadFailure(exception))
        {
            return OpResults.Err("保存 ERP AccessToken 失败，请重试。");
        }

        return OpResults.Ok();
    }
}
