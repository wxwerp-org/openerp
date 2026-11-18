using Microsoft.AspNetCore.Http.HttpResults;
using OpenERP.Api.Orders;

namespace OpenERP.Api.Erp;

public static class ErpEndpoints
{
    public static void MapErp(this WebApplication app)
    {
        var orderSyncEnabled = app.Environment.IsDevelopment() || app.Environment.IsProduction();
        app.MapGet("/api/erp/config", async (IErpClient erp, CancellationToken cancellationToken) =>
        {
            var configuration = await erp.GetConfigurationAsync(cancellationToken);
            return TypedResults.Ok(new ErpConfigurationResponse(configuration.Configured, configuration.BaseUrl, orderSyncEnabled));
        })
            .WithSummary("查询私有 ERP 接入配置状态，不返回 Token");

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

        app.MapGet("/api/erp/shops", async Task<Results<Ok<ShopPage>, ProblemHttpResult>>
            (IErpClient erp, int? page, int? pageSize, CancellationToken cancellationToken) =>
        {
            var (result, status) = await erp.GetShopsAsync(page ?? 1, pageSize ?? 20, cancellationToken);
            if (result.IsErr)
                return TypedResults.Problem(statusCode: status, title: "获取店铺失败", detail: result.Error.Message);

            return TypedResults.Ok(result.Value);
        }).WithSummary("分页获取 Temu 半托管跨境店铺及授权状态");

        if (orderSyncEnabled)
            app.MapOrderSync();
    }

    private sealed record ErpConfigurationResponse(bool Configured, string BaseUrl, bool OrderSyncEnabled);

    /// <summary>管理员提交的新 ERP AccessToken。</summary>
    private sealed record UpdateErpTokenRequest(string? AccessToken);
}

/// <summary>接入配置状态，绝不包含凭证。</summary>
public sealed record ErpConfiguration(bool Configured, string BaseUrl);

/// <summary>店铺列表；授权状态查询失败时为未知，不隐藏店铺。</summary>
public sealed record ShopPage(IReadOnlyList<ErpShop> Items, int Page, int PageSize, int TotalCount, int PageCount);

/// <summary>当前 Token 可见的 Temu 半托管跨境店铺。</summary>
public sealed record ErpShop(string Sid, string Name, string Platform, string? Region, bool? IsTokenOk, string? StatusError);
