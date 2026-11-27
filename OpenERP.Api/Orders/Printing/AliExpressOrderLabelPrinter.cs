using System.Text.Json;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders.Sources;
using OpResult;

namespace OpenERP.Api.Orders.Printing;

/// <summary>
/// AliExpress 面单打印（已用真实店铺联调通过），说明见 docs/order-label-printing.md。
/// 面单要用「国际运单号」，订单里没有，所以每个订单分两步：
/// 1. <c>aliexpress.logistics.querylogisticsorderdetail</c>：用交易单号查物流订单，拿到国际运单号。
/// 2. <c>aliexpress.logistics.redefining.getprintinfos</c>：用国际运单号取面单，返回 Base64 的 PDF。
/// 注意：平台公告说第 1 步的接口会在 2027-03-23 下线，之前要改用 <c>aliexpress.asf.cb.package.query</c>
/// （参数 trade_order_id，运单号字段 tracking_number）。
/// </summary>
public sealed class AliExpressOrderLabelPrinter(IPlatformAgentApiClient agent, IErpTokenStore tokens) : IOrderLabelPrinter
{
    private const string AgentPlatform = "aliexpress";
    private const string LogisticsOrderApi = "aliexpress.logistics.querylogisticsorderdetail";
    private const string PrintApi = "aliexpress.logistics.redefining.getprintinfos";

    public string Platform => AliExpressOrderSource.PlatformName;

    public async Task<OpResult<OrderLabelBatch>> PrintLabelsAsync(
        PlatformShopRef shop, IReadOnlyList<string> platformOrderNumbers, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAsync(cancellationToken);
        if (token.IsErr)
            return token.Error;

        var accessToken = token.Value;
        var documents = new List<OrderLabelDocument>();
        var failures = new List<OrderLabelFailure>();
        foreach (var number in platformOrderNumbers)
        {
            if (!long.TryParse(number, out _))
            {
                failures.Add(new(number, "订单号不是 AliExpress 交易单号，无法查询物流订单。"));
                continue;
            }

            // 第 1 步：查物流订单，拿国际运单号。
            var logistics = await agent.InvokeAsync(AgentPlatform, shop.Sid, LogisticsOrderApi,
                new Dictionary<string, string> { ["trade_order_id"] = number, ["current_page"] = "1", ["page_size"] = "50" },
                accessToken, cancellationToken);
            if (logistics.IsErr)
            {
                failures.Add(new(number, logistics.Error.Message));
                continue;
            }

            var packages = FindPackages(logistics.Value);
            if (packages.Count == 0)
            {
                failures.Add(new(number, PlatformMessage(logistics.Value) ?? "该订单还没有线上发货的物流订单，无法打印面单。"));
                continue;
            }

            // 第 2 步：用国际运单号取面单。
            var query = JsonSerializer.Serialize(packages.Select(package => package.LogisticsOrderId is { } id
                ? new Dictionary<string, object> { ["international_logistics_id"] = package.TrackingNumber, ["id"] = id }
                : new Dictionary<string, object> { ["international_logistics_id"] = package.TrackingNumber }));
            var print = await agent.InvokeAsync(AgentPlatform, shop.Sid, PrintApi,
                new Dictionary<string, string> { ["print_detail"] = "true", ["warehouse_order_query_d_t_os"] = query },
                accessToken, cancellationToken);
            if (print.IsErr)
            {
                failures.Add(new(number, print.Error.Message));
                continue;
            }

            var pdf = FindPdf(print.Value);
            if (pdf is null)
                failures.Add(new(number, PlatformMessage(print.Value) ?? "平台未返回可打印的面单。"));
            else
                documents.Add(new(number, $"label-{number}.pdf", "application/pdf", pdf));
        }

        return new OrderLabelBatch(documents, failures);
    }

    internal sealed record Package(string TrackingNumber, long? LogisticsOrderId);

    /// <summary>
    /// 从物流订单查询响应里收集国际运单号。真实响应会多包几层
    /// （<c>result.result_list.aeop_logistics_order_detail_dto[]</c>），所以递归查找，不假设固定层级。
    /// </summary>
    internal static List<Package> FindPackages(JsonElement element)
    {
        var packages = new List<Package>();
        Collect(element, packages);
        return packages.DistinctBy(package => package.TrackingNumber, StringComparer.Ordinal).ToList();

        static void Collect(JsonElement node, List<Package> into)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    if (node.TryGetProperty("international_logistics_num", out var tracking) &&
                        tracking.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tracking.GetString()))
                    {
                        long? id = node.TryGetProperty("logistics_order_id", out var value) && value.ValueKind == JsonValueKind.Number &&
                            value.TryGetInt64(out var parsed) ? parsed : null;
                        into.Add(new(tracking.GetString()!.Trim(), id));
                        return;
                    }

                    foreach (var property in node.EnumerateObject())
                        Collect(property.Value, into);
                    return;
                case JsonValueKind.Array:
                    foreach (var item in node.EnumerateArray())
                        Collect(item, into);
                    return;
            }
        }
    }

    /// <summary>
    /// 在面单响应里找 PDF。真实响应的 <c>result</c> 是一段 JSON 字符串，里面的 <c>body</c> 是 Base64 的 PDF（解码后以 %PDF 开头）；
    /// 也兼容直接就是 Base64 的情况。
    /// </summary>
    internal static byte[]? FindPdf(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString()!.Trim();
                if (text.StartsWith('{') || text.StartsWith('['))
                {
                    try
                    {
                        using var inner = JsonDocument.Parse(text);
                        return FindPdf(inner.RootElement);
                    }
                    catch (JsonException)
                    {
                        return null;
                    }
                }

                try
                {
                    var bytes = Convert.FromBase64String(text);
                    return bytes.AsSpan().StartsWith("%PDF"u8) ? bytes : null;
                }
                catch (FormatException)
                {
                    return null;
                }
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    if (FindPdf(property.Value) is { } found)
                        return found;
                return null;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    if (FindPdf(item) is { } found)
                        return found;
                return null;
            default:
                return null;
        }
    }

    /// <summary>平台返回的业务错误文本（error_desc / error_message），没有则为 null。</summary>
    private static string? PlatformMessage(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "error_desc", "error_message" })
                if (element.TryGetProperty(name, out var text) && text.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(text.GetString()))
                    return text.GetString();

            foreach (var property in element.EnumerateObject())
                if (PlatformMessage(property.Value) is { } found)
                    return found;
        }

        return null;
    }
}
