using System.Net.Http.Json;
using System.Text.Json;

namespace OpenERP.Api.Erp;

public sealed class WxwErpClient(HttpClient http, IConfiguration configuration) : IErpClient
{
    // 本期接入已确认的站点，不接受浏览器提交任意目标地址或凭证。
    private const string BaseUrl = "https://v3-api.wxwerp.com";
    private string Token => configuration["Erp:AccessToken"]?.Trim() ?? "";
    public ErpConfiguration Configuration => new(!string.IsNullOrEmpty(Token), BaseUrl);

    public async Task<ErpResult<ShopPage>> GetShopsAsync(int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return new(null, "页码必须大于 0，每页数量必须在 1 至 100 之间。", 400);
        if (!Configuration.Configured)
            return new(null, "尚未配置私有 ERP AccessToken。", 503);

        var response = await PostAsync("/open-api/shop/get_shops", new
        {
            page, page_size = pageSize, platform = "TemuPop"
        }, ct);
        if (response.Value is not { } root)
            return new(null, response.Error, response.Status);

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array ||
            !TryCount(root, "data_count", out var totalCount) || !TryCount(root, "page_count", out var pageCount))
            return new(null, "ERP 店铺列表响应格式不正确。");

        var shops = new List<ErpShop>();
        foreach (var item in items.EnumerateArray())
        {
            var sid = Text(item, "sid");
            var platform = Text(item, "platform");
            if (!int.TryParse(sid, out var id) || id <= 0 || platform != "TemuPop")
                return new(null, "ERP 返回了无效或不属于 Temu 半托管的店铺。");
            shops.Add(new(sid, Text(item, "name") ?? "", platform, Text(item, "region"), null, null));
        }

        using var limit = new SemaphoreSlim(4);
        var withStatus = await Task.WhenAll(shops.Select(async shop =>
        {
            await limit.WaitAsync(ct);
            try
            {
                var status = await PostAsync("/open-api/shop/get_status", new { sid = shop.Sid }, ct);
                if (status.Value is { } body && body.TryGetProperty("data", out var statusData) &&
                    statusData.ValueKind == JsonValueKind.Object &&
                    statusData.TryGetProperty("isTokenOk", out var tokenOk) &&
                    tokenOk.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    return shop with { IsTokenOk = tokenOk.GetBoolean() };

                return shop with { StatusError = status.Error ?? "ERP 授权状态响应格式不正确。" };
            }
            finally
            {
                limit.Release();
            }
        }));
        return new(new(withStatus, page, pageSize, totalCount, pageCount));
    }

    private async Task<ErpResult<JsonElement?>> PostAsync(string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path);
        if (!request.Headers.TryAddWithoutValidation("AccessToken", Token) || Token.Contains('\r') || Token.Contains('\n'))
            return new(null, "ERP AccessToken 配置格式不正确。", 503);
        request.Content = JsonContent.Create(body);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return new(null, $"ERP 接口返回 HTTP {(int)response.StatusCode}，请检查凭证权限及服务状态。");
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var success) ||
                success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new(null, "ERP 接口未返回有效的业务结果。");
            if (!success.GetBoolean())
            {
                // 不透传上游原始响应，防止把凭证或内部信息带到浏览器。
                var message = Text(root, "message");
                return new(null, message switch
                {
                    "AccessToken invalid" or "AccessToken disabled" or "AccessToken expired" or "AccessToken is empty"
                        => "ERP AccessToken 无效、停用或过期，请检查服务端配置。",
                    "AccessToken scope denied" => "ERP AccessToken 没有此接口的访问权限。",
                    _ => "ERP 拒绝了请求，请检查 Token 权限、账号额度及店铺访问范围。"
                });
            }
            return new(root.Clone());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(null, "ERP 请求超时，请重试。", 504);
        }
        catch (Polly.Timeout.TimeoutRejectedException)
        {
            return new(null, "ERP 请求超时，请重试。", 504);
        }
        catch (HttpRequestException)
        {
            return new(null, "无法连接私有 ERP，请检查网络后重试。");
        }
        catch (JsonException)
        {
            return new(null, "ERP 返回了无法解析的响应。");
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
