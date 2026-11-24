using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace OpenERP.Api;

internal static class DatabaseOptions
{
    internal const string PostgresMigrations = "OpenERP.Migrations.PostgreSQL";
    internal const string SqliteMigrations = "OpenERP.Migrations.Sqlite";

    internal static TBuilder UseOpenErpPostgres<TBuilder>(this TBuilder builder, string? connectionString = null,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null) where TBuilder : DbContextOptionsBuilder
    {
        builder.UseNpgsql(connectionString, postgres =>
        {
            postgres.MigrationsAssembly(PostgresMigrations);
            configure?.Invoke(postgres);
        });
        return builder;
    }

    internal static TBuilder UseOpenErpSqlite<TBuilder>(this TBuilder builder, string? connectionString)
        where TBuilder : DbContextOptionsBuilder
    {
        builder.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(SqliteMigrations));
        return builder;
    }
}
