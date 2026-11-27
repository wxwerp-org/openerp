using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.Common;
using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using OpResult;
using OpenERP.Api.Authentication;

namespace OpenERP.Api.Orders;

internal sealed class OrderListQuery(AppDbContext db, ILogger<OrderListQuery> logger)
{
    private static readonly string OrderHeadersSql = $$"""
        SELECT o."ErpId", o."Sid", o."Platform",
               parsed.created_at AS "CreatedAt", parsed.created_at::date AS "CreatedDate",
               parsed.dispatched_at AS "DispatchedAt",
               source.created AS "CreatedRaw", source.dispatched AS "DispatchedRaw"
        FROM "Orders" AS o
        CROSS JOIN LATERAL (VALUES (btrim(o."CreatedRaw"), btrim(o."DispatchedRaw"))) AS source(created, dispatched)
        CROSS JOIN LATERAL (VALUES (
            {{OrderCreatedTimeSql.Normalize("source.created")}},
            {{OrderCreatedTimeSql.Normalize("source.dispatched")}}
        )) AS normalized(created, dispatched)
        CROSS JOIN LATERAL (
            SELECT {{OrderCreatedTimeSql.Parse("source.created", "normalized.created")}} AS created_at,
                   {{OrderCreatedTimeSql.Parse("source.dispatched", "normalized.dispatched")}} AS dispatched_at
        ) AS parsed
        WHERE o."DeletedAt" IS NULL
        """;

    internal async Task<(OpResult<OrderListResponse> Result, int Status)> ReadAsync(
        OrderListRequest request, ShopAccess access, CancellationToken ct)
    {
        try
        {
            // 订单同步可能同时改变行和子单；选项、总数与明细来自同一个数据库快照。
            return await db.Database.CreateExecutionStrategy().ExecuteAsync<(OpResult<OrderListResponse>, int)>(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
                var platform = string.IsNullOrEmpty(request.Platform) ? null : request.Platform;
                if (request.ShopSid is { } sid && !await access.AllowsAsync(db, platform, sid, ct))
                    return (OpResults.Err("无权查看该店铺。"), StatusCodes.Status403Forbidden);
                var response = await ReadSnapshotAsync(request, access, ct);
                await transaction.CommitAsync(ct);
                return (OpResults.Ok(response), StatusCodes.Status200OK);
            });
        }
        catch (Exception exception) when (exception is DbException or RetryLimitExceededException)
        {
            logger.LogError(exception, "读取本地订单列表失败。");
            return (OpResults.Err("读取本地订单失败，请重试。"), StatusCodes.Status503ServiceUnavailable);
        }
    }

    private async Task<OrderListResponse> ReadSnapshotAsync(OrderListRequest request, ShopAccess access, CancellationToken ct)
    {
        var orders = access.Orders(db).AsNoTracking();
        var directory = access.Shops(db).AsNoTracking();
        var childOrders = db.Set<PlatformOrderRecord>().AsNoTracking();
        var selected = ReadHeaders();
        if (!access.IsAdministrator)
        {
            // 目录上的 SID 可能对应另一个平台，历史订单只使用同平台的店铺名称。
            directory = directory.Where(shop => orders.Any(order => order.Platform == shop.Platform && order.Sid == shop.Sid));
            childOrders = childOrders.Where(po => orders.Any(order => order.ErpId == po.ErpId));
            selected = selected.Where(header => orders.Any(order => order.ErpId == header.ErpId));
        }

        var platformOrders = childOrders.Select(po => new
        {
            po.ErpId,
            po.PoNumber,
            Status = !string.IsNullOrWhiteSpace(po.StatusText) ? po.StatusText! :
                !string.IsNullOrWhiteSpace(po.StatusCode) ? po.StatusCode! : "未知"
        });
        var shops = await orders.Select(order => order.Sid).Distinct()
            .LeftJoin(directory, sid => sid, shop => shop.Sid,
                (sid, shop) => new { Sid = sid, Name = shop == null ? null : shop.Name, Platform = shop == null ? null : shop.Platform })
            .OrderBy(shop => shop.Sid).ToArrayAsync(ct);
        var platforms = await orders.Select(order => order.Platform).Distinct().ToArrayAsync(ct);
        var statuses = await platformOrders.Select(po => po.Status).Distinct().ToArrayAsync(ct);
        var options = new OrderListOptions(shops.Select(shop => new OrderListShop(shop.Sid, shop.Name)).ToArray(),
            platforms.Order(StringComparer.Ordinal).ToArray(), statuses.Order(StringComparer.Ordinal).ToArray());

        var number = string.IsNullOrEmpty(request.Number) ? null : request.Number;
        var status = string.IsNullOrEmpty(request.Status) ? null : request.Status;
        var filterPlatformOrders = number is not null || status is not null;
        var children = platformOrders.Select(po => new
        {
            po.ErpId,
            po.PoNumber,
            po.Status,
            Matched = filterPlatformOrders && (number == null || po.PoNumber.Contains(number))
                && (status == null || po.Status == status)
        });
        if (request.ShopSid is { } sid)
            selected = selected.Where(order => order.Sid == sid);
        if (!string.IsNullOrEmpty(request.Platform))
            selected = selected.Where(order => order.Platform == request.Platform);
        if (request.CreatedFrom is { } from)
            selected = selected.Where(order => order.CreatedDate >= from);
        if (request.CreatedTo is { } to)
            selected = selected.Where(order => order.CreatedDate <= to);
        if (filterPlatformOrders)
            selected = selected.Where(order => children.Any(po => po.ErpId == order.ErpId && po.Matched));

        var total = await selected.LongCountAsync(ct);
        if (total == 0)
            return new([], 0, 1, request.PageSize, options);

        var page = (int)Math.Min(request.Page, (total - 1) / request.PageSize + 1);
        var lines = db.Set<OrderLineRecord>();
        var rows = await selected.OrderBy(order => order.CreatedAt == null)
            .ThenByDescending(order => order.CreatedAt).ThenBy(order => order.ErpId)
            .Skip((page - 1) * request.PageSize).Take(request.PageSize)
            .Select(order => new
            {
                Order = order,
                LineCount = lines.Count(line => line.ErpId == order.ErpId),
                ItemQuantity = lines.Where(line => line.ErpId == order.ErpId).Sum(line => (long)line.Quantity)
            }).ToArrayAsync(ct);

        var ids = rows.Select(row => row.Order.ErpId).ToArray();
        var childRows = await children.Where(po => ids.Contains(po.ErpId)).ToArrayAsync(ct);
        var childrenByOrder = childRows.OrderBy(po => po.PoNumber, StringComparer.Ordinal).ToLookup(po => po.ErpId,
            po => new PlatformOrderListItem(po.PoNumber, po.Status, po.Matched));
        var lineRows = await lines.AsNoTracking().Where(line => ids.Contains(line.ErpId)).OrderBy(line => line.Ordinal)
            .ToArrayAsync(ct);
        var linesByOrder = lineRows.ToLookup(line => line.ErpId, line => new OrderLineListItem(line.ProductId, line.SkuCode,
            line.Name, line.ImageUrl, line.Specs, line.Quantity, line.UnitPrice, line.Currency, line.Logistics,
            ParseDetails(line.Details)));
        var orderRows = await orders.Where(order => ids.Contains(order.ErpId))
            .Select(order => new { order.ErpId, order.Status, order.Buyer, order.TotalAmount, order.TotalCurrency,
                order.Logistics, order.PaidRaw, order.Details })
            .ToDictionaryAsync(order => order.ErpId, ct);
        var shopDetails = shops.ToDictionary(shop => shop.Sid);
        var items = rows.Select(row =>
        {
            var shop = shopDetails[row.Order.Sid];
            var detail = orderRows[row.Order.ErpId];
            var name = access.IsAdministrator || shop.Platform == row.Order.Platform ? shop.Name : null;
            return new OrderListItem(row.Order.ErpId, row.Order.Sid, name, row.Order.Platform,
                childrenByOrder[row.Order.ErpId].ToArray(),
                FormatTime(row.Order.CreatedAt, row.Order.CreatedRaw), FormatTime(row.Order.DispatchedAt, row.Order.DispatchedRaw),
                row.LineCount, row.ItemQuantity, linesByOrder[row.Order.ErpId].ToArray(),
                detail.Status, detail.Buyer, detail.TotalAmount, detail.TotalCurrency, detail.Logistics, detail.PaidRaw,
                ParseDetails(detail.Details));
        }).ToArray();

        return new(items, total, page, request.PageSize, options);
    }

    /// <summary>读取单个本地订单（与列表同一结构）；订单不存在或无权访问时返回 null。</summary>
    internal async Task<OrderListItem?> ReadOneAsync(string erpId, ShopAccess access, CancellationToken ct)
    {
        var orders = access.Orders(db).AsNoTracking();
        var header = await ReadHeaders().Where(row => row.ErpId == erpId && orders.Any(order => order.ErpId == row.ErpId))
            .FirstOrDefaultAsync(ct);
        if (header is null)
            return null;

        var order = await orders.FirstAsync(row => row.ErpId == erpId, ct);
        var shopName = await access.Shops(db).AsNoTracking()
            .Where(shop => shop.Sid == order.Sid && shop.Platform == order.Platform)
            .Select(shop => shop.Name).FirstOrDefaultAsync(ct);
        var platformOrders = await db.Set<PlatformOrderRecord>().AsNoTracking().Where(po => po.ErpId == erpId)
            .OrderBy(po => po.PoNumber).ToArrayAsync(ct);
        var lines = await db.Set<OrderLineRecord>().AsNoTracking().Where(line => line.ErpId == erpId)
            .OrderBy(line => line.Ordinal).ToArrayAsync(ct);
        return new OrderListItem(order.ErpId, order.Sid, shopName, order.Platform,
            [.. platformOrders.Select(po => new PlatformOrderListItem(po.PoNumber,
                !string.IsNullOrWhiteSpace(po.StatusText) ? po.StatusText! :
                !string.IsNullOrWhiteSpace(po.StatusCode) ? po.StatusCode! : "未知", false))],
            FormatTime(header.CreatedAt, header.CreatedRaw), FormatTime(header.DispatchedAt, header.DispatchedRaw),
            lines.Length, lines.Sum(line => (long)line.Quantity),
            [.. lines.Select(line => new OrderLineListItem(line.ProductId, line.SkuCode, line.Name, line.ImageUrl,
                line.Specs, line.Quantity, line.UnitPrice, line.Currency, line.Logistics, ParseDetails(line.Details)))],
            order.Status, order.Buyer, order.TotalAmount, order.TotalCurrency, order.Logistics, order.PaidRaw,
            ParseDetails(order.Details));
    }

    private static IReadOnlyList<OrderDetail> ParseDetails(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<OrderDetail[]>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    internal IQueryable<OrderListHeader> ReadHeaders() =>
        db.Database.SqlQueryRaw<OrderListHeader>(OrderHeadersSql, OrderCreatedTimeSql.Parameters());

    private static string? FormatTime(DateTime? value, string? source)
    {
        if (value is not { } time || source is null)
            return null;
        if (source.Length == 10 && source.All(char.IsAsciiDigit))
            return time.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        // 只统一日期顺序和分隔符，保留来源的时间精度及显式偏移。
        var date = time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return source.Length > 10 ? date + "T" + source[11..] : date;
    }

    internal sealed record OrderListHeader(string ErpId, int Sid, string Platform,
        [property: Column(TypeName = "timestamp without time zone")] DateTime? CreatedAt,
        DateOnly? CreatedDate,
        [property: Column(TypeName = "timestamp without time zone")] DateTime? DispatchedAt,
        string? CreatedRaw, string? DispatchedRaw);
}
