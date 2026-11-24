using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenERP.Api.Migrations;

internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args).Build();
        var connection = configuration.GetConnectionString("openerp");
        var options = new DbContextOptionsBuilder<AppDbContext>();
        if (DatabaseRegistration.UsesSqlite(configuration))
            options.UseOpenErpSqlite(connection);
        else
            options.UseOpenErpPostgres(connection);
        return new AppDbContext(options.Options);
    }
}
