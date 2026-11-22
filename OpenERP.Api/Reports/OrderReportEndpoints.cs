using System.Globalization;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Authentication;
using OpenERP.Core.Reports;

namespace OpenERP.Api.Reports;

public static class OrderReportEndpoints
{
    public static void MapOrderReports(this WebApplication app)
    {
        app.MapGet("/api/reports/order-snapshot", async Task<Results<Ok<OrderSnapshotReport>, ProblemHttpResult>>
            (IOrderSnapshotReportQuery query, TimeProvider clock, AppDbContext db, HttpContext context, string? platform,
                [FromQuery] string[]? shops, int? page, int? pageSize, string? trendStart, string? trendEnd,

                CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            DateOnly? start = null;
            if (trendStart is not null)
            {
                if (trendStart.Length != 10 || !DateOnly.TryParseExact(trendStart, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    return TypedResults.Problem(statusCode: 400, title: "走势开始日期必须使用 yyyy-MM-dd 格式。");
                start = date;
            }

            DateOnly? end = null;
            if (trendEnd is not null)
            {
                if (trendEnd.Length != 10 || !DateOnly.TryParseExact(trendEnd, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    return TypedResults.Problem(statusCode: 400, title: "走势结束日期必须使用 yyyy-MM-dd 格式。");
                end = date;
            }

            end ??= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var request = new OrderSnapshotReportRequest(platform, shops ?? [], page ?? 1, pageSize ?? 20, start, end);
            if (request.ValidationError is { } error)
                return TypedResults.Problem(statusCode: 400, title: error);

            var access = ShopAccess.For(context.User);
            if (!access.IsAdministrator)
            {
                try
                {
                    var assignments = await access.Assignments(db).AsNoTracking()
                        .Select(assignment => new { assignment.Platform, assignment.Sid })
                        .ToArrayAsync(cancellationToken);
                    var allowed = assignments.Select(shop => StoredOrderMapping.Shop(shop.Platform, shop.Sid)).ToArray();
                    var allowedKeys = allowed.Select(shop => shop.Key).ToHashSet(StringComparer.Ordinal);
                    if (request.Shops.Any(key => !allowedKeys.Contains(key)))
                        return TypedResults.Problem(statusCode: 403, title: "无权查看所选店铺。");
                    request = request with { AllowedShops = allowed };
                }
                catch (Exception exception) when (AuthenticationEndpoints.IsDatabaseFailure(exception))
                {
                    return TypedResults.Problem(statusCode: 503, title: "读取本地订单失败，请重试。");
                }
            }

            var result = await query.QueryAsync(request, cancellationToken);
            return result.IsErr
                ? TypedResults.Problem(statusCode: 503, title: result.Error.Message)
                : TypedResults.Ok(result.Value);
        }).WithSummary("查询本地订单商品量快照，数量不代表净销量")
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }
}
