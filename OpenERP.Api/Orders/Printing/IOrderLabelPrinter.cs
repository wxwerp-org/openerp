using OpenERP.Api.Orders.Sources;
using OpResult;

namespace OpenERP.Api.Orders.Printing;

/// <summary>一份可直接打印的面单文档，内容与平台无关（目前为 PDF）。</summary>
public sealed record OrderLabelDocument(string PlatformOrderNumber, string FileName, string ContentType, byte[] Content);

/// <summary>某个平台子单没有取到面单的原因。</summary>
public sealed record OrderLabelFailure(string PlatformOrderNumber, string Reason);

/// <summary>一次平台调用的面单：成功取得的文档，加上逐单的失败原因。</summary>
public sealed record OrderLabelBatch(IReadOnlyList<OrderLabelDocument> Documents, IReadOnlyList<OrderLabelFailure> Failures);

/// <summary>
/// 面单打印能力：每个平台一个实现，通过平台（或物流）接口取得物流面单。
/// 打印端点只依赖这个契约，不关心各平台不同的接口与响应结构。
/// </summary>
public interface IOrderLabelPrinter
{
    /// <summary>平台名称，与授权店铺目录里的平台名称一致。</summary>
    string Platform { get; }

    /// <summary>读取同一店铺下若干平台订单号的面单。整体失败返回错误；部分订单失败通过 Failures 表达。</summary>
    Task<OpResult<OrderLabelBatch>> PrintLabelsAsync(
        PlatformShopRef shop, IReadOnlyList<string> platformOrderNumbers, CancellationToken cancellationToken);
}

/// <summary>按平台名称解析面单打印实现。</summary>
public sealed class OrderLabelPrinters(IEnumerable<IOrderLabelPrinter> printers)
{
    private readonly Dictionary<string, IOrderLabelPrinter> _printers =
        printers.ToDictionary(printer => printer.Platform, StringComparer.Ordinal);

    public IOrderLabelPrinter? Find(string platform) => _printers.GetValueOrDefault(platform);
}
