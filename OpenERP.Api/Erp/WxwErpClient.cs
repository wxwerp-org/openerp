using System.Net.Http.Json;
using System.Text.Json;
using OpResult;

namespace OpenERP.Api.Erp;

public sealed partial class WxwErpClient(HttpClient http, IErpTokenStore tokens) : IErpClient
{
    // ERP Token 只发往这里指定的上游站点。
    private const string BaseUrl = "https://v3-api.wxwerp.com";

    public async Task<ErpConfiguration> GetConfigurationAsync(CancellationToken cancellationToken) =>
        new(await tokens.IsConfiguredAsync(cancellationToken), BaseUrl);

    public async Task<(OpResult<ShopPage> Result, int Status)> GetShopsAsync(
        int page, int pageSize, string? platform, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return (OpResults.Err("页码必须大于 0，每页数量必须在 1 至 100 之间。"), 400);

        var accessToken = await tokens.GetAsync(ct);
        // 店铺列表只读上游基础信息；授权状态由本地同步任务落库。
        return await GetShopListAsync(accessToken, page, pageSize, platform, ct);
    }

    public async Task<OpResult<ErpShopStatus>> GetShopStatusAsync(
        string sid, string accessToken, CancellationToken ct)
    {
        var (response, _) = await PostAsync(accessToken, "/open-api/shop/get_status", new { sid }, ct);
        if (response.IsErr)
            return response.Error;

        if (!response.Value.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return OpResults.Err("ERP 授权状态响应格式不正确。");

        return OpResults.Ok(new ErpShopStatus(
            TryBoolean(data, "isTokenOk"),
            Text(data, "region"),
            TryBoolean(data, "isSupportUpdateNumber")));
    }

    public async Task<(OpResult<IReadOnlyList<ErpPlatform>> Result, int Status)> GetPlatformsAsync(CancellationToken ct) =>
        await GetPlatformsAsync(await tokens.GetAsync(ct), ct);

    public async Task<(OpResult<IReadOnlyList<ErpPlatform>> Result, int Status)> GetPlatformsAsync(
        string accessToken, CancellationToken ct)
    {
        if (accessToken.Length == 0)
            return (OpResults.Err("尚未配置私有 ERP AccessToken。"), 503);

        // platforms 分组由店铺列表接口随全量店铺统计返回，不需要逐平台查询。
        var (groupsResponse, groupsStatus) = await PostAsync(
            accessToken, "/open-api/shop/get_shops", new { page = 1, page_size = 1 }, ct);
        if (groupsResponse.IsErr)
            return (groupsResponse.Error, groupsStatus);

        var root = groupsResponse.Value;
        if (!root.TryGetProperty("platforms", out var groups) || groups.ValueKind != JsonValueKind.Array)
            return (OpResults.Err("ERP 平台目录响应格式不正确。"), 502);

        var metadata = await ReadPlatformMetadataAsync(accessToken, ct);
        var platforms = new List<ErpPlatform>();
        foreach (var group in groups.EnumerateArray())
        {
            var name = Text(group, "platform");
            if (string.IsNullOrWhiteSpace(name) || !TryLooseCount(group, "count", out var shopCount) || shopCount <= 0)
                continue;

            metadata.TryGetValue(name, out var platformMetadata);
            var text = Text(group, "text") ?? platformMetadata?.Text;
            platforms.Add(new ErpPlatform(
                name,
                DisplayName(text, name),
                platformMetadata?.Icon,
                platformMetadata?.IsProduction ?? true,
                shopCount));
        }

        return (OpResults.Ok<IReadOnlyList<ErpPlatform>>(platforms
            .OrderByDescending(platform => platform.ShopCount)
            .ThenBy(platform => platform.Name, StringComparer.Ordinal)
            .ToArray()), 200);
    }

    public async Task<OpResult<IReadOnlyList<ErpShop>>> GetAllShopsAsync(string? platform, CancellationToken ct) =>
        await GetAllShopsAsync(platform, await tokens.GetAsync(ct), ct);

    public async Task<OpResult<IReadOnlyList<ErpShop>>> GetAllShopsAsync(
        string? platform, string accessToken, CancellationToken ct)
    {
        const int pageSize = 100;
        const int maxPages = 100;
        var shops = new List<ErpShop>();
        if (accessToken.Length == 0)
            return OpResults.Err("尚未配置私有 ERP AccessToken。");

        for (var page = 1; ; page++)
        {
            if (page > maxPages)
                return OpResults.Err("ERP 店铺数量超出选店接口可读取范围。");

            var (result, _) = await GetShopListAsync(accessToken, page, pageSize, platform, ct);
            if (result.IsErr)
                return result.Error;

            var list = result.Value;
            shops.AddRange(list.Items);
            if (page >= list.PageCount)
                break;
        }

        return OpResults.Ok<IReadOnlyList<ErpShop>>(shops.DistinctBy(shop => shop.Sid).ToArray());
    }

    private async Task<Dictionary<string, ErpPlatformMetadata>> ReadPlatformMetadataAsync(
        string accessToken, CancellationToken ct)
    {
        // 平台枚举只用于补充展示名、图标和生产可用标记；读取失败时使用店铺分组自带的展示名。
        var (response, _) = await PostAsync(accessToken, "/open-api/system/get_platforms", new { }, ct);
        var metadata = new Dictionary<string, ErpPlatformMetadata>(StringComparer.Ordinal);
        if (response.IsErr ||
            !response.Value.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
            return metadata;

        foreach (var item in items.EnumerateArray())
        {
            var name = Text(item, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var isProduction = item.TryGetProperty("isProduction", out var production) &&
                production.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? production.GetBoolean()
                : (bool?)null;
            metadata[name] = new(Text(item, "text"), Text(item, "icon"), isProduction);
        }

        return metadata;
    }

    private async Task<(OpResult<ShopPage> Result, int Status)> GetShopListAsync(
        string accessToken, int page, int pageSize, string? platform, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return (OpResults.Err("页码必须大于 0，每页数量必须在 1 至 100 之间。"), 400);
        if (accessToken.Length == 0)
            return (OpResults.Err("尚未配置私有 ERP AccessToken。"), 503);

        var normalizedPlatform = string.IsNullOrWhiteSpace(platform) ? null : platform.Trim();
        object request = normalizedPlatform is null
            ? new { page, page_size = pageSize }
            : new { page, page_size = pageSize, platform = normalizedPlatform };

        var (response, status) = await PostAsync(accessToken, "/open-api/shop/get_shops", request, ct);
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
            var platformName = Text(item, "platform");
            if (!int.TryParse(sid, out var id) || id <= 0 || string.IsNullOrWhiteSpace(platformName))
                return (OpResults.Err("ERP 返回了无效的店铺信息。"), 502);
            shops.Add(new ErpShop(
                Sid: sid,
                Name: Text(item, "name") ?? "",
                Platform: platformName,
                Region: Text(item, "region"),
                MemberId: Text(item, "memberid")));
        }

        return (new ShopPage(shops, page, pageSize, totalCount, pageCount), 200);
    }

    private async Task<(OpResult<JsonElement> Result, int Status)> PostAsync(
        string accessToken, string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path);
        if (!request.Headers.TryAddWithoutValidation("AccessToken", accessToken) ||
            accessToken.Contains('\r') || accessToken.Contains('\n'))
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
                        => "ERP AccessToken 无效、停用或过期，请在系统设置中检查。",
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

    private static bool? TryBoolean(JsonElement item, string key) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static bool TryCount(JsonElement item, string key, out int value)
    {
        value = 0;
        return item.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out value) && value >= 0;
    }

    private static bool TryLooseCount(JsonElement item, string key, out int value)
    {
        value = 0;
        if (!item.TryGetProperty(key, out var property))
            return false;

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value) && value >= 0,
            JsonValueKind.String => int.TryParse(property.GetString(), out value) && value >= 0,
            _ => false
        };
    }

    private static string DisplayName(string? text, string fallback)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return fallback;

        var stripped = trimmed.TrimStart('_').Trim();
        return string.IsNullOrEmpty(stripped) ? fallback : stripped;
    }

    private sealed record ErpPlatformMetadata(string? Text, string? Icon, bool? IsProduction);
}
