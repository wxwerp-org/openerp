using System.Globalization;
using System.Text.Json;
using OpResult;
using OpenERP.Api.Orders;

namespace OpenERP.Api.Erp;

public sealed partial class WxwErpClient
{
    private const string OrderTimePath = "/open-api/order_detail/get_time";
    private const string TemuPop = "TemuPop";
    private const int MaxOrderPageSize = 500;
    private const int MaxConcurrentOrderDetails = 8;
    private static readonly SemaphoreSlim OrderDetailSlots = new(MaxConcurrentOrderDetails, MaxConcurrentOrderDetails);

    public async Task<OpResult<OrderImportBatch>> ReadOrdersAsync(
        int sid, Action<OrderReadProgress>? report, ErpAccessToken accessToken, CancellationToken ct)
    {
        const int shopPageSize = 100;
        var found = false;
        for (var page = 1; ; page++)
        {
            var (shops, _) = await GetShopsAsync(page, shopPageSize, TemuPop, accessToken, ct);
            if (shops.IsErr)
                return shops.Error;

            var list = shops.Value;
            found = list.Items.Any(shop => int.Parse(shop.Sid, CultureInfo.InvariantCulture) == sid);
            if (found || page >= list.PageCount)
                break;
        }

        if (!found)
            return OpResults.Err("当前 ERP AccessToken 无法访问该 TemuPop 店铺。");

        var scan = await ReadOrderIndexAsync(sid, MaxOrderPageSize, report, accessToken, ct);
        if (scan.IsErr)
            return scan.Error;

        var index = scan.Value;
        var orders = new List<OrderSnapshot>(index.Orders.Count);
        var skips = new Dictionary<string, int>(index.SkipReasons);
        var processed = index.Total - index.Orders.Count;
        report?.Invoke(new("details", index.Total, index.Total, processed));
        using var detailCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pending = new Queue<Task<OpResult<OrderReadItem>>>(MaxConcurrentOrderDetails);
        var next = 0;
        try
        {
            for (var current = 0; current < index.Orders.Count; current++)
            {
                // 读取可以重叠，按列表顺序合并结果并报告进度。
                while (pending.Count < MaxConcurrentOrderDetails && next < index.Orders.Count)
                {
                    ct.ThrowIfCancellationRequested();
                    pending.Enqueue(ReadOrderWithPermitAsync(accessToken, sid, index.Orders[next++], detailCancellation.Token));
                }

                var detail = await pending.Dequeue();
                if (detail.IsErr)
                    return detail.Error;

                var result = detail.Value;
                if (result.Order is { } order)
                    orders.Add(order);
                else if (result.SkipReason is { } reason)
                    CountSkip(skips, reason);

                report?.Invoke(new("details", index.Total, index.Total, ++processed));
            }
        }
        finally
        {
            if (pending.Count > 0)
            {
                await detailCancellation.CancelAsync();
                // 等待已经发起的读取退出，保留最先处理到的失败。
                Task remaining = Task.WhenAll(pending);
                await remaining.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        return new OrderImportBatch(index.Total, orders, skips);
    }

    public async Task<OpResult<OrderIndex>> ReadOrderIndexAsync(
        int sid, int pageSize, Action<OrderReadProgress>? report, ErpAccessToken accessToken, CancellationToken ct)
    {
        if (sid <= 0 || pageSize is < 1 or > MaxOrderPageSize)
            return OpResults.Err("店铺或分页参数无效。");

        var filters = new
        {
            status = "all",
            gid = "all",
            shops = new { values = new[] { new { items = new[] { new { key = sid.ToString(CultureInfo.InvariantCulture) } } } } },
            platforms = new[] { 40 }
        };
        const string countPath = "/open-api/order/get_list_page_count";
        var countRequest = new { filters, page_size = pageSize };
        var (before, _) = await PostAsync(accessToken, countPath, countRequest, ct);
        if (before.IsErr)
            return before.Error;
        if (!TryReadPageCount(before.Value, pageSize, out var total, out var pages))
            return OpResults.Err("ERP 订单分页统计格式不正确。");

        var orders = new List<ErpOrderHeader>();
        var ids = new List<string>();
        var skips = new Dictionary<string, int>();
        report?.Invoke(new("pages", total, 0, 0));

        for (var page = 1; page <= pages; page++)
        {
            var (response, _) = await PostAsync(accessToken, "/open-api/order/get_list", new
            {
                filters, page, page_size = pageSize, sort = new { field = "w_order._id", order = "desc" }
            }, ct);
            if (response.IsErr)
                return response.Error;
            if (!TryField(response.Value, "data", JsonValueKind.Object, out var data) ||
                !TryField(data, "orders", JsonValueKind.Array, out var rows) ||
                rows.GetArrayLength() != Math.Min(pageSize, total - ids.Count))
                return OpResults.Err("ERP 订单分页返回条数与统计不一致。");

            foreach (var row in rows.EnumerateArray())
            {
                var id = Text(row, "id")?.ToLowerInvariant();
                var number = Text(row, "orderid");
                if (id is not { Length: 24 } || !id.All(char.IsAsciiHexDigit) || string.IsNullOrWhiteSpace(number))
                    return OpResults.Err("ERP 订单标识格式不正确。");
                if (ids.Count > 0 && string.CompareOrdinal(ids[^1], id) <= 0)
                    return OpResults.Err("ERP 订单分页出现重复 ID 或排序不一致。");

                ids.Add(id);
                if (!TryReadPlatformOrders(row, sid, out var pos, out var reason))
                    return OpResults.Err("ERP 订单列表的平台子单状态格式不正确。");
                if (reason is not null)
                {
                    CountSkip(skips, reason);
                    continue;
                }

                if (!TryReadTimes(row, "time", "list", out var times))
                    return OpResults.Err("ERP 订单列表的时间格式不正确。");

                var (incomeCost, incomeCurrency) = ReadIncome(row);
                orders.Add(new(id, number, times, pos, incomeCost, incomeCurrency));
            }

            report?.Invoke(new("pages", total, ids.Count, 0));
        }

        var (after, _) = await PostAsync(accessToken, countPath, countRequest, ct);
        if (after.IsErr)
            return after.Error;
        if (!TryReadPageCount(after.Value, pageSize, out var afterTotal, out var afterPages) ||
            afterTotal != total || afterPages != pages || ids.Count != total)
            return OpResults.Err("ERP 订单分页统计发生变化，请重试同步。");

        return new OrderIndex(total, ids, orders, skips);
    }

    private async Task<OpResult<OrderReadItem>> ReadOrderWithPermitAsync(
        ErpAccessToken accessToken, int sid, ErpOrderHeader header, CancellationToken ct)
    {
        await OrderDetailSlots.WaitAsync(ct);
        try
        {
            return await ReadOrderAsync(accessToken, sid, header, ct);
        }
        finally
        {
            OrderDetailSlots.Release();
        }
    }

    private async Task<OpResult<OrderReadItem>> ReadOrderAsync(
        ErpAccessToken accessToken, int sid, ErpOrderHeader header, CancellationToken ct)
    {
        var detail = await ReadOrderDetailAsync(accessToken, sid, header, ct);
        if (detail.IsErr || detail.Value.Order is not { } order || order.Times.Created is not null)
            return detail;

        var (timeResult, timeStatus) = await PostAsync(accessToken, OrderTimePath, new { id = header.ErpId }, ct);
        if (timeResult.IsErr)
        {
            // 时间记录缺失和主单已消失的消息相同，重新读取并校验详情后才能保存。
            if (timeStatus == 404)
                return await ReadOrderDetailAsync(accessToken, sid, header, ct);

            return timeResult.Error;
        }

        if (!TryField(timeResult.Value, "data", JsonValueKind.Object, out var timeData) ||
            !TryField(timeData, "times", JsonValueKind.Array, out _) ||
            !TryReadTimes(timeData, "date", "detail", out var fallback))
            return OpResults.Err("ERP 订单时间详情格式不正确。");

        var times = order.Times;
        return new OrderReadItem(order with
        {
            Times = new(fallback.Created, times.Paid ?? fallback.Paid,
                times.Dispatched ?? fallback.Dispatched, times.Refunded ?? fallback.Refunded)
        }, null);
    }

    private async Task<OpResult<OrderReadItem>> ReadOrderDetailAsync(
        ErpAccessToken accessToken, int sid, ErpOrderHeader header, CancellationToken ct)
    {
        var (response, _) = await PostAsync(accessToken, "/open-api/order_detail/get_base",
            new { id = header.ErpId, ask = false, showAllProduct = true }, ct);
        if (response.IsErr)
            return response.Error;
        if (!TryField(response.Value, "data", JsonValueKind.Object, out var data) ||
            Text(data, "orderId") != header.OrderNumber ||
            !TryField(data, "shop", JsonValueKind.Object, out var shop) ||
            !TryField(data, "status", JsonValueKind.Object, out var status) ||
            Text(status, "key") is not { Length: > 0 } statusCode ||
            !TryField(data, "platformOrders", JsonValueKind.Array, out var platformOrders) ||
            !TryField(data, "products", JsonValueKind.Array, out var products))
            return OpResults.Err("ERP 订单详情格式或编号不匹配。");

        if (!TrySid(shop, out var detailSid) || detailSid != sid || Text(shop, "platform") != TemuPop)
            return new OrderReadItem(null, "订单详情店铺或平台与目标不符");

        var poCodes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var po in platformOrders.EnumerateArray())
        {
            if (Text(po, "oid") is not { Length: > 0 } poNumber)
                return new OrderReadItem(null, "订单详情缺少平台子单标识");

            if (!TryOptionalField(po, "status", JsonValueKind.Object, out var poStatus) ||
                (poStatus.ValueKind == JsonValueKind.Object &&
                 !TryOptionalField(poStatus, "key", JsonValueKind.String, out _)))
                return OpResults.Err("ERP 订单详情的平台子单状态格式不正确。");

            var code = Text(poStatus, "key");
            if (!poCodes.TryAdd(poNumber, code) && poCodes[poNumber] != code)
                return OpResults.Err("ERP 订单详情的同一平台子单返回了不同状态。");
        }

        if (poCodes.Count != header.PlatformOrders.Count || header.PlatformOrders.Any(po => !poCodes.ContainsKey(po.PoNumber)))
            return new OrderReadItem(null, "订单列表和详情的平台子单归属不一致");

        var lines = new List<OrderLineSnapshot>(products.GetArrayLength());
        foreach (var product in products.EnumerateArray())
        {
            if (!TryField(product, "qty", JsonValueKind.Number, out var quantity) || !quantity.TryGetInt32(out var qty))
                return OpResults.Err("ERP 订单商品数量格式不正确。");

            if (!TryOptionalField(product, "opid", JsonValueKind.String, out _) ||
                !TryOptionalField(product, "oguid", JsonValueKind.String, out _) ||
                !TryOptionalField(product, "skuId", JsonValueKind.String, out _) ||
                !TryOptionalField(product, "skuCode", JsonValueKind.String, out _) ||
                !TryOptionalBoolean(product, "enabled", out var disabled) ||
                !TryOptionalBoolean(product, "append", out var appended))
                return OpResults.Err("ERP 订单商品标识或状态格式不正确。");

            // ERP 详情的 enabled 字段直接来自源商品行的 disabled。
            lines.Add(new(Text(product, "opid"), Text(product, "oguid"), Text(product, "skuId"), Text(product, "skuCode"),
                qty, disabled, appended));
        }

        var pos = header.PlatformOrders.Select(po => po with { StatusCode = poCodes[po.PoNumber] }).ToArray();
        return new OrderReadItem(new(header.ErpId, header.OrderNumber, sid, TemuPop, statusCode,
            header.Times, pos, lines, header.IncomeCost, header.IncomeCurrency), null);
    }

    private static (decimal? Cost, string? Currency) ReadIncome(JsonElement row)
    {
        try
        {
            if (!TryField(row, "amounts", JsonValueKind.Array, out var amounts) || amounts.GetArrayLength() == 0)
                return default;

            var selected = amounts[0];
            var currency = Text(selected, "currency");
            for (var index = 1; index < amounts.GetArrayLength(); index++)
            {
                // 首条为订单原币，之后首个有效的不同币种为 ERP 成本币种。
                var candidate = amounts[index];
                var candidateCurrency = Text(candidate, "currency");
                if (string.IsNullOrWhiteSpace(candidateCurrency) || candidateCurrency == currency)
                    continue;

                selected = candidate;
                currency = candidateCurrency;
                break;
            }

            var cost = ParseIncomeAmount(Text(selected, "amount"));
            return (cost, string.IsNullOrWhiteSpace(currency) ? null : currency);
        }
        catch (InvalidOperationException)
        {
            // 可选金额的 JSON 文本无法解码时，仍保留订单其余字段。
            return default;
        }
    }

    private static decimal? ParseIncomeAmount(string? amount)
    {
        const decimal maxIncomeAmount = 9999999999999999.99m;
        var text = amount.AsSpan().Trim();
        var decimalPoint = text.IndexOf('.');
        var integer = decimalPoint < 0 ? text : text[..decimalPoint];
        var fraction = decimalPoint < 0 ? ReadOnlySpan<char>.Empty : text[(decimalPoint + 1)..];
        if (fraction.Length > 2 || integer.IndexOfAnyExceptInRange('0', '9') >= 0 ||
            fraction.IndexOfAnyExceptInRange('0', '9') >= 0)
            return null;

        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) &&
            value <= maxIncomeAmount ? value : null;
    }

    private static bool TryReadPlatformOrders(JsonElement row, int sid,
        out IReadOnlyList<PlatformOrderSnapshot> result, out string? skipReason)
    {
        result = [];
        skipReason = null;
        if (!TryField(row, "sub_orders", JsonValueKind.Array, out var children) || children.GetArrayLength() == 0)
        {
            skipReason = "列表缺少平台子单";
            return true;
        }

        var pos = new Dictionary<string, PlatformOrderSnapshot>(StringComparer.Ordinal);
        foreach (var child in children.EnumerateArray())
        {
            var number = Text(child, "id");
            if (string.IsNullOrWhiteSpace(number) || !TrySid(child, out var childSid))
            {
                skipReason = "平台子单缺少 PO 号或 SID";
                return true;
            }
            if (childSid != sid)
            {
                skipReason = "平台子单不属于目标店铺";
                return true;
            }

            if (!TryOptionalField(child, "status", JsonValueKind.String, out var status))
                return false;

            var snapshot = new PlatformOrderSnapshot(number, childSid, null,
                status.ValueKind == JsonValueKind.String ? status.GetString() : null);
            if (!pos.TryAdd(number, snapshot) && pos[number] != snapshot)
            {
                skipReason = "列表中的同一 PO 返回了不同信息";
                return true;
            }
        }

        result = pos.Values.ToArray();
        return true;
    }

    private static bool TryReadTimes(JsonElement order, string valueField, string source, out OrderTimes result)
    {
        result = new(null, null, null, null);
        if (!TryOptionalField(order, "times", JsonValueKind.Array, out var rows))
            return false;
        if (rows.ValueKind != JsonValueKind.Array)
            return true;

        var values = new Dictionary<string, OrderTimeValue>(StringComparer.Ordinal);
        foreach (var row in rows.EnumerateArray())
        {
            if (Text(row, "name") is not { } name)
                return false;
            var label = name.TrimStart('_');
            if (label is not ("创建" or "付款" or "出库" or "退款"))
                continue;

            if (!TryOptionalField(row, valueField, JsonValueKind.String, out var raw))
                return false;
            if (raw.ValueKind == JsonValueKind.String &&
                raw.GetString() is { } value && !string.IsNullOrWhiteSpace(value) && value.Trim() != "-")
                values.TryAdd(label, new(value, source));
        }

        result = new(values.GetValueOrDefault("创建"), values.GetValueOrDefault("付款"),
            values.GetValueOrDefault("出库"), values.GetValueOrDefault("退款"));
        return true;
    }

    private static bool TryReadPageCount(JsonElement root, int pageSize, out int total, out int pages)
    {
        total = pages = 0;
        return TryField(root, "data", JsonValueKind.Object, out var data) &&
            TryCount(data, "data_count", out total) && TryCount(data, "page_count", out pages) &&
            pages == ((long)total + pageSize - 1) / pageSize;
    }

    private static bool TryField(JsonElement node, string field, JsonValueKind kind, out JsonElement value)
    {
        value = default;
        return node.ValueKind == JsonValueKind.Object && node.TryGetProperty(field, out value) && value.ValueKind == kind;
    }

    private static bool TrySid(JsonElement node, out int sid)
    {
        sid = 0;
        return (TryField(node, "sid", JsonValueKind.Number, out var value)
            ? value.TryGetInt32(out sid)
            : int.TryParse(Text(node, "sid"), NumberStyles.None, CultureInfo.InvariantCulture, out sid)) && sid > 0;
    }

    private static bool TryOptionalField(JsonElement node, string key, JsonValueKind kind, out JsonElement value) =>
        !node.TryGetProperty(key, out value) || value.ValueKind == JsonValueKind.Null || value.ValueKind == kind;

    private static bool TryOptionalBoolean(JsonElement node, string key, out bool? value)
    {
        value = null;
        if (!node.TryGetProperty(key, out var field) || field.ValueKind == JsonValueKind.Null)
            return true;
        if (field.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;

        value = field.GetBoolean();
        return true;
    }

    private static void CountSkip(Dictionary<string, int> skips, string reason) =>
        skips[reason] = skips.GetValueOrDefault(reason) + 1;

    private sealed record OrderReadItem(OrderSnapshot? Order, string? SkipReason);
}
