using System.Net.Http.Json;
using System.Text.Json;
using OpResult;

namespace OpenERP.Api.Erp;

/// <summary>
/// 旺销王「平台接口」代理：调用方只提供店铺 Sid，店铺的平台授权与应用密钥由旺销王托管，
/// 请求被转发到对应平台的开放 API，响应中的 <c>content</c> 是平台的原始响应。
/// 与 <see cref="IErpClient"/>（旺销王自己的业务接口）分开：这里不关心旺销王的订单、商品结构，只做平台调用。
/// </summary>
public interface IPlatformAgentApiClient
{
    /// <param name="platform">代理平台，对应路径 <c>/open-api/agent/{platform}/invoke</c>，例如 <c>aliexpress</c>。</param>
    /// <param name="sid">旺销王里的店铺编号，须属于当前 AccessToken 所属账号且平台与 <paramref name="platform"/> 一致。</param>
    /// <param name="api">平台开放 API 名称，例如 <c>aliexpress.trade.seller.orderlist.get</c>。</param>
    /// <param name="parameters">平台请求参数；旺销王要求所有值都是字符串，对象类参数需先序列化为 JSON 文本。</param>
    /// <returns>平台的原始响应（旺销王响应里的 <c>content</c>）。</returns>
    Task<OpResult<JsonElement>> InvokeAsync(string platform, int sid, string api,
        IReadOnlyDictionary<string, string> parameters, ErpAccessToken accessToken, CancellationToken cancellationToken,
        string? method = null);
}

public sealed class PlatformAgentApiClient(HttpClient http) : IPlatformAgentApiClient
{
    public async Task<OpResult<JsonElement>> InvokeAsync(string platform, int sid, string api,
        IReadOnlyDictionary<string, string> parameters, ErpAccessToken accessToken, CancellationToken ct,
        string? method = null)
    {
        if (accessToken.Value.Length == 0)
            return OpResults.Err("尚未配置私有 ERP AccessToken。");
        if (platform.Length == 0 || platform.Any(character => !char.IsAsciiLetterOrDigit(character)))
            return OpResults.Err("平台名称格式不正确。");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{accessToken.BaseUrl}/open-api/agent/{platform}/invoke");
        if (!request.Headers.TryAddWithoutValidation("AccessToken", accessToken.Value) ||
            accessToken.Value.Contains('\r') || accessToken.Value.Contains('\n'))
            return OpResults.Err("ERP AccessToken 配置格式不正确。");

        var body = new Dictionary<string, object> { ["sid"] = sid, ["api"] = api, ["params"] = parameters };
        if (!string.IsNullOrEmpty(method))
            body["method"] = method;
        request.Content = JsonContent.Create(body);

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return OpResults.Err($"ERP 平台接口返回 HTTP {(int)response.StatusCode}，请检查凭证权限及服务状态。");

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var success) ||
                success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return OpResults.Err("ERP 平台接口未返回有效的业务结果。");

            if (!success.GetBoolean())
            {
                // 不透传上游原始响应，防止把凭证或内部信息带到浏览器。
                var message = root.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String
                    ? text.GetString() : null;
                return OpResults.Err(message switch
                {
                    "AccessToken invalid" or "AccessToken disabled" or "AccessToken expired" or "AccessToken is empty"
                        => "ERP AccessToken 无效、停用或过期，请在系统设置中检查。",
                    "AccessToken scope denied" => "ERP AccessToken 没有平台接口的访问权限。",
                    "Shop not found or not accessible" => "旺销王中找不到该店铺，或当前账号无权访问。",
                    "Shop platform does not match agent API" => "该店铺的平台与所调用的平台接口不一致。",
                    "Shop token not found" => "该店铺尚未在旺销王完成平台授权。",
                    "sid invalid" => "店铺编号不正确。",
                    _ => "旺销王平台接口调用失败，请检查 Token 权限、账号额度及店铺授权。"
                });
            }

            return root.TryGetProperty("content", out var content) ? content.Clone() : OpResults.Err("ERP 平台接口未返回平台响应。");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return OpResults.Err("ERP 平台接口请求超时，请重试。");
        }
        catch (Polly.Timeout.TimeoutRejectedException)
        {
            return OpResults.Err("ERP 平台接口请求超时，请重试。");
        }
        catch (HttpRequestException)
        {
            return OpResults.Err("无法连接私有 ERP，请检查网络后重试。");
        }
        catch (JsonException)
        {
            return OpResults.Err("ERP 平台接口返回了无法解析的响应。");
        }
    }
}
