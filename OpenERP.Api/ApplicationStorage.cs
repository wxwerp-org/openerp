using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Authentication;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using OpenERP.Api.Persistence;

namespace OpenERP.Api;

internal static class ApplicationStorageRegistration
{
    internal static void AddApplicationStorage(this WebApplicationBuilder builder, bool maintenance = false)
    {
        var directory = NativeDataDirectory.Resolve(builder.Configuration, NativeLocalHosting.IsPublished);
        if (directory is null)
        {
            if (maintenance && string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("openerp")))
                throw new InvalidOperationException("未配置 ConnectionStrings:openerp。");
            builder.AddNpgsqlDbContext<AppDbContext>("openerp");
            builder.Services.AddPostgresPersistence();
            if (!maintenance)
            {
                builder.AddRedisDistributedCache("redis");
                builder.AddS3FileStorage();
            }
            return;
        }

        builder.Services.AddSingleton(_ => maintenance
            ? NativeDataDirectory.AcquireExistingLease(directory)
            : NativeDataDirectory.AcquireLease(directory));
        builder.Services.AddSqlitePersistence((services, options) => options.UseSqlite(
            NativeDatabase.ConnectionString(services.GetRequiredService<NativeDataDirectory>().Root, createIfMissing: !maintenance)));
        if (!maintenance)
            builder.Services.AddSingleton<IFileStore>(services => new DirectoryFileBucket(
                new DirectoryInfo(Path.Combine(services.GetRequiredService<NativeDataDirectory>().Root, "objects")),
                services.GetRequiredService<ILogger<DirectoryFileBucket>>()));
    }

    internal static void AddPostgresPersistence(this IServiceCollection services)
    {
        services.AddScoped<IApplicationDatabase, PostgresDatabase>();
        services.AddScoped<IAccountWrites, PostgresAccountWrites>();
        services.AddScoped<IShopDirectorySearch, PostgresShopDirectorySearch>();
        services.AddScoped<IOrderReadSource, PostgresOrderReadSource>();
    }

    internal static void AddSqlitePersistence(this IServiceCollection services, Action<IServiceProvider, DbContextOptionsBuilder> configure)
    {
        services.AddDbContext<AppDbContext, SqliteAppDbContext>(configure);
        services.AddScoped<IApplicationDatabase, NativeDatabase>();
        services.AddScoped<IAccountWrites, SqliteAccountWrites>();
        services.AddScoped<IShopDirectorySearch, SqliteShopDirectorySearch>();
        services.AddScoped<IOrderReadSource, SqliteOrderReadSource>();
    }
}
