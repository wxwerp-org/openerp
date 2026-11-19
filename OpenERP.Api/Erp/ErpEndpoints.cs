using Microsoft.AspNetCore.Http.HttpResults;
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
                configuration.Value.Configured, configuration.Value.BaseUrl, orderSyncEnabled));
        })
            .WithSummary("查询私有 ERP 接入配置状态，不返回 Token")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapPut("/api/erp/config", async Task<Results<NoContent, ProblemHttpResult>>
            (UpdateErpTokenRequest request, IErpTokenStore store, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.AccessToken) || request.AccessToken.Contains('\r') || request.AccessToken.Contains('\n'))
                return TypedResults.Problem(statusCode: 400, title: "请输入单行 ERP AccessToken");

            var saved = await store.SaveAsync(request.AccessToken.Trim(), cancellationToken);
            return saved.IsErr
                ? TypedResults.Problem(statusCode: 503, title: saved.Error.Message)
                : TypedResults.NoContent();
        }).RequireAuthorization()
            .WithSummary("保存或更换私有 ERP AccessToken")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapShopDirectory();

        if (orderSyncEnabled)
            app.MapOrderSync();
    }

    private sealed record ErpConfigurationResponse(bool Configured, string BaseUrl, bool OrderSyncEnabled);

    /// <summary>管理员提交的新 ERP AccessToken。</summary>
    private sealed record UpdateErpTokenRequest(string? AccessToken);
}

/// <summary>接入配置状态，绝不包含凭证。</summary>
public sealed record ErpConfiguration(bool Configured, string BaseUrl)
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
