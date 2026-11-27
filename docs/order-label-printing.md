# 订单打印面单

在订单列表勾选订单，点底部的"打印面单"，新窗口会打开面单 PDF，用浏览器直接打印即可。
已用真实店铺的已发货订单联调通过（2026-11-26）。

## 使用条件

- 已在 `/system/erp-binding` 绑定旺销王 AccessToken。
- 订单是**已通过 AliExpress 线上物流发货**的。没发货、或自己线下发货的订单没有面单，会提示"还没有线上发货的物流订单"。

## 前端怎么调

只有一个接口，和平台无关：

`POST /api/orders/print-labels`，请求体 `{ "ids": ["订单列表里的订单 Id", ...] }`，一次 1～100 个。

返回：

```json
{
  "documents": [ { "platformOrderNumber": "…", "fileName": "label-….pdf", "contentType": "application/pdf", "content": "Base64 的 PDF" } ],
  "failures":  [ { "platformOrderNumber": "…", "reason": "没取到面单的原因" } ]
}
```

成功的放 `documents`，每个订单一份 PDF；失败的放 `failures` 并说明原因，不会让整个请求报错。
前端代码在 `OpenERP.Web/src/api/order.ts` 的 `printOrderLabels` 和 `views/order/list/index.vue`。

## 后端怎么做

端点只认一个接口 `IOrderLabelPrinter`，**每个平台写一个实现**，按平台名自动选用：

```csharp
public interface IOrderLabelPrinter
{
    string Platform { get; }   // 平台名，如 "AliExpress"
    Task<OpResult<OrderLabelBatch>> PrintLabelsAsync(PlatformShopRef shop, IReadOnlyList<string> platformOrderNumbers, CancellationToken ct);
}
```

以后要支持别的平台：新写一个实现，在 `Program.cs` 里注册一行，前端和端点都不用改。
没有实现的平台，会在 `failures` 里提示"暂不支持打印面单"。

## AliExpress 是怎么取到面单的

面单要用"国际运单号"，而订单里没有，所以每个订单调两次旺销王平台接口
（调用方式见 [development.md 的平台接口调用流程](development.md#平台接口调用流程)）：

| 步骤 | 平台 API | 传什么 | 拿到什么 |
| --- | --- | --- | --- |
| 1 | `aliexpress.logistics.querylogisticsorderdetail` | `trade_order_id`（交易单号） | 国际运单号 `international_logistics_num`、物流订单号 |
| 2 | `aliexpress.logistics.redefining.getprintinfos` | `print_detail=true`、`warehouse_order_query_d_t_os`（JSON 文本，如 `[{"international_logistics_id":"LP…","id":123}]`） | Base64 的 PDF |

两点要知道：

- 平台的真实返回会多包几层，面单的 `result` 还是一段 JSON 字符串，里面的 `body` 才是 PDF。代码里 `FindPackages`、`FindPdf` 已经处理好了。
- **要留意：** 步骤 1 的接口平台公告说 **2027-03-23 下线**，之前需要改用新接口 `aliexpress.asf.cb.package.query`
  （传 `trade_order_id`，运单号字段是 `tracking_number`，2026-12-09 起开放）。到时只需要改 `AliExpressOrderLabelPrinter.cs` 里步骤 1 的接口名、参数和 `FindPackages` 里的字段名。

## 代码在哪

| 文件 | 作用 |
| --- | --- |
| `OpenERP.Api/Orders/Printing/IOrderLabelPrinter.cs` | 接口和按平台选用 |
| `OpenERP.Api/Orders/Printing/AliExpressOrderLabelPrinter.cs` | AliExpress 的实现 |
| `OpenERP.Api/Orders/Printing/OrderLabelEndpoints.cs` | 统一接口 |
| `OpenERP.Api.Tests/AliExpressOrderLabelPrinterTests.cs`、`OrderListEndpointTests.cs` | 测试 |
