using Microsoft.EntityFrameworkCore;

namespace OpenERP.Api;

internal static class DatabaseRegistration
{
    internal static bool UsesSqlite(IConfiguration configuration) =>
        configuration["Database:Provider"]?.ToUpperInvariant() switch
        {
            null or "POSTGRESQL" => false,
            "SQLITE" => true,
            var provider => throw new InvalidOperationException($"未知的 Database:Provider 值 {provider}。")
        };

    internal static void AddApplicationDatabase(this IHostApplicationBuilder builder)
    {
        if (UsesSqlite(builder.Configuration))
        {
            var connection = builder.Configuration.GetConnectionString("openerp");
            if (string.IsNullOrWhiteSpace(connection))
                throw new InvalidOperationException("未配置 ConnectionStrings:openerp。");
            builder.Services.AddDbContext<AppDbContext>(options => options.UseOpenErpSqlite(connection));
        }
        else
        {
            builder.AddNpgsqlDbContext<AppDbContext>("openerp",
                configureDbContextOptions: options => options.UseNpgsql(pg => pg.MigrationsAssembly(DatabaseOptions.PostgresMigrations)));
        }
    }
}
