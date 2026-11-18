using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OpResult;

namespace OpenERP.Api.Erp;

public interface IErpTokenStore
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken);

    Task<string> GetAsync(CancellationToken cancellationToken);

    Task<OpResult.OpResult> SaveAsync(string accessToken, CancellationToken cancellationToken);
}

internal sealed class ErpSettings
{
    internal const int SingletonId = 1;

    public int Id { get; set; }

    public required string ProtectedAccessToken { get; set; }
}

internal sealed class ErpTokenStore(AppDbContext db, IDataProtectionProvider protection) : IErpTokenStore
{
    private readonly IDataProtector _protector = protection.CreateProtector("OpenERP.Erp.AccessToken");

    public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken) =>
        db.ErpSettings.AnyAsync(settings => settings.Id == ErpSettings.SingletonId, cancellationToken);

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        var value = await db.ErpSettings.Where(settings => settings.Id == ErpSettings.SingletonId)
            .Select(settings => settings.ProtectedAccessToken)
            .SingleOrDefaultAsync(cancellationToken);
        return value is null ? "" : _protector.Unprotect(value);
    }

    public async Task<OpResult.OpResult> SaveAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            var protectedToken = _protector.Protect(accessToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ErpSettings" ("Id", "ProtectedAccessToken")
                VALUES ({ErpSettings.SingletonId}, {protectedToken})
                ON CONFLICT ("Id") DO UPDATE
                SET "ProtectedAccessToken" = EXCLUDED."ProtectedAccessToken"
                """, cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or CryptographicException)
        {
            return OpResults.Err("保存 ERP AccessToken 失败，请重试。");
        }

        return OpResults.Ok();
    }
}
