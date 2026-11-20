using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using OpenERP.Api.Erp;
using OpenERP.Api.Tests;

[assembly: HostingStartup(typeof(BrowserErpStartup))]

namespace OpenERP.Api.Tests;

/// <summary>只在浏览器验收宿主中替换 ERP 客户端的 HTTP 传输。</summary>
public sealed class BrowserErpStartup : IHostingStartup
{
    internal const string ReadyMessage = "浏览器验收 ERP 传输桩已加载";
    internal const string TestToken = "openerp-browser-test-token";

    public void Configure(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IHttpMessageHandlerBuilderFilter, ErpTransportFilter>();
            Console.WriteLine(ReadyMessage);
        });
    }

    private sealed class ErpTransportFilter : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            if (builder.Name == nameof(WxwErpClient))
            {
                builder.PrimaryHandler.Dispose();
                builder.PrimaryHandler = new BrowserErpHandler();
            }
        };
    }

    private sealed class BrowserErpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (request.Method != HttpMethod.Post || request.RequestUri is not { } uri ||
                uri.GetLeftPart(UriPartial.Authority) != ErpConfiguration.UpstreamBaseUrl)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (!request.Headers.TryGetValues("AccessToken", out var tokens) || !tokens.SequenceEqual([TestToken]))
                return Task.FromResult(Json("""{"success":false,"message":"AccessToken invalid"}"""));

            var response = uri.AbsolutePath switch
            {
                "/open-api/shop/get_shops" => """
                    {"success":true,"data_count":1,"page_count":1,
                     "data":{"items":[{"sid":"30","name":"验收店铺","platform":"TemuPop","memberid":"browser-member-30"}]},
                     "platforms":[{"platform":"TemuPop","text":"Temu半托管","count":1}]}
                    """,
                "/open-api/shop/get_status" => """
                    {"success":true,"data":{"isTokenOk":false,"region":"US"}}
                    """,
                "/open-api/order/get_list_page_count" => """
                    {"success":true,"data":{"data_count":1,"page_count":1}}
                    """,
                "/open-api/order/get_list" => """
                    {"success":true,"data":{"orders":[{"id":"000000000000000000000030","orderid":"browser-sync-order",
                       "sub_orders":[{"id":"TM-BROWSER-SYNC-30","sid":30,"status":"已发货"}],
                       "times":[{"name":"创建","time":"2026-11-19 08:30"}]}]}}
                    """,
                "/open-api/order_detail/get_base" => """
                    {"success":true,"data":{"orderId":"browser-sync-order","shop":{"sid":30,"platform":"TemuPop"},
                       "status":{"key":"shipped"},"platformOrders":[{"oid":"TM-BROWSER-SYNC-30","status":{"key":"shipped"}}],
                       "products":[{"skuCode":"SKU-BROWSER-SYNC","qty":4}]}}
                    """,
                _ => null
            };
            return Task.FromResult(response is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(response));
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
