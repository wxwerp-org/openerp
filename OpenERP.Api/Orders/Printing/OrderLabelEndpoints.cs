using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Authentication;
using OpenERP.Api.Orders.Sources;

namespace OpenERP.Api.Orders.Printing;

internal static class OrderLabelEndpoints
{
    internal static void MapOrderLabels(this WebApplication app)
    {
        app.MapPost("/api/orders/print-labels", async Task<Results<Ok<OrderLabelResponse>, ProblemHttpResult>>
            (OrderLabelRequest request, HttpContext context, AppDbContext db, OrderLabelPrinters printers,
                CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var ids = request.Ids?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
            if (ids.Length is 0 or > 100)
                return TypedResults.Problem(statusCode: 400, title: "一次需选择1至100个订单。");

            // 只处理自己有权限的店铺下的订单；无权或不存在的订单不会出现在结果里。
            var orders = await ShopAccess.For(context.User).Orders(db).AsNoTracking()
                .Where(order => ids.Contains(order.ErpId))
                .Select(order => new { order.ErpId, order.Sid, order.Platform, order.OrderNumber }).ToArrayAsync(ct);
            if (orders.Length == 0)
                return TypedResults.Problem(statusCode: 404, title: "订单不存在，或无权查看。");

            var allowedIds = orders.Select(order => order.ErpId).ToArray();
            var platformNumbers = (await db.Set<PlatformOrderRecord>().AsNoTracking()
                .Where(po => allowedIds.Contains(po.ErpId)).Select(po => new { po.ErpId, po.PoNumber }).ToArrayAsync(ct))
                .ToLookup(po => po.ErpId, po => po.PoNumber);

            var documents = new List<OrderLabelFile>();
            var failures = new List<OrderLabelFailureItem>();
            foreach (var group in orders.GroupBy(order => (order.Platform, order.Sid)))
            {
                var numbers = group.SelectMany(order => platformNumbers[order.ErpId] is { } list && list.Any()
                    ? list : [order.OrderNumber]).Distinct(StringComparer.Ordinal).ToArray();
                var printer = printers.Find(group.Key.Platform);
                if (printer is null)
                {
                    failures.AddRange(numbers.Select(number =>
                        new OrderLabelFailureItem(number, $"{group.Key.Platform} 暂不支持打印面单。")));
                    continue;
                }

                var shop = await db.Shops.AsNoTracking().Where(row => row.Sid == group.Key.Sid)
                    .Select(row => new PlatformShopRef(row.Sid, row.Platform, row.Name)).FirstOrDefaultAsync(ct);
                if (shop is null)
                {
                    failures.AddRange(numbers.Select(number =>
                        new OrderLabelFailureItem(number, "店铺不存在或已从授权店铺中移除。")));
                    continue;
                }

                var result = await printer.PrintLabelsAsync(shop, numbers, ct);
                if (result.IsErr)
                {
                    var message = result.Error.Message;
                    failures.AddRange(numbers.Select(number => new OrderLabelFailureItem(number, message)));
                    continue;
                }

                documents.AddRange(result.Value.Documents.Select(document => new OrderLabelFile(
                    document.PlatformOrderNumber, document.FileName, document.ContentType,
                    Convert.ToBase64String(document.Content))));
                failures.AddRange(result.Value.Failures.Select(failure =>
                    new OrderLabelFailureItem(failure.PlatformOrderNumber, failure.Reason)));
            }

            return TypedResults.Ok(new OrderLabelResponse(documents, failures));
        }).RequireAuthorization().WithSummary("按所选本地订单取得物流面单（按平台抽象的打印能力）")
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }
}

/// <summary>要打印面单的本地订单（ERP 主单号，即列表里的订单 Id）。</summary>
public sealed record OrderLabelRequest(IReadOnlyList<string>? Ids);

/// <summary>一份面单文件，内容为 Base64。</summary>
public sealed record OrderLabelFile(string PlatformOrderNumber, string FileName, string ContentType, string Content);

public sealed record OrderLabelFailureItem(string PlatformOrderNumber, string Reason);

/// <summary>成功取得的面单文件与未取得面单的平台订单及原因。</summary>
public sealed record OrderLabelResponse(IReadOnlyList<OrderLabelFile> Documents, IReadOnlyList<OrderLabelFailureItem> Failures);
