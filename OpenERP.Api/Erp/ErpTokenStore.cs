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

    /// <param name="baseUrl">独立部署的上游地址；为空表示公共版。</param>
    Task<OpResult.OpResult> SaveAsync(string accessToken, string? baseUrl, CancellationToken cancellationToken);

    /// <summary>撤销配置：彻底删除已保存的 AccessToken 与部署地址。</summary>
    Task<OpResult.OpResult> DeleteAsync(CancellationToken cancellationToken);
}

internal sealed class ErpTokenStore(AppDbContext db, IDataProtectionProvider protection) : IErpTokenStore
{
    private readonly IDataProtector _protector = protection.CreateProtector("OpenERP.Erp.AccessToken");

    public async Task<OpResult<ErpConfiguration>> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var row = await db.ErpSettings
                .Select(settings => new { settings.BaseUrl })
                .SingleOrDefaultAsync(cancellationToken);
            var independent = !string.IsNullOrWhiteSpace(row?.BaseUrl);
            return new ErpConfiguration(
                row is not null,
                independent ? row!.BaseUrl!.TrimEnd('/') : ErpConfiguration.UpstreamBaseUrl,
                independent);
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
            var row = await db.ErpSettings
                .Select(settings => new { settings.ProtectedAccessToken, settings.BaseUrl })
                .SingleOrDefaultAsync(cancellationToken);
            return row is null
                ? OpResults.Err("尚未配置私有 ERP AccessToken。")
                : new ErpAccessToken(_protector.Unprotect(row.ProtectedAccessToken), row.BaseUrl);
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

    public async Task<OpResult.OpResult> DeleteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.ErpSettings.IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception exception) when (IsDatabaseReadFailure(exception))
        {
            return OpResults.Err("撤销 ERP 配置失败，请重试。");
        }

        return OpResults.Ok();
    }

    public async Task<OpResult.OpResult> SaveAsync(string accessToken, string? baseUrl, CancellationToken cancellationToken)
    {
        try
        {
            var protectedToken = _protector.Protect(accessToken);
            await SingletonSettings.SaveAsync(db,
                new ErpSettings { ProtectedAccessToken = protectedToken, BaseUrl = baseUrl },
                ct => db.ErpSettings.IgnoreQueryFilters().ExecuteUpdateAsync(setters => setters
                    .SetProperty(settings => settings.ProtectedAccessToken, protectedToken)
                    .SetProperty(settings => settings.BaseUrl, baseUrl)
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
