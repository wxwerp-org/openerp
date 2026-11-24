using System.Globalization;
using System.Security.Claims;
using Mapster;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Authentication;

namespace OpenERP.Api.Erp;

/// <summary>授权店铺页的本地读取接口；不访问上游，wxw 离线时仍可用。</summary>
public static class ShopDirectoryEndpoints
{
    private static readonly TypeAdapterConfig DirectoryMappings = CreateDirectoryMappings();

    public static void MapShopDirectory(this WebApplication app)
    {
        app.MapGet("/api/erp/shops", GetShopsAsync)
            .WithSummary("从本地店铺目录分页查询店铺与授权状态");

        app.MapGet("/api/erp/platforms", GetPlatformsAsync)
            .WithSummary("从本地店铺目录返回有店铺的平台及计数");

        app.MapGet("/api/erp/shop-sync", GetShopSyncAsync)
            .RequireAuthorization(UserAccount.AdministratorRole)
            .WithSummary("查询店铺与授权状态同步的当前或最近一次结果");
    }

    private static async Task<Results<Ok<ShopDirectoryPage>, Ok<EmployeeShopDirectoryPage>, ProblemHttpResult>>
        GetShopsAsync(
            [FromServices] AppDbContext db,
            ClaimsPrincipal user,
            HttpRequest request,
            CancellationToken cancellationToken)
    {
        // 角色检查先于参数解析：员工只要携带 status 键（含空值和空白）即 403，
        // 不能被参数绑定失败先返回的 400 抢先。
        var access = ShopAccess.For(user);
        if (!access.IsAdministrator && request.Query.ContainsKey("status"))
            return TypedResults.Problem("仅管理员可按授权状态筛选店铺。", statusCode: 403);

        if (!TryReadInt(request, "page", out var page) || !TryReadInt(request, "pageSize", out var pageSize))
            return TypedResults.Problem("page 和 pageSize 必须是整数。", statusCode: 400);

        var currentPage = page ?? 1;
        var currentPageSize = pageSize ?? 20;
        if (currentPage < 1)
            return TypedResults.Problem("page 必须大于 0。", statusCode: 400);
        if (currentPageSize is < 1 or > 100)
            return TypedResults.Problem("pageSize 必须在 1 到 100 之间。", statusCode: 400);

        var status = request.Query["status"].ToString();
        ShopStatusFilter? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            statusFilter = status.Trim().ToLowerInvariant() switch
            {
                "valid" => ShopStatusFilter.Valid,
                "invalid" => ShopStatusFilter.Invalid,
                "unknown" => ShopStatusFilter.Unknown,
                _ => null
            };
            if (statusFilter is null)
                return TypedResults.Problem("status 必须是 valid、invalid 或 unknown。", statusCode: 400);
        }

        var normalizedPlatform = Normalize(request.Query["platform"].ToString());
        var normalizedKeyword = Normalize(request.Query["keyword"].ToString());

        try
        {
            var query = access.Shops(db).AsNoTracking();
            if (normalizedPlatform is not null)
                query = query.Where(shop => shop.Platform == normalizedPlatform);
            if (normalizedKeyword is not null)
            {
                var searchName = normalizedKeyword.ToUpperInvariant();
                query = int.TryParse(normalizedKeyword, NumberStyles.None, CultureInfo.InvariantCulture, out var sid)
                    ? query.Where(shop => shop.Sid == sid || shop.SearchName!.Contains(searchName))
                    : query.Where(shop => shop.SearchName!.Contains(searchName));
            }

            // 员工仅查看分配店铺的状态，不读取全局汇总与同步状态。
            if (!access.IsAdministrator)
                return await ReadEmployeePageAsync(query, currentPage, currentPageSize, cancellationToken);

            // 先读状态行再读店铺行：同步提交是原子事务，此顺序只会得到“旧版本 + 新数据”，
            // 客户端会因 status.Version 更大而再次刷新，不会把旧数据标成最新。
            var state = await db.ShopSyncStates
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);

            // 汇总只受平台和关键词影响，不受当前状态筛选影响。
            var counts = await query
                .GroupBy(shop => shop.IsTokenOk)
                .Select(group => new { IsTokenOk = group.Key, Count = group.Count() })
                .ToArrayAsync(cancellationToken);
            var valid = counts.Where(item => item.IsTokenOk == true).Sum(item => item.Count);
            var invalid = counts.Where(item => item.IsTokenOk == false).Sum(item => item.Count);
            var unknown = counts.Where(item => item.IsTokenOk == null).Sum(item => item.Count);

            query = statusFilter switch
            {
                ShopStatusFilter.Valid => query.Where(shop => shop.IsTokenOk == true),
                ShopStatusFilter.Invalid => query.Where(shop => shop.IsTokenOk == false),
                ShopStatusFilter.Unknown => query.Where(shop => shop.IsTokenOk == null),
                _ => query
            };

            var totalCount = await query.CountAsync(cancellationToken);
            var pageCount = totalCount == 0 ? 0 : (totalCount + currentPageSize - 1) / currentPageSize;
            currentPage = pageCount == 0 ? 1 : Math.Min(currentPage, pageCount);

            var rows = await query
                .OrderBy(shop => shop.Name)
                .ThenBy(shop => shop.Sid)
                .Skip((currentPage - 1) * currentPageSize)
                .Take(currentPageSize)
                .ToArrayAsync(cancellationToken);

            var items = rows.Adapt<ShopDirectoryItem[]>(DirectoryMappings);

            var summary = new ShopDirectorySummary(valid, invalid, unknown, state?.LastCompletedAt, state?.Version ?? 0);
            return TypedResults.Ok(new ShopDirectoryPage(
                items, currentPage, currentPageSize, totalCount, pageCount, summary));
        }
        catch (Exception exception) when (AuthenticationEndpoints.IsDatabaseFailure(exception))
        {
            return TypedResults.Problem("本地店铺数据暂时不可用。", statusCode: 503);
        }
    }

    private static async Task<Ok<EmployeeShopDirectoryPage>> ReadEmployeePageAsync(
        IQueryable<Shop> query, int currentPage, int currentPageSize, CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var pageCount = totalCount == 0 ? 0 : (totalCount + currentPageSize - 1) / currentPageSize;
        currentPage = pageCount == 0 ? 1 : Math.Min(currentPage, pageCount);

        var rows = await query
            .OrderBy(shop => shop.Name)
            .ThenBy(shop => shop.Sid)
            .Skip((currentPage - 1) * currentPageSize)
            .Take(currentPageSize)
            .Select(shop => new { shop.Sid, shop.Name, shop.Platform, shop.Region, shop.IsTokenOk, shop.TokenError, shop.SyncedAt })
            .ToArrayAsync(cancellationToken);
        var items = rows
            .Select(shop => new EmployeeShopDirectoryItem(
                shop.Sid.ToString(CultureInfo.InvariantCulture), shop.Name, shop.Platform, shop.Region,
                shop.IsTokenOk, shop.TokenError, shop.SyncedAt))
            .ToArray();
        return TypedResults.Ok(new EmployeeShopDirectoryPage(
            items, currentPage, currentPageSize, totalCount, pageCount));
    }

    private static async Task<Results<Ok<PlatformCatalog>, ProblemHttpResult>> GetPlatformsAsync(
        [FromServices] AppDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        try
        {
            var counts = await ShopAccess.For(user).Shops(db)
                .GroupBy(shop => shop.Platform)
                .Select(group => new { Name = group.Key, Count = group.Count() })
                .ToArrayAsync(cancellationToken);
            var catalog = await db.ShopPlatforms
                .AsNoTracking()
                .ToDictionaryAsync(platform => platform.Name, cancellationToken);

            var items = counts
                .Select(item => catalog.TryGetValue(item.Name, out var platform)
                    ? platform.Adapt<ErpPlatform>(DirectoryMappings) with { ShopCount = item.Count }
                    : new ErpPlatform(item.Name, item.Name, null, true, item.Count))
                .OrderByDescending(platform => platform.ShopCount)
                .ThenBy(platform => platform.Name, StringComparer.Ordinal)
                .ToArray();

            return TypedResults.Ok(new PlatformCatalog(items));
        }
        catch (Exception exception) when (AuthenticationEndpoints.IsDatabaseFailure(exception))
        {
            return TypedResults.Problem("本地店铺数据暂时不可用。", statusCode: 503);
        }
    }

    private static async Task<Results<Ok<ShopSyncStatus>, ProblemHttpResult>> GetShopSyncAsync(
        [FromServices] AppDbContext db,
        [FromServices] IShopSyncJob job,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await ShopSyncStatusQueries.ReadAsync(job, db, cancellationToken));
        }
        catch (Exception exception) when (AuthenticationEndpoints.IsDatabaseFailure(exception))
        {
            return TypedResults.Problem("本地同步状态暂时不可用。", statusCode: 503);
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>读取整数查询参数；键缺省按未提供处理，其余取值按 Minimal API 绑定语义解析。</summary>
    private static bool TryReadInt(HttpRequest request, string name, out int? value)
    {
        value = null;
        if (!request.Query.TryGetValue(name, out var raw))
            return true;
        if (!int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return false;

        value = parsed;
        return true;
    }

    private static TypeAdapterConfig CreateDirectoryMappings()
    {
        var config = new TypeAdapterConfig();
        config.NewConfig<Shop, ShopDirectoryItem>()
            .MapToConstructor(true)
            .Map(destination => destination.Sid, shop => shop.Sid.ToString(CultureInfo.InvariantCulture));
        config.NewConfig<ShopPlatform, ErpPlatform>().MapToConstructor(true);
        return config;
    }

    private enum ShopStatusFilter
    {
        Valid,
        Invalid,
        Unknown
    }
}

/// <summary>本地店铺目录中的一家店铺。</summary>
public sealed record ShopDirectoryItem(
    string Sid,
    string Name,
    string Platform,
    string? Region,
    string? MemberId,
    bool? IsTokenOk,
    string? TokenError,
    DateTimeOffset? SyncedAt);

/// <summary>本地汇总；不随状态筛选变化。SyncVersion 是读取目录时的同步状态版本。</summary>
public sealed record ShopDirectorySummary(
    int Valid, int Invalid, int Unknown, DateTimeOffset? LastSyncedAt, long SyncVersion);

/// <summary>本地店铺分页结果。</summary>
public sealed record ShopDirectoryPage(
    IReadOnlyList<ShopDirectoryItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int PageCount,
    ShopDirectorySummary Summary);

/// <summary>员工可见的分配店铺及店铺授权状态。</summary>
public sealed record EmployeeShopDirectoryItem(string Sid, string Name, string Platform, string? Region,
    bool? IsTokenOk, string? TokenError, DateTimeOffset? SyncedAt);

/// <summary>员工可见的店铺分页；不返回授权汇总。</summary>
public sealed record EmployeeShopDirectoryPage(
    IReadOnlyList<EmployeeShopDirectoryItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int PageCount);
