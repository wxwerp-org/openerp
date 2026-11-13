using Microsoft.AspNetCore.Http.HttpResults;
using OpenERP.Api.Erp;

namespace OpenERP.Api.Orders;

public static class OrderSyncEndpoints
{
    public static void MapOrderSync(this WebApplication app)
    {
        const string route = "/api/erp/shops/{sid:int}/order-sync";

        app.MapPost(route, Results<Accepted<OrderSyncStatus>, ProblemHttpResult>
            (int sid, IErpClient erp, OrderSyncJobs jobs) =>
        {
            if (sid <= 0)
                return TypedResults.Problem(statusCode: 400, title: "店铺 SID 必须为正整数");
            if (!erp.Configuration.Configured)
                return TypedResults.Problem(statusCode: 503, title: "尚未配置私有 ERP AccessToken");
            var started = jobs.Start(sid);
            if (started.IsErr)
                return TypedResults.Problem(statusCode: 409, title: started.Error.Message);

            return TypedResults.Accepted($"/api/erp/shops/{sid}/order-sync", started.Value);
        }).WithSummary("为 TemuPop 店铺启动历史订单同步");

        app.MapGet(route, Results<Ok<OrderSyncStatus>, NoContent, ProblemHttpResult>
            (int sid, OrderSyncJobs jobs) =>
        {
            if (sid <= 0)
                return TypedResults.Problem(statusCode: 400, title: "店铺 SID 必须为正整数");
            if (jobs.Latest(sid) is { } job)
                return TypedResults.Ok(job);

            return TypedResults.NoContent();
        }).WithSummary("查询店铺最近一次历史订单同步");
    }
}
