using System.Data;
using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using OpResult;
using OpenERP.Api.Authentication;

namespace OpenERP.Api.Orders;

internal sealed class OrderListQuery(AppDbContext db, ILogger<OrderListQuery> logger)
{
    internal async Task<(OpResult<OrderListResponse> Result, int Status)> ReadAsync(
        OrderListRequest request, ShopAccess access, CancellationToken cancellationToken)
    {
        try
        {
            // 订单同步可能同时改变行和子单；选项、总数与明细来自同一个数据库快照。
            return await db.Database.CreateExecutionStrategy().ExecuteAsync<(OpResult<OrderListResponse>, int)>(async ct =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
                var platform = string.IsNullOrEmpty(request.Platform) ? null : request.Platform;
                if (request.ShopSid is { } sid && !await access.AllowsAsync(db, platform, sid, ct))
                    return (OpResults.Err("无权查看该店铺。"), StatusCodes.Status403Forbidden);
                var response = await ReadSnapshotAsync(request, access, ct);
                await transaction.CommitAsync(ct);
                return (OpResults.Ok(response), StatusCodes.Status200OK);
            }, cancellationToken);
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
        var selected = orders;
        if (!access.IsAdministrator)
        {
            // 目录上的 SID 可能对应另一个平台，历史订单只使用同平台的店铺名称。
            directory = directory.Where(shop => orders.Any(order => order.Platform == shop.Platform && order.Sid == shop.Sid));
            childOrders = childOrders.Where(po => orders.Any(order => order.ErpId == po.ErpId));
        }

        var platformOrders = childOrders.Select(po => new
        {
            po.ErpId,
            po.PoNumber,
            Status = po.StatusText != null && po.StatusText.Trim(' ', '\t', '\n', '\r') != "" ? po.StatusText :
                po.StatusCode != null && po.StatusCode.Trim(' ', '\t', '\n', '\r') != "" ? po.StatusCode : "未知"
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
        if (request.CreatedFrom.HasValue || request.CreatedTo.HasValue)
        {
            var fromTicks = (long)(request.CreatedFrom?.DayNumber ?? 0) * TimeSpan.TicksPerDay;
            selected = selected.Where(order => order.CreatedAtTicks >= fromTicks);
        }
        if (request.CreatedTo is { } to)
        {
            var endTicks = (to.DayNumber + 1L) * TimeSpan.TicksPerDay;
            selected = selected.Where(order => order.CreatedAtTicks < endTicks);
        }
        if (filterPlatformOrders)
            selected = selected.Where(order => children.Any(po => po.ErpId == order.ErpId && po.Matched));

        var total = await selected.LongCountAsync(ct);
        if (total == 0)
            return new([], 0, 1, request.PageSize, options);

        var page = (int)Math.Min(request.Page, (total - 1) / request.PageSize + 1);
        var lines = db.Set<OrderLineRecord>();
        var rows = await selected.OrderByDescending(order => order.CreatedAtTicks).ThenBy(order => order.ErpId)
            .Skip((page - 1) * request.PageSize).Take(request.PageSize)
            .Select(order => new
            {
                order.ErpId,
                order.Sid,
                order.Platform,
                order.CreatedRaw,
                order.CreatedAtTicks,
                order.DispatchedRaw,
                order.Status,
                order.Buyer,
                order.TotalAmount,
                order.TotalCurrency,
                order.Logistics,
                order.PaidRaw,
                order.Details,
                LineCount = lines.Count(line => line.ErpId == order.ErpId),
                ItemQuantity = lines.Where(line => line.ErpId == order.ErpId).Sum(line => (long)line.Quantity)
            }).ToArrayAsync(ct);

        var ids = rows.Select(row => row.ErpId).ToArray();
        var childRows = await children.Where(po => ids.Contains(po.ErpId)).ToArrayAsync(ct);
        var childrenByOrder = childRows.OrderBy(po => po.PoNumber, StringComparer.Ordinal).ToLookup(po => po.ErpId,
            po => new PlatformOrderListItem(po.PoNumber, po.Status, po.Matched));
        var lineRows = await lines.AsNoTracking().Where(line => ids.Contains(line.ErpId)).OrderBy(line => line.Ordinal)
            .ToArrayAsync(ct);
        var linesByOrder = lineRows.ToLookup(line => line.ErpId, line => new OrderLineListItem(line.ProductId, line.SkuCode,
            line.Name, line.ImageUrl, line.Specs, line.Quantity, line.UnitPrice, line.Currency, line.Logistics,
            ParseDetails(line.Details)));
        var shopDetails = shops.ToDictionary(shop => shop.Sid);
        var items = rows.Select(row =>
        {
            var shop = shopDetails[row.Sid];
            var name = access.IsAdministrator || shop.Platform == row.Platform ? shop.Name : null;
            return new OrderListItem(row.ErpId, row.Sid, name, row.Platform,
                childrenByOrder[row.ErpId].ToArray(),
                OrderSourceTime.Format(row.CreatedRaw, row.CreatedAtTicks),
                OrderSourceTime.Format(row.DispatchedRaw, OrderSourceTime.ToTicks(row.DispatchedRaw)),
                row.LineCount, row.ItemQuantity, linesByOrder[row.ErpId].ToArray(),
                row.Status, row.Buyer, row.TotalAmount, row.TotalCurrency, row.Logistics, row.PaidRaw,
                ParseDetails(row.Details));
        }).ToArray();

        return new(items, total, page, request.PageSize, options);
    }

    /// <summary>读取单个本地订单（与列表同一结构）；订单不存在或无权访问时返回 null。</summary>
    internal async Task<OrderListItem?> ReadOneAsync(string erpId, ShopAccess access, CancellationToken ct)
    {
        var order = await access.Orders(db).AsNoTracking().FirstOrDefaultAsync(row => row.ErpId == erpId, ct);
        if (order is null)
            return null;

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
            OrderSourceTime.Format(order.CreatedRaw, order.CreatedAtTicks),
            OrderSourceTime.Format(order.DispatchedRaw, OrderSourceTime.ToTicks(order.DispatchedRaw)),
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
}
