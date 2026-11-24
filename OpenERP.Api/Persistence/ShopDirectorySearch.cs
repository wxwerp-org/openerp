using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Erp;

namespace OpenERP.Api.Persistence;

internal sealed class PostgresShopDirectorySearch : IShopDirectorySearch
{
    // 两边都在数据库中转换大小写，以免关键词受当前 .NET 进程区域设置影响。
    public IQueryable<Shop> Filter(IQueryable<Shop> visibleShops, ShopKeyword keyword) =>
        visibleShops.Where(shop => shop.Sid == keyword.Sid || shop.Name.ToLower().Contains(EF.Parameter(keyword.Text).ToLower()));
}

internal sealed class SqliteShopDirectorySearch : IShopDirectorySearch
{
    private const string FunctionName = "openerp_contains_ignore_case";

    public SqliteShopDirectorySearch(SqliteAppDbContext db)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        connection.CreateFunction<string, string, bool>(FunctionName, ContainsIgnoreCase, isDeterministic: true);
    }

    internal static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.HasDbFunction(() => ContainsIgnoreCase("", "")).HasName(FunctionName);

    // SQLite 内置的大小写转换只处理 ASCII。此函数保留非 ASCII 店名的原文匹配。
    private static bool ContainsIgnoreCase(string name, string keyword) =>
        name.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    public IQueryable<Shop> Filter(IQueryable<Shop> visibleShops, ShopKeyword keyword) =>
        visibleShops.Where(shop => shop.Sid == keyword.Sid || ContainsIgnoreCase(shop.Name, keyword.Text));
}
