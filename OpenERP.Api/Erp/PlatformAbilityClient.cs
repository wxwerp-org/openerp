using System.Text.Json;
using System.Text.RegularExpressions;
using OpResult;

namespace OpenERP.Api.Erp;

/// <summary>平台接口分类；冻结分类表示不适合卖家使用。</summary>
public sealed record PlatformAbilityCategory(int Id, string Name, int Count, bool Frozen, string? FrozenReason);

/// <summary>平台官方 API 条目；<see cref="SchemaName"/> 为空表示没有可读取的 Schema。</summary>
public sealed record PlatformAbilityApi(
    string Name, string Api, string Method, string Category, int CategoryId, string Description,
    bool Frozen, string? FrozenReason, string? SchemaName);

/// <summary>旺销王开放 API 中一个平台的能力清单；上游没有接口索引时 <see cref="Apis"/> 为空。</summary>
public sealed record PlatformAbility(
    string Key, string Name, string Description, string? OfficialUrl, string? OfficialUrlText, string ApiPath,
    string? RequestExample, IReadOnlyList<string> Steps,
    IReadOnlyList<PlatformAbilityCategory> Categories, IReadOnlyList<PlatformAbilityApi> Apis);

/// <summary>读取旺销王开放 API 清单（<c>/open-api/manifest</c>）与平台接口 Schema；两者都是公开只读接口，不携带 AccessToken。</summary>
public interface IPlatformAbilityClient
{
    Task<OpResult<IReadOnlyList<PlatformAbility>>> GetAbilitiesAsync(string baseUrl, CancellationToken cancellationToken);

    /// <returns>平台接口 Schema 的原始 JSON。</returns>
    Task<OpResult<JsonElement>> GetSchemaAsync(string baseUrl, string platform, string name, CancellationToken cancellationToken);
}

public sealed partial class PlatformAbilityClient(HttpClient http) : IPlatformAbilityClient
{
    [GeneratedRegex("^[a-z0-9]+$", RegexOptions.IgnoreCase)]
    private static partial Regex PlatformPattern();

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9_-]|\.(?!\.))*$", RegexOptions.IgnoreCase)]
    private static partial Regex ApiNamePattern();

    public async Task<OpResult<IReadOnlyList<PlatformAbility>>> GetAbilitiesAsync(string baseUrl, CancellationToken ct)
    {
        var response = await GetJsonAsync($"{baseUrl.TrimEnd('/')}/open-api/manifest", ct);
        if (response.IsErr)
            return response.Error;

        var root = response.Value;
        if (!root.TryGetProperty("platform_interfaces", out var section) ||
            !section.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return OpResults.Err("旺销王开放 API 清单格式不正确。");

        var abilities = new List<PlatformAbility>();
        foreach (var item in items.EnumerateArray())
        {
            var key = Text(item, "key");
            if (key is null || !PlatformPattern().IsMatch(key))
                continue;

            abilities.Add(new PlatformAbility(
                key,
                Text(item, "name") ?? key,
                Text(item, "description") ?? "",
                Text(item, "officialUrl"),
                Text(item, "officialUrlText"),
                Text(item, "apiPath") ?? $"/open-api/agent/{key}/invoke",
                item.TryGetProperty("requestExample", out var example) ? example.GetRawText() : null,
                StringArray(item, "steps"),
                ReadCategories(item),
                ReadApis(item, key)));
        }

        return OpResults.Ok<IReadOnlyList<PlatformAbility>>(abilities);
    }

    public async Task<OpResult<JsonElement>> GetSchemaAsync(
        string baseUrl, string platform, string name, CancellationToken ct)
    {
        if (!PlatformPattern().IsMatch(platform) || !ApiNamePattern().IsMatch(name))
            return OpResults.Err("平台或接口名称格式不正确。");

        return await GetJsonAsync($"{baseUrl.TrimEnd('/')}/open-api/schema/platform/{platform}/{name}", ct);
    }

    private static List<PlatformAbilityCategory> ReadCategories(JsonElement item)
    {
        var categories = new List<PlatformAbilityCategory>();
        if (!item.TryGetProperty("apiCategories", out var array) || array.ValueKind != JsonValueKind.Array)
            return categories;

        foreach (var category in array.EnumerateArray())
        {
            if (!category.TryGetProperty("id", out var id) || !id.TryGetInt32(out var categoryId))
                continue;

            categories.Add(new PlatformAbilityCategory(
                categoryId,
                Text(category, "name") ?? "",
                category.TryGetProperty("count", out var count) && count.TryGetInt32(out var total) ? total : 0,
                Bool(category, "frozen"),
                Text(category, "frozenReason")));
        }

        return categories;
    }

    private static List<PlatformAbilityApi> ReadApis(JsonElement item, string platform)
    {
        var apis = new List<PlatformAbilityApi>();
        if (!item.TryGetProperty("apis", out var array) || array.ValueKind != JsonValueKind.Array)
            return apis;

        foreach (var api in array.EnumerateArray())
        {
            var name = Text(api, "name");
            if (name is null)
                continue;

            apis.Add(new PlatformAbilityApi(
                name,
                Text(api, "api") ?? name,
                Text(api, "method") ?? "",
                Text(api, "category") ?? "",
                api.TryGetProperty("categoryId", out var id) && id.TryGetInt32(out var categoryId) ? categoryId : 0,
                Text(api, "description") ?? "",
                Bool(api, "frozen"),
                Text(api, "frozenReason"),
                SchemaName(Text(api, "schemaUrl"), platform)));
        }

        return apis;
    }

    /// <summary>只接受指向该平台 Schema 目录的地址，取末段作为接口名；其他情况视为无 Schema。</summary>
    private static string? SchemaName(string? schemaUrl, string platform)
    {
        if (schemaUrl is null)
            return null;

        var marker = $"/open-api/schema/platform/{platform}/";
        var index = schemaUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;

        var name = schemaUrl[(index + marker.Length)..];
        return ApiNamePattern().IsMatch(name) ? name : null;
    }

    private async Task<OpResult<JsonElement>> GetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return OpResults.Err($"旺销王开放 API 返回 HTTP {(int)response.StatusCode}。");

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return json.RootElement.ValueKind == JsonValueKind.Object
                ? json.RootElement.Clone()
                : OpResults.Err("旺销王开放 API 返回了无法识别的内容。");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return OpResults.Err("旺销王开放 API 请求超时，请重试。");
        }
        catch (Polly.Timeout.TimeoutRejectedException)
        {
            return OpResults.Err("旺销王开放 API 请求超时，请重试。");
        }
        catch (HttpRequestException)
        {
            return OpResults.Err("无法连接旺销王开放 API，请检查网络后重试。");
        }
        catch (JsonException)
        {
            return OpResults.Err("旺销王开放 API 返回了无法解析的响应。");
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static List<string> StringArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.String).Select(entry => entry.GetString()!).ToList()
            : [];
}
