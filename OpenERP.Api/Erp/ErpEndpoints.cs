using Microsoft.AspNetCore.Http.HttpResults;
using OpenERP.Api.Authentication;
using OpenERP.Api.Orders;

namespace OpenERP.Api.Erp;

public static class ErpEndpoints
{
    public static void MapErp(this WebApplication app)
    {
        var orderSyncEnabled = app.Environment.IsDevelopment() || app.Environment.IsProduction();
        app.MapGet("/api/erp/config", async Task<Results<Ok<ErpConfigurationResponse>, ProblemHttpResult>>
            (IErpTokenStore store, CancellationToken cancellationToken) =>
        {
            var configuration = await store.GetConfigurationAsync(cancellationToken);
            if (configuration.IsErr)
                return TypedResults.Problem(statusCode: 503, title: configuration.Error.Message);

            return TypedResults.Ok(new ErpConfigurationResponse(
                configuration.Value.Configured, configuration.Value.BaseUrl, orderSyncEnabled,
                configuration.Value.Independent));
        })
            .RequireAuthorization(UserAccount.AdministratorRole)
            .WithSummary("查询私有 ERP 接入配置状态，不返回 Token")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapPut("/api/erp/config", async Task<Results<NoContent, ProblemHttpResult>>
            (UpdateErpTokenRequest request, IErpTokenStore store, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.AccessToken) || request.AccessToken.Contains('\r') || request.AccessToken.Contains('\n'))
                return TypedResults.Problem(statusCode: 400, title: "请输入单行 ERP AccessToken");

            if (!TryNormalizeBaseUrl(request.BaseUrl, out var baseUrl))
                return TypedResults.Problem(statusCode: 400, title: InvalidBaseUrlMessage);

            var saved = await store.SaveAsync(request.AccessToken.Trim(), baseUrl, cancellationToken);
            return saved.IsErr
                ? TypedResults.Problem(statusCode: 503, title: saved.Error.Message)
                : TypedResults.NoContent();
        }).RequireAuthorization(UserAccount.AdministratorRole)
            .WithSummary("保存或更换私有 ERP AccessToken")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapDelete("/api/erp/config", async Task<Results<NoContent, ProblemHttpResult>>
            (IErpTokenStore store, CancellationToken cancellationToken) =>
        {
            var deleted = await store.DeleteAsync(cancellationToken);
            return deleted.IsErr
                ? TypedResults.Problem(statusCode: 503, title: deleted.Error.Message)
                : TypedResults.NoContent();
        }).RequireAuthorization(UserAccount.AdministratorRole)
            .WithSummary("撤销私有 ERP 接入配置，删除已保存的 AccessToken 与部署地址")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapPost("/api/erp/config/validate", async Task<Results<Ok<ErpTokenCheckResponse>, ProblemHttpResult>>
            (UpdateErpTokenRequest request, IErpClient client, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.AccessToken) || request.AccessToken.Contains('\r') || request.AccessToken.Contains('\n'))
                return TypedResults.Problem(statusCode: 400, title: "请输入单行 ERP AccessToken");

            if (!TryNormalizeBaseUrl(request.BaseUrl, out var baseUrl))
                return TypedResults.Problem(statusCode: 400, title: InvalidBaseUrlMessage);

            var (result, status) = await client.GetShopsAsync(
                1, 1, null, new ErpAccessToken(request.AccessToken.Trim(), baseUrl), cancellationToken);
            return TypedResults.Ok(result.IsErr
                ? new ErpTokenCheckResponse(false, status is 401 or 403 ? "ERP 拒绝了该 AccessToken，请检查后重试" : result.Error.Message)
                : new ErpTokenCheckResponse(true, "AccessToken 可用"));
        }).RequireAuthorization(UserAccount.AdministratorRole)
            .WithSummary("向上游 ERP 检测 AccessToken 可用性，不保存")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/erp/order-sync-availability",
            async Task<Results<Ok<OrderSyncAvailabilityResponse>, ProblemHttpResult>>
                (IErpTokenStore store, CancellationToken cancellationToken) =>
            {
                var configuration = await store.GetConfigurationAsync(cancellationToken);
                return configuration.IsErr
                    ? TypedResults.Problem(statusCode: 503, title: configuration.Error.Message)
                    : TypedResults.Ok(new OrderSyncAvailabilityResponse(configuration.Value.Configured, orderSyncEnabled));
            })
            .RequireAuthorization()
            .WithSummary("查询业务页面使用的 ERP 配置状态与订单同步可用性")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapShopDirectory();
        app.MapPlatformAbilities();

    }

    private sealed record ErpConfigurationResponse(
        bool Configured, string BaseUrl, bool OrderSyncEnabled, bool Independent);

    private const string InvalidBaseUrlMessage = "独立部署地址须为 https://xxx.wxwerp.com 形式";

    /// <summary>
    /// 空值表示公共版（返回 null）；独立部署地址须是 wxwerp.com 子域的 https 根地址，
    /// 避免管理员把 Token 发往任意主机或内网地址。
    /// </summary>
    internal static bool TryNormalizeBaseUrl(string? value, out string? baseUrl)
    {
        baseUrl = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            uri.AbsolutePath != "/" ||
            !uri.IdnHost.EndsWith(".wxwerp.com", StringComparison.OrdinalIgnoreCase))
            return false;

        baseUrl = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    /// <summary>ERP 接入是否已配置、当前环境是否支持订单同步。</summary>
    private sealed record OrderSyncAvailabilityResponse(bool Configured, bool OrderSyncEnabled);

    /// <summary>AccessToken 可用性检测结果。</summary>
    private sealed record ErpTokenCheckResponse(bool Valid, string Message);

    /// <summary>管理员提交的新 ERP AccessToken。</summary>
    private sealed record UpdateErpTokenRequest(string? AccessToken, string? BaseUrl = null);
}

/// <summary>接入配置状态，绝不包含凭证。</summary>
public sealed record ErpConfiguration(bool Configured, string BaseUrl, bool Independent = false)
{
    internal const string UpstreamBaseUrl = "https://v3-api.wxwerp.com";
}

/// <summary>上游店铺分页；仅用于 ERP 客户端读取全量目录。</summary>
public sealed record ShopPage(
    IReadOnlyList<ErpShop> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int PageCount);

/// <summary>上游店铺基础信息；授权状态由本地同步任务落库。</summary>
public sealed record ErpShop(
    string Sid,
    string Name,
    string Platform,
    string? Region = null,
    string? MemberId = null);

/// <summary>上游单店授权状态；只包含上游实际返回的字段。</summary>
public sealed record ErpShopStatus(bool? IsTokenOk, string? Region, bool? IsSupportUpdateNumber);

/// <summary>平台目录项；平台集合与展示名来自上游 ERP，不在本地写死。</summary>
public sealed record ErpPlatform(
    string Name, string DisplayName, string? Icon, bool IsProduction, int ShopCount);

/// <summary>当前 Token 可见店铺涉及的平台目录。</summary>
public sealed record PlatformCatalog(IReadOnlyList<ErpPlatform> Items);
