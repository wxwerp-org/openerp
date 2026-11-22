using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.Common;
using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

using OpResult;
using OpenERP.Api.Authentication;

namespace OpenERP.Api.Orders;

internal sealed class OrderListQuery(AppDbContext db, ILogger<OrderListQuery> logger)
{
    private const string IsoTimePattern = @"^[0-9]{4}-[0-9]{2}-[0-9]{2}([ T]([01][0-9]|2[0-3]):[0-5][0-9](:[0-5][0-9](\.[0-9]{1,6})?)?(Z|[+-]([01][0-9]:[0-5][0-9]))?)?$";
    private const string YearLastPattern = "^[0-9]{2}-[0-9]{2}-[0-9]{4}($|[ T])";
    private const string HourOnlyPattern = "([ T][0-9]{2})$";
    private const string EpochSecondsPattern = "^[0-9]{10}$";

    private static readonly string OrderHeadersSql = $$"""
        SELECT o."ErpId", o."Sid", o."Platform",
               parsed.created_at AS "CreatedAt", parsed.created_at::date AS "CreatedDate",
               parsed.dispatched_at AS "DispatchedAt",
               source.created AS "CreatedRaw", source.dispatched AS "DispatchedRaw"
        FROM "Orders" AS o
        CROSS JOIN LATERAL (VALUES (btrim(o."CreatedRaw"), btrim(o."DispatchedRaw"))) AS source(created, dispatched)
        CROSS JOIN LATERAL (VALUES (
            regexp_replace({{NormalizeYearLastSql("source.created")}}, @hour_only, @hour_with_minute),
            regexp_replace({{NormalizeYearLastSql("source.dispatched")}}, @hour_only, @hour_with_minute)
        )) AS normalized(created, dispatched)
        CROSS JOIN LATERAL (
            SELECT CASE
                WHEN normalized.created ~ @iso AND pg_input_is_valid(normalized.created, 'timestamp without time zone')
                    THEN normalized.created::timestamp
                WHEN source.created ~ @epoch THEN to_timestamp(source.created::bigint) AT TIME ZONE 'UTC'
            END AS created_at,
            CASE
                WHEN normalized.dispatched ~ @iso AND pg_input_is_valid(normalized.dispatched, 'timestamp without time zone')
                    THEN normalized.dispatched::timestamp
                WHEN source.dispatched ~ @epoch THEN to_timestamp(source.dispatched::bigint) AT TIME ZONE 'UTC'
            END AS dispatched_at
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
        var shopDetails = shops.ToDictionary(shop => shop.Sid);
        var items = rows.Select(row =>
        {
            var shop = shopDetails[row.Order.Sid];
            var name = access.IsAdministrator || shop.Platform == row.Order.Platform ? shop.Name : null;
            return new OrderListItem(row.Order.ErpId, row.Order.Sid, name, row.Order.Platform,
                childrenByOrder[row.Order.ErpId].ToArray(),
                FormatTime(row.Order.CreatedAt, row.Order.CreatedRaw), FormatTime(row.Order.DispatchedAt, row.Order.DispatchedRaw),
                row.LineCount, row.ItemQuantity);
        }).ToArray();

        return new(items, total, page, request.PageSize, options);
    }

    private IQueryable<OrderListHeader> ReadHeaders() =>
        // PostgreSQL 18 在强制转换前验证日历日期。无时区时间按来源原值排序，有偏移的值保留其偏移。
        db.Database.SqlQueryRaw<OrderListHeader>(OrderHeadersSql,
            new NpgsqlParameter("iso", IsoTimePattern), new NpgsqlParameter("year_last", YearLastPattern),
            new NpgsqlParameter("hour_only", HourOnlyPattern), new NpgsqlParameter("hour_with_minute", @"\1:00"),
            new NpgsqlParameter("epoch", EpochSecondsPattern));

    // 仅接收上述 SQL 中的固定列引用。年在后时，两个不同的 01–12 分量会产生两个有效日期。
    private static string NormalizeYearLastSql(string column) => $$"""
        CASE WHEN {{column}} ~ @year_last THEN
            CASE
                WHEN substr({{column}}, 1, 2)::int BETWEEN 1 AND 12
                     AND substr({{column}}, 4, 2)::int BETWEEN 1 AND 12
                     AND substr({{column}}, 1, 2) <> substr({{column}}, 4, 2) THEN NULL
                ELSE substr({{column}}, 7, 4) || '-' ||
                     CASE WHEN substr({{column}}, 1, 2)::int > 12
                          THEN substr({{column}}, 4, 2) || '-' || substr({{column}}, 1, 2)
                          ELSE substr({{column}}, 1, 5)
                     END || substr({{column}}, 11)
            END
        ELSE {{column}} END
        """;

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

    private sealed record OrderListHeader(string ErpId, int Sid, string Platform,
        [property: Column(TypeName = "timestamp without time zone")] DateTime? CreatedAt,
        DateOnly? CreatedDate,
        [property: Column(TypeName = "timestamp without time zone")] DateTime? DispatchedAt,
        string? CreatedRaw, string? DispatchedRaw);
}
