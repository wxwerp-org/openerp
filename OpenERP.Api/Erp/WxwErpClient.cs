using System.Net.Http.Json;
using System.Text.Json;
using OpResult;

namespace OpenERP.Api.Erp;

public sealed partial class WxwErpClient(HttpClient http, IConfiguration configuration) : IErpClient
{
    // 本期接入已确认的站点，不接受浏览器提交任意目标地址或凭证。
    private const string BaseUrl = "https://v3-api.wxwerp.com";
    private string Token => configuration["Erp:AccessToken"]?.Trim() ?? "";
    public ErpConfiguration Configuration => new(!string.IsNullOrEmpty(Token), BaseUrl);

    public async Task<(OpResult<ShopPage> Result, int Status)> GetShopsAsync(int page, int pageSize, CancellationToken ct)
    {
        var (result, statusCode) = await GetShopListAsync(page, pageSize, ct);
        if (result.IsErr)
            return (result.Error, statusCode);

        var list = result.Value;
        using var limit = new SemaphoreSlim(4);
        var withStatus = await Task.WhenAll(list.Items.Select(async shop =>
        {
            await limit.WaitAsync(ct);
            try
            {
                var (status, _) = await PostAsync("/open-api/shop/get_status", new { sid = shop.Sid }, ct);
                if (status.IsErr)
                    return shop with { StatusError = status.Error.Message };
                if (status.Value.TryGetProperty("data", out var statusData) &&
                    statusData.ValueKind == JsonValueKind.Object &&
                    statusData.TryGetProperty("isTokenOk", out var tokenOk) &&
                    tokenOk.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    return shop with { IsTokenOk = tokenOk.GetBoolean() };

                return shop with { StatusError = "ERP 授权状态响应格式不正确。" };
            }
            finally
            {
                limit.Release();
            }
        }));
        return (list with { Items = withStatus }, statusCode);
    }

    public async Task<OpResult<IReadOnlyList<ErpShop>>> GetAllShopsAsync(CancellationToken ct)
    {
        const int pageSize = 100;
        const int maxPages = 100;
        var shops = new List<ErpShop>();
        for (var page = 1; ; page++)
        {
            if (page > maxPages)
                return OpResults.Err("ERP 店铺数量超出选店接口可读取范围。");

            var (result, _) = await GetShopListAsync(page, pageSize, ct);
            if (result.IsErr)
                return result.Error;

            var list = result.Value;
            shops.AddRange(list.Items);
            if (page >= list.PageCount)
                break;
        }

        return OpResults.Ok<IReadOnlyList<ErpShop>>(shops.DistinctBy(shop => shop.Sid).ToArray());
    }

    private async Task<(OpResult<ShopPage> Result, int Status)> GetShopListAsync(int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return (OpResults.Err("页码必须大于 0，每页数量必须在 1 至 100 之间。"), 400);
        if (!Configuration.Configured)
            return (OpResults.Err("尚未配置私有 ERP AccessToken。"), 503);

        var (response, status) = await PostAsync("/open-api/shop/get_shops", new
        {
            page, page_size = pageSize, platform = "TemuPop"
        }, ct);
        if (response.IsErr)
            return (response.Error, status);

        var root = response.Value;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array ||
            !TryCount(root, "data_count", out var totalCount) || !TryCount(root, "page_count", out var pageCount))
            return (OpResults.Err("ERP 店铺列表响应格式不正确。"), 502);

        var shops = new List<ErpShop>();
        foreach (var item in items.EnumerateArray())
        {
            var sid = Text(item, "sid");
            var platform = Text(item, "platform");
            if (!int.TryParse(sid, out var id) || id <= 0 || platform != "TemuPop")
                return (OpResults.Err("ERP 返回了无效或不属于 Temu 半托管的店铺。"), 502);
            shops.Add(new(sid, Text(item, "name") ?? "", platform, Text(item, "region"), null, null));
        }

        return (new ShopPage(shops, page, pageSize, totalCount, pageCount), 200);
    }

    private async Task<(OpResult<JsonElement> Result, int Status)> PostAsync(string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path);
        if (!request.Headers.TryAddWithoutValidation("AccessToken", Token) || Token.Contains('\r') || Token.Contains('\n'))
            return (OpResults.Err("ERP AccessToken 配置格式不正确。"), 503);
        request.Content = JsonContent.Create(body);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return (OpResults.Err($"ERP 接口返回 HTTP {(int)response.StatusCode}，请检查凭证权限及服务状态。"), 502);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var success) ||
                success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return (OpResults.Err("ERP 接口未返回有效的业务结果。"), 502);
            if (!success.GetBoolean())
            {
                // 不透传上游原始响应，防止把凭证或内部信息带到浏览器。
                var message = Text(root, "message");
                if (path == OrderTimePath && message is "_暂未找到信息" or "暂未找到信息")
                    return (OpResults.Err("ERP 未找到订单或时间记录。"), 404);

                return (OpResults.Err(message switch
                {
                    "AccessToken invalid" or "AccessToken disabled" or "AccessToken expired" or "AccessToken is empty"
                        => "ERP AccessToken 无效、停用或过期，请检查服务端配置。",
                    "AccessToken scope denied" => "ERP AccessToken 没有此接口的访问权限。",
                    _ => "ERP 拒绝了请求，请检查 Token 权限、账号额度及店铺访问范围。"
                }), 502);
            }
            return (root.Clone(), 200);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (OpResults.Err("ERP 请求超时，请重试。"), 504);
        }
        catch (Polly.Timeout.TimeoutRejectedException)
        {
            return (OpResults.Err("ERP 请求超时，请重试。"), 504);
        }
        catch (HttpRequestException)
        {
            return (OpResults.Err("无法连接私有 ERP，请检查网络后重试。"), 502);
        }
        catch (JsonException)
        {
            return (OpResults.Err("ERP 返回了无法解析的响应。"), 502);
        }
    }

    private static string? Text(JsonElement item, string key) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryCount(JsonElement item, string key, out int value)
    {
        value = 0;
        return item.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out value) && value >= 0;
    }
}
