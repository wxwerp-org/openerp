using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OpenERP.Api.Deployment;

namespace OpenERP.Api.Migrations;

internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var builder = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath("appsettings.json"), optional: true);
        var configuration = ContainerCommands.AddRuntimeConfiguration(builder).Build();
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(configuration.GetConnectionString("openerp"))
            .Options);
    }
}
