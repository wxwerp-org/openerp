using Microsoft.AspNetCore.Http.HttpResults;

namespace OpenERP.Api.Erp;

public static class ErpEndpoints
{
    public static void MapErp(this WebApplication app)
    {
        app.MapGet("/api/erp/config", (IErpClient erp) => TypedResults.Ok(erp.Configuration))
            .WithSummary("查询私有 ERP 接入配置状态，不返回 Token");

        app.MapGet("/api/erp/shops", async Task<Results<Ok<ShopPage>, ProblemHttpResult>>
            (IErpClient erp, int? page, int? pageSize, CancellationToken cancellationToken) =>
        {
            var result = await erp.GetShopsAsync(page ?? 1, pageSize ?? 20, cancellationToken);
            if (result.Value is { } shops)
                return TypedResults.Ok(shops);

            return TypedResults.Problem(statusCode: result.Status, title: "获取店铺失败", detail: result.Error);
        }).WithSummary("分页获取 Temu 半托管跨境店铺及授权状态");
    }
}

/// <summary>接入配置状态，绝不包含凭证。</summary>
public sealed record ErpConfiguration(bool Configured, string BaseUrl);

/// <summary>店铺列表；授权状态查询失败时为未知，不隐藏店铺。</summary>
public sealed record ShopPage(IReadOnlyList<ErpShop> Items, int Page, int PageSize, int TotalCount, int PageCount);

/// <summary>当前 Token 可见的 Temu 半托管跨境店铺。</summary>
public sealed record ErpShop(string Sid, string Name, string Platform, string? Region, bool? IsTokenOk, string? StatusError);

/// <summary>ERP 调用结果，包含业务数据或失败信息。</summary>
public sealed record ErpResult<T>(T? Value, string? Error = null, int Status = 502);
