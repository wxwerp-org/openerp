using System.Net;
using System.Text;
using System.Text.Json;
using OpResult;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

public sealed partial class OrderReaderTests
{
    [Fact]
    public async Task ReadsDetailsConcurrentlyAndReportsInInputOrder()
    {
        // Given 两张订单的详情可独立读取，第一张的响应延迟到第二张返回之后。
        var ct = TestContext.Current.CancellationToken;
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new ParallelDetailHandler(async (id, requestCt) =>
        {
            if (id == FirstId)
            {
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(requestCt);
                return DetailResponse(Detail);
            }

            Assert.Equal(SecondId, id);
            var response = DetailResponse(Detail);
            secondReturned.SetResult();
            return response;
        });
        using var http = new HttpClient(handler);
        var progress = new List<int>();
        OpResult<OrderImportBatch> result;

        // When 在首单仍挂起时，读取器完成第二单。
        var read = new WxwErpClient(http).ReadOrdersAsync(30, item =>
        {
            if (item.Stage == "details")
                progress.Add(item.Processed);
        }, Token, ct);
        try
        {
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            await secondReturned.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.False(read.IsCompleted);
        }
        finally
        {
            releaseFirst.TrySetResult();
            result = await read.WaitAsync(ct);
        }

        // Then 订单输出和进度按输入顺序合并。
        if (result.IsErr)
            throw new XunitException(result.Error.Message);
        Assert.Equal([FirstId, SecondId], result.Value.Orders.Select(order => order.ErpId));
        Assert.Equal([0, 1, 2], progress);
    }

    [Fact]
    public async Task SharesEightDetailSlotsAcrossShopsAndCancelsQueuedReads()
    {
        // Given 第一家店的八个详情请求占满容量，第二家店有独立的客户端。
        var ct = TestContext.Current.CancellationToken;
        var allSlotsOccupied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDetails = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRequests = 0;
        var secondRequests = 0;
        using var firstHandler = new ParallelDetailHandler(async (_, requestCt) =>
        {
            if (Interlocked.Increment(ref firstRequests) == 8)
                allSlotsOccupied.SetResult();
            await releaseDetails.Task.WaitAsync(requestCt);
            return DetailResponse(Detail);
        }, 30, 10);
        using var secondHandler = new ParallelDetailHandler((_, _) =>
        {
            Interlocked.Increment(ref secondRequests);
            return Task.FromResult(DetailResponse(Detail.Replace("\"sid\":30", "\"sid\":31", StringComparison.Ordinal)));
        }, 31);
        using var firstHttp = new HttpClient(firstHandler);
        using var secondHttp = new HttpClient(secondHandler);
        using var secondCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var firstRead = new WxwErpClient(firstHttp).ReadOrdersAsync(30, null, Token, ct);
        var secondClient = new WxwErpClient(secondHttp);
        OpResult<OrderImportBatch> firstResult;
        try
        {
            await allSlotsOccupied.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

            // When 在第一家店持有全部许可时启动并取消第二家店的读取。
            var secondRead = secondClient.ReadOrdersAsync(31, null, Token, secondCancellation.Token);
            await secondCancellation.CancelAsync();

            // Then 排队读取可取消，两家店都不能越过八个在途详情。
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondRead.WaitAsync(TimeSpan.FromSeconds(5), ct));
            Assert.Equal(8, Volatile.Read(ref firstRequests));
            Assert.Equal(0, Volatile.Read(ref secondRequests));
        }
        finally
        {
            releaseDetails.TrySetResult();
            firstResult = await firstRead.WaitAsync(ct);
        }

        // Then 释放容量后，第一家店全部读完，第二家店可重新读取。
        if (firstResult.IsErr)
            throw new XunitException(firstResult.Error.Message);
        Assert.Equal(10, firstResult.Value.Orders.Count);
        var retry = await secondClient.ReadOrdersAsync(31, null, Token, ct);
        if (retry.IsErr)
            throw new XunitException(retry.Error.Message);
        Assert.Equal(2, retry.Value.Orders.Count);
    }

    [Fact]
    public async Task CancelsAndDrainsPendingDetailsWhenFirstOrderIsRateLimited()
    {
        // Given 十张待同步订单，首单返回 HTTP 429，其他详情需要等待。
        var ct = TestContext.Current.CancellationToken;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var allSlotsOccupied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCount = 0;
        var canceled = 0;
        using var handler = new ParallelDetailHandler(async (id, requestCt) =>
        {
            if (Interlocked.Increment(ref requestCount) == 8)
                allSlotsOccupied.SetResult();
            if (id == "00000000000000000000000a")
            {
                await allSlotsOccupied.Task.WaitAsync(requestCt);
                return DetailResponse("{}", HttpStatusCode.TooManyRequests);
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, requestCt);
                throw new XunitException("失败后应取消该详情请求。");
            }
            finally
            {
                if (requestCt.IsCancellationRequested)
                    Interlocked.Increment(ref canceled);
            }
        }, 30, 10);
        using var http = new HttpClient(handler);

        // When 读取器收到首单的限流响应。
        var result = await new WxwErpClient(http).ReadOrdersAsync(30, null, Token, timeout.Token);

        // Then 返回整店失败，取消并等待在途详情，也不再发起窗口以外的请求。
        if (!result.IsErr)
            throw new XunitException("上游限流必须以整店读取失败结束。");
        Assert.Contains("HTTP 429", result.Error.Message);
        Assert.Equal(8, requestCount);
        Assert.Equal(7, canceled);
    }

    private static HttpResponseMessage DetailResponse(string data, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(
                data.StartsWith('!') ? data[1..] : "{\"success\":true,\"data\":" + data + "}",
                Encoding.UTF8, "application/json")
        };

    private sealed class ParallelDetailHandler(
        Func<string, CancellationToken, Task<HttpResponseMessage>> detail, int sid = 30, int orderCount = 2)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/open-api/shop/get_shops":
                    return DetailResponse(Shops.Replace("\"sid\":\"30\"", $"\"sid\":\"{sid}\"", StringComparison.Ordinal));
                case "/open-api/order/get_list_page_count":
                    return DetailResponse($$"""{"data_count":{{orderCount}},"page_count":1}""");
                case "/open-api/order/get_list":
                    var rows = Enumerable.Range(1, orderCount).Reverse()
                        .Select(id => Row(id.ToString("x24"), 30)
                            .Replace("\"sid\":30", $"\"sid\":{sid}", StringComparison.Ordinal)
                            .Replace("\"name\":\"_付款\"", "\"name\":\"_创建\"", StringComparison.Ordinal));
                    return DetailResponse("{\"orders\":[" + string.Join(",", rows) + "]}");
                case "/open-api/order_detail/get_base":
                    using (var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)))
                        return await detail(body.RootElement.GetProperty("id").GetString()!, ct);
                default:
                    throw new XunitException($"出现额外请求 {request.RequestUri?.AbsolutePath}");
            }
        }
    }
}
