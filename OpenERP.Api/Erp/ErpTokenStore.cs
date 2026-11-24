using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Persistence;
using OpResult;

namespace OpenERP.Api.Erp;

public interface IErpTokenStore
{
    Task<OpResult<ErpConfiguration>> GetConfigurationAsync(CancellationToken cancellationToken);

    Task<OpResult<ErpAccessToken>> GetAsync(CancellationToken cancellationToken);

    Task<OpResult.OpResult> SaveAsync(string accessToken, CancellationToken cancellationToken);
}

internal sealed class ErpSettings : Entity
{
    public required string ProtectedAccessToken { get; set; }
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

    // 读取令牌时，连接超时也可能被包装为 InvalidOperationException。
    private static bool IsDatabaseReadFailure(Exception exception) =>
        DatabaseErrors.IsUnavailable(exception) || exception is InvalidOperationException { InnerException: TimeoutException };

    public async Task<OpResult.OpResult> SaveAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            var protectedToken = _protector.Protect(accessToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ErpSettings" ("Id", "ProtectedAccessToken")
                VALUES ({Guid.CreateVersion7()}, {protectedToken})
                ON CONFLICT ("Singleton") DO UPDATE
                SET "ProtectedAccessToken" = EXCLUDED."ProtectedAccessToken", "DeletedAt" = NULL
                """, cancellationToken);
        }
        catch (Exception exception) when (exception is CryptographicException || DatabaseErrors.IsUnavailable(exception))
        {
            return OpResults.Err("保存 ERP AccessToken 失败，请重试。");
        }

        return OpResults.Ok();
    }
}
