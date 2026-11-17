using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using OpenERP.Core.Reports;

namespace OpenERP.Api.Reports;

public static class OrderReportEndpoints
{
    public static void MapOrderReports(this WebApplication app)
    {
        app.MapGet("/api/reports/order-snapshot", async Task<Results<Ok<OrderSnapshotReport>, ProblemHttpResult>>
            (IOrderSnapshotReportQuery query, HttpContext context, string? platform,
                [FromQuery] string[]? shops, int? page, int? pageSize, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var request = new OrderSnapshotReportRequest(platform, shops ?? [], page ?? 1, pageSize ?? 20);
            if (request.ValidationError is { } error)
                return TypedResults.Problem(statusCode: 400, title: error);
            var result = await query.QueryAsync(request, cancellationToken);
            return result.IsErr
                ? TypedResults.Problem(statusCode: 503, title: result.Error.Message)
                : TypedResults.Ok(result.Value);
        }).WithSummary("查询本地订单商品量快照，数量不代表净销量");
    }
}
