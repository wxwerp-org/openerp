using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenERP.Api.Erp;
using OpResult;

namespace OpenERP.Api.Orders.Sources;

/// <summary>
/// 通过旺销王「平台接口」代理读取 AliExpress 卖家订单：只传店铺 Sid，店铺授权与应用密钥由旺销王托管。
/// 订单列表接口 <c>aliexpress.trade.seller.orderlist.get</c> 已包含子订单（商品、数量、SKU），不再逐单读取详情。
/// 参数、响应字段以旺销王的平台接口文档为准（<c>apiSchemas/platform/aliexpress</c>）。
/// </summary>
public sealed class AliExpressOrderSource(
    IPlatformAgentApiClient agent, IErpTokenStore tokens, TimeProvider time,
    ILogger<AliExpressOrderSource>? logger = null) : IPlatformOrderSource
{
    public const string PlatformName = "AliExpress";

    private const string AgentPlatform = "aliexpress";
    private const string ListApi = "aliexpress.trade.seller.orderlist.get";
    private const int PageSize = 50;
    private const int LookbackDays = 180;
    private const string TimeSource = "AliExpress";

    private static readonly TimeZoneInfo PacificTime = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    public string Platform => PlatformName;

    public async Task<OpResult<OrderImportBatch>> ReadOrdersAsync(
        PlatformShopRef shop, Action<OrderReadProgress>? report, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAsync(cancellationToken);
        if (token.IsErr)
            return token.Error;

        var accessToken = token.Value;
        report?.Invoke(new("pages", 0, 0, 0));
        // 平台的创建时间区间使用美国太平洋时间。
        var end = TimeZoneInfo.ConvertTime(time.GetUtcNow(), PacificTime).DateTime;
        var start = end.AddDays(-LookbackDays);

        var skipped = new Dictionary<string, int>();
        var orders = new List<OrderSnapshot>();
        var total = 0;
        var read = 0;
        for (var page = 1; ; page++)
        {
            var query = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["current_page"] = page,
                ["page_size"] = PageSize,
                ["create_date_start"] = Format(start),
                ["create_date_end"] = Format(end)
            });
            var response = await agent.InvokeAsync(AgentPlatform, shop.Sid, ListApi,
                new Dictionary<string, string> { ["param_aeop_order_query"] = query }, accessToken, cancellationToken);
            if (response.IsErr)
                return response.Error;

            var result = Result(response.Value);
            if (result is null)
                return OpResults.Err(PlatformError(response.Value) ?? "AliExpress 订单列表响应格式不正确。");
            if (Bool(result.Value, "success") == false)
                return OpResults.Err(
                    $"AliExpress 订单列表查询失败：{Text(result.Value, "error_message") ?? Text(result.Value, "error_code") ?? "未知错误"}");

            total = Number(result.Value, "total_count") ?? total;
            var rows = Array(result.Value, "target_list");
            foreach (var row in rows)
            {
                read++;
                var order = ToSnapshot(row, shop, out var skipReason);
                if (order is null)
                {
                    skipped[skipReason!] = skipped.GetValueOrDefault(skipReason!) + 1;
                    // 只记录字段名，不记录订单内容，便于对照平台实际返回的结构。
                    logger?.LogWarning("AliExpress 订单被跳过 {Sid} {Reason}，订单字段 {Fields}，product_list {ProductShape}",
                        shop.Sid, skipReason, FieldNames(row), Shape(row, "product_list"));
                }
                else
                    orders.Add(order);
            }

            report?.Invoke(new("pages", Math.Max(total, read), read, read));
            var totalPages = Number(result.Value, "total_page");
            if (rows.Count == 0 || (totalPages is null ? rows.Count < PageSize : page >= totalPages))
                break;
        }

        report?.Invoke(new("details", read, read, read));
        return new OrderImportBatch(read, orders, skipped);
    }

    private static OrderSnapshot? ToSnapshot(JsonElement row, PlatformShopRef shop, out string? skipReason)
    {
        skipReason = null;
        var orderId = Text(row, "order_id");
        if (orderId is null)
        {
            skipReason = "列表缺少订单号";
            return null;
        }

        var lines = new List<OrderLineSnapshot>();
        foreach (var product in Array(row, "product_list"))
        {
            var quantity = Number(product, "product_count");
            if (quantity is null or < 0)
            {
                skipReason = "商品数量格式不正确";
                return null;
            }

            lines.Add(new(Text(product, "product_id"), Text(product, "child_id"), null, Text(product, "sku_code"),
                quantity.Value, null, null, Text(product, "product_name"), Text(product, "product_img_url"),
                Text(product, "product_standard"), Price(product, out var currency), currency,
                Text(product, "logistics_service_name"), Details(product, LineLabels, LineConsumed)));
        }

        if (lines.Count == 0)
        {
            skipReason = "列表缺少商品行";
            return null;
        }

        var status = Text(row, "order_status") ?? "未知";
        var total = Amount(row, out var totalCurrency);
        if (total is null && lines.All(line => line.UnitPrice is not null))
        {
            // 列表没有给出订单总额时，用商品单价 × 数量汇总，币种取自商品行。
            total = lines.Sum(line => line.UnitPrice!.Value * line.Quantity);
            totalCurrency = lines.Select(line => line.Currency).FirstOrDefault(currency => currency is not null);
        }

        return new OrderSnapshot(
            $"{PlatformName}:{shop.Sid}:{orderId}", orderId, shop.Sid, PlatformName, status,
            new OrderTimes(TimeValue(Text(row, "gmt_create")), TimeValue(Text(row, "gmt_pay_time")),
                TimeValue(Text(row, "gmt_send_goods_time")), null),
            [new PlatformOrderSnapshot(orderId, shop.Sid, status, null)], lines,
            Buyer: Text(row, "buyer_login_id") ?? Text(row, "buyer_signer_fullname"),
            TotalAmount: total, TotalCurrency: totalCurrency,
            Logistics: Text(row, "logistics_service_name") ?? lines.Select(line => line.Logistics).FirstOrDefault(name => name is not null),
            Details: Details(row, OrderLabels, OrderConsumed));
    }

    // 平台字段到展示名称的对照；未列出的标量字段按原字段名保留，避免信息丢失。
    // 分组沿用平台无关的词汇：buyer 买家 / payment 金额与支付 / logistics 物流 / status 状态 / time 时间 / other 其他。
    private static readonly Dictionary<string, (string Label, string Group)> OrderLabels = new()
    {
        ["buyer_signer_fullname"] = ("收货人", "buyer"),
        ["payment_type"] = ("支付方式", "payment"),
        ["fund_status"] = ("资金状态", "payment"),
        ["loan_status"] = ("放款状态", "payment"),
        ["frozen_status"] = ("冻结状态", "payment"),
        ["escrow_fee_rate"] = ("手续费率", "payment"),
        ["logistics_status"] = ("物流状态", "logistics"),
        ["issue_status"] = ("纠纷状态", "status"),
        ["seller_signer_fullname"] = ("卖家", "other"),
        ["seller_operator_login_id"] = ("卖家账号", "other"),
        ["gmt_update"] = ("更新时间", "time"),
        ["gmt_modified"] = ("更新时间", "time"),
        ["biz_type"] = ("业务类型", "other")
    };

    private static readonly HashSet<string> OrderConsumed =
    [
        "order_id", "order_status", "gmt_create", "gmt_pay_time", "gmt_send_goods_time", "buyer_login_id",
        "logistics_service_name", "product_list", "order_detail_url", "pay_amount", "order_amount", "total_amount",
        "total_pay_amount"
    ];

    private static readonly Dictionary<string, (string Label, string Group)> LineLabels = new()
    {
        // price：紧跟在商品单价后面展示的费用（如物流费用）
        ["logistics_amount"] = ("物流费用", "price"),
        ["fund_status"] = ("资金状态", "payment"),
        ["logistics_type"] = ("物流类型", "logistics"),
        ["goods_prepare_time"] = ("备货期（天）", "logistics"),
        ["freight_commit_day"] = ("承诺时效（天）", "logistics"),
        ["delivery_time"] = ("送达时效", "logistics"),
        ["send_goods_time"] = ("发货时间", "logistics"),
        ["send_goods_operator"] = ("产品状态", "status"),
        ["son_order_status"] = ("子订单状态", "status"),
        ["show_status"] = ("展示状态", "status"),
        ["issue_status"] = ("纠纷状态", "status"),
        ["can_submit_issue"] = ("可提交纠纷", "status"),
        ["money_back3x"] = ("3 天退款", "status"),
        ["memo"] = ("备注", "other"),
        ["product_unit"] = ("单位", "other"),
        // more：机器可读的原始内容，只在「更多信息」里点击后展示
        ["product_snap_url"] = ("商品快照", "more"),
        ["extend_map"] = ("扩展信息", "more")
    };

    // 商品小计与商品行的「单价 × 数量」重复，不再单独保存。
    private static readonly HashSet<string> LineConsumed =
    [
        "product_id", "child_id", "order_id", "sku_code", "product_count", "product_name", "product_img_url",
        "product_standard", "product_unit_price", "logistics_service_name", "product_snapshot_url",
        "total_product_amount"
    ];

    /// <summary>把对象上未被单独建模的标量字段整理为「名称 + 取值」的 JSON 数组文本，没有内容时为 null。</summary>
    private static string? Details(
        JsonElement element, Dictionary<string, (string Label, string Group)> labels, HashSet<string> consumed)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        var known = new List<(int Index, OrderDetail Detail)>();
        var unknown = new List<OrderDetail>();
        var keys = labels.Keys.ToList();
        foreach (var property in element.EnumerateObject())
        {
            if (consumed.Contains(property.Name))
                continue;
            var name = labels.TryGetValue(property.Name, out var label) ? label.Label : property.Name;
            var group = label.Group ?? "more";
            // 嵌套对象 / 内嵌 JSON 的字符串展开成「路径 + 取值」，统一放进「更多信息」；金额对象仍按金额处理。
            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "是",
                JsonValueKind.False => "否",
                JsonValueKind.Object => Money(property.Value),
                _ => null
            };
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array && value is null
                || value is not null && LooksLikeJson(value))
            {
                Flatten(name, property.Value, unknown, 0);
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (label.Label is not null)
                known.Add((keys.IndexOf(property.Name), new(name, value.Trim(), group)));
            else
                unknown.Add(new(name, value.Trim(), "more"));
        }

        // 已知字段在前，按对照表顺序；其余保持来源顺序。
        List<OrderDetail> all = [.. known.OrderBy(item => item.Index).Select(item => item.Detail), .. unknown];
        return all.Count == 0
            ? null
            : JsonSerializer.Serialize(all, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static bool LooksLikeJson(string value) => value.TrimStart() is ['{' or '[', ..];

    /// <summary>
    /// 把嵌套对象、数组或内嵌 JSON 字符串展开为「路径 + 取值」条目（如 <c>扩展信息.length</c>），分组为 more。
    /// 最多展开 4 层，空对象、空值忽略。
    /// </summary>
    private static void Flatten(string path, JsonElement element, List<OrderDetail> into, int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object when depth < 4:
                foreach (var property in element.EnumerateObject())
                    Flatten($"{path}.{property.Name}", property.Value, into, depth + 1);
                break;
            case JsonValueKind.Array when depth < 4:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    Flatten(item.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? $"{path}[{index++}]" : path, item, into, depth + 1);
                break;
            case JsonValueKind.String:
                var text = element.GetString();
                if (text is not null && LooksLikeJson(text) && depth < 4)
                {
                    try
                    {
                        using var nested = JsonDocument.Parse(text);
                        Flatten(path, nested.RootElement.Clone(), into, depth + 1);
                        break;
                    }
                    catch (JsonException)
                    {
                    }
                }

                if (!string.IsNullOrWhiteSpace(text))
                    into.Add(new(path, text.Trim(), "more"));
                break;
            case JsonValueKind.Number:
                into.Add(new(path, element.GetRawText(), "more"));
                break;
            case JsonValueKind.True or JsonValueKind.False:
                into.Add(new(path, element.GetBoolean() ? "是" : "否", "more"));
                break;
        }
    }

    /// <summary>形如 <c>{amount, currency_code}</c> 的金额对象转为「币种 数额」文本；其他对象返回 null。</summary>
    private static string? Money(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || Text(value, "amount") is not { } amount)
            return null;
        var currency = Text(value, "currency_code") ?? Text(value, "currencyCode");
        return currency is null ? amount : $"{currency} {amount}";
    }

    /// <summary>订单总额：依次尝试平台常见的金额字段。</summary>
    private static decimal? Amount(JsonElement row, out string? currency)
    {
        currency = null;
        foreach (var name in new[] { "pay_amount", "order_amount", "total_amount", "total_pay_amount" })
        {
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty(name, out var value))
                continue;
            var amount = value;
            if (value.ValueKind == JsonValueKind.Object)
            {
                currency = Text(value, "currency_code") ?? Text(value, "currencyCode");
                if (!value.TryGetProperty("amount", out amount))
                    continue;
            }

            if (amount.ValueKind == JsonValueKind.Number && amount.TryGetDecimal(out var number))
                return number;
            if (amount.ValueKind == JsonValueKind.String &&
                decimal.TryParse(amount.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number))
                return number;
        }

        return null;
    }

    /// <summary>旺销王返回的平台原始响应可能带外层的 <c>aliexpress_..._response</c>，也可能已展开。</summary>
    private static JsonElement? Result(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object)
            return null;
        if (content.TryGetProperty("result", out var direct) && direct.ValueKind == JsonValueKind.Object)
            return direct;
        foreach (var property in content.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object &&
                property.Value.TryGetProperty("result", out var nested) && nested.ValueKind == JsonValueKind.Object)
                return nested;
        }

        return null;
    }

    private static string? PlatformError(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object || !content.TryGetProperty("error_response", out var error))
            return null;

        var code = Text(error, "code") ?? "unknown";
        var message = Text(error, "msg");
        return message is null ? $"AliExpress 返回错误 {code}" : $"AliExpress 返回错误 {code}：{message}";
    }

    private static string Format(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static OrderTimeValue? TimeValue(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : new OrderTimeValue(raw.Trim(), TimeSource);

    private static List<JsonElement> Array(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var items))
            return [];
        return items.ValueKind switch
        {
            JsonValueKind.Array => [.. items.EnumerateArray()],
            JsonValueKind.Object => Unwrap(items),
            _ => []
        };
    }

    /// <summary>平台的列表字段有时再包一层只含一个数组的对象（例如 order_product_dto），展开成数组。</summary>
    private static List<JsonElement> Unwrap(JsonElement item)
    {
        var properties = item.EnumerateObject().ToArray();
        return properties.Length == 1 && properties[0].Value.ValueKind == JsonValueKind.Array
            ? [.. properties[0].Value.EnumerateArray()]
            : [item];
    }

    private static string FieldNames(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object ? string.Join(',', element.EnumerateObject().Select(property => property.Name)) : element.ValueKind.ToString();

    private static string Shape(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return "缺失";
        return value.ValueKind switch
        {
            JsonValueKind.Array => $"数组({value.GetArrayLength()}) " + (value.GetArrayLength() > 0 ? FieldNames(value[0]) : ""),
            JsonValueKind.Object => "对象 " + FieldNames(value),
            _ => value.ValueKind.ToString()
        };
    }

    private static string? Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    /// <summary>单价可能是 <c>{amount, currency_code}</c> 对象，也可能直接是数字或字符串。</summary>
    private static decimal? Price(JsonElement product, out string? currency)
    {
        currency = null;
        if (product.ValueKind != JsonValueKind.Object || !product.TryGetProperty("product_unit_price", out var price))
            return null;
        var amount = price;
        if (price.ValueKind == JsonValueKind.Object)
        {
            currency = Text(price, "currency_code") ?? Text(price, "currencyCode");
            if (!price.TryGetProperty("amount", out amount))
                return null;
        }

        return amount.ValueKind switch
        {
            JsonValueKind.Number when amount.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(amount.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static bool? Bool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static int? Number(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }
}
