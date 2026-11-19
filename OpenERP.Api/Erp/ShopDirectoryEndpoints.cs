using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Authentication;

namespace OpenERP.Api.Erp;

/// <summary>授权店铺页的本地读取接口；不访问上游，wxw 离线时仍可用。</summary>
public static class ShopDirectoryEndpoints
{
    public static void MapShopDirectory(this WebApplication app)
    {
        app.MapGet("/api/erp/shops", GetShopsAsync)
            .WithSummary("从本地店铺目录分页查询店铺与授权状态");

        app.MapGet("/api/erp/platforms", GetPlatformsAsync)
            .WithSummary("从本地店铺目录返回有店铺的平台及计数");

        app.MapGet("/api/erp/shop-sync", GetShopSyncAsync)
            .WithSummary("查询店铺与授权状态同步的当前或最近一次结果");
    }

    private static async Task<Results<Ok<ShopDirectoryPage>, ProblemHttpResult>> GetShopsAsync(
        [FromServices] AppDbContext db,
        int? page,
        int? pageSize,
        string? platform,
        string? status,
        string? keyword,
        CancellationToken cancellationToken)
    {
        var currentPage = page ?? 1;
        var currentPageSize = pageSize ?? 20;
        if (currentPage < 1)
            return TypedResults.Problem("page 必须大于 0。", statusCode: 400);
        if (currentPageSize is < 1 or > 100)
            return TypedResults.Problem("pageSize 必须在 1 到 100 之间。", statusCode: 400);

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

        var normalizedPlatform = Normalize(platform);
        var normalizedKeyword = Normalize(keyword);

        try
        {
            // 先读状态行再读店铺行：同步提交是原子事务，此顺序只会得到“旧版本 + 新数据”，
            // 客户端会因 status.Version 更大而再次刷新，不会把旧数据标成最新。
            var state = await db.ShopSyncStates
                .AsNoTracking()
                .SingleOrDefaultAsync(syncState => syncState.Id == 1, cancellationToken);

            IQueryable<Shop> query = db.Shops.AsNoTracking();
            if (normalizedPlatform is not null)
                query = query.Where(shop => shop.Platform == normalizedPlatform);
            if (normalizedKeyword is not null)
            {
                var pattern = $"%{EscapeLike(normalizedKeyword)}%";
                query = int.TryParse(normalizedKeyword, NumberStyles.None, CultureInfo.InvariantCulture, out var sid)
                    ? query.Where(shop => shop.Sid == sid || EF.Functions.ILike(shop.Name, pattern, "\\"))
                    : query.Where(shop => EF.Functions.ILike(shop.Name, pattern, "\\"));
            }

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

            var items = rows.Select(shop => new ShopDirectoryItem(
                shop.Sid.ToString(CultureInfo.InvariantCulture),
                shop.Name,
                shop.Platform,
                shop.Region,
                shop.MemberId,
                shop.IsTokenOk,
                shop.TokenError,
                shop.SyncedAt)).ToArray();

            var summary = new ShopDirectorySummary(valid, invalid, unknown, state?.LastCompletedAt, state?.Version ?? 0);
            return TypedResults.Ok(new ShopDirectoryPage(
                items, currentPage, currentPageSize, totalCount, pageCount, summary));
        }
        catch (Exception exception) when (AuthenticationEndpoints.IsDatabaseFailure(exception))
        {
            return TypedResults.Problem("本地店铺数据暂时不可用。", statusCode: 503);
        }
    }

    private static async Task<Results<Ok<PlatformCatalog>, ProblemHttpResult>> GetPlatformsAsync(
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        try
        {
            var counts = await db.Shops
                .GroupBy(shop => shop.Platform)
                .Select(group => new { Name = group.Key, Count = group.Count() })
                .ToArrayAsync(cancellationToken);
            var catalog = await db.ShopPlatforms
                .AsNoTracking()
                .ToDictionaryAsync(platform => platform.Name, cancellationToken);

            var items = counts
                .Select(item => catalog.TryGetValue(item.Name, out var platform)
                    ? new ErpPlatform(
                        item.Name, platform.DisplayName, platform.Icon, platform.IsProduction, item.Count)
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

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

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
