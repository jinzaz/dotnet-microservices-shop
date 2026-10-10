using System.Linq.Expressions;
using MSA.OrderService.Domain;

namespace MSA.OrderService.Application.AfterSales;

/// <summary>After-sales service-level timeouts; each counts from the moment the request entered its current status.</summary>
public sealed class AfterSalesOptions
{
    public const string SectionName = "AfterSales";

    /// <summary>Merchant review window; afterwards the request is approved automatically.</summary>
    public TimeSpan ReviewTimeout { get; init; } = TimeSpan.FromDays(2);
    /// <summary>Customer window to ship the return; afterwards the request closes.</summary>
    public TimeSpan ReturnShipmentTimeout { get; init; } = TimeSpan.FromDays(7);
    /// <summary>Merchant window to confirm a shipped return; afterwards a return-and-refund is received and refunded.</summary>
    public TimeSpan ReturnReceiptTimeout { get; init; } = TimeSpan.FromDays(10);
    /// <summary>Merchant window to refund received goods; afterwards the refund starts automatically.</summary>
    public TimeSpan RefundTimeout { get; init; } = TimeSpan.FromDays(2);
    /// <summary>Customer window to confirm the replacement; afterwards the request completes.</summary>
    public TimeSpan ReplacementReceiptTimeout { get; init; } = TimeSpan.FromDays(10);
    public TimeSpan ScanInterval { get; init; } = TimeSpan.FromMinutes(1);
    /// <summary>Used when a return is auto-approved; without it return and exchange requests wait for a merchant.</summary>
    public string? DefaultReturnAddress { get; init; }

    internal static bool IsValid(AfterSalesOptions options) =>
        new[] { options.ReviewTimeout, options.ReturnShipmentTimeout, options.ReturnReceiptTimeout, options.RefundTimeout, options.ReplacementReceiptTimeout }
            .All(value => value >= TimeSpan.FromMinutes(1))
        && options.ScanInterval >= TimeSpan.FromSeconds(5)
        && (options.DefaultReturnAddress?.Trim().Length ?? 0) <= 500;

    /// <summary>Requests whose automatic action is due; used both to find candidates and to re-check under lock.</summary>
    public Expression<Func<AfterSalesRequest, bool>> Due(DateTimeOffset now)
    {
        var review = now - ReviewTimeout;
        var returnShipment = now - ReturnShipmentTimeout;
        var returnReceipt = now - ReturnReceiptTimeout;
        var refund = now - RefundTimeout;
        var replacementReceipt = now - ReplacementReceiptTimeout;
        var canApproveReturns = !string.IsNullOrWhiteSpace(DefaultReturnAddress);
        return request =>
            request.Status == AfterSalesStatus.Requested && request.StatusChangedOnUtc <= review
                && (canApproveReturns || request.Type == AfterSalesType.RefundOnly || request.Type == AfterSalesType.Reship)
            || request.Status == AfterSalesStatus.AwaitingReturn && request.StatusChangedOnUtc <= returnShipment
            || request.Status == AfterSalesStatus.ReturnInTransit && request.Type == AfterSalesType.ReturnAndRefund && request.StatusChangedOnUtc <= returnReceipt
            || request.Status == AfterSalesStatus.ReturnReceived && request.Type == AfterSalesType.ReturnAndRefund && request.StatusChangedOnUtc <= refund
            || request.Status == AfterSalesStatus.ReplacementShipped && request.StatusChangedOnUtc <= replacementReceipt;
    }

    /// <summary>When the current status will be handled automatically, for display; null when nothing is automatic.</summary>
    public DateTimeOffset? DeadlineFor(AfterSalesStatus status, AfterSalesType type, DateTimeOffset statusChangedOnUtc) =>
        status switch
        {
            AfterSalesStatus.Requested when type is AfterSalesType.RefundOnly or AfterSalesType.Reship || !string.IsNullOrWhiteSpace(DefaultReturnAddress)
                => statusChangedOnUtc + ReviewTimeout,
            AfterSalesStatus.AwaitingReturn => statusChangedOnUtc + ReturnShipmentTimeout,
            AfterSalesStatus.ReturnInTransit when type == AfterSalesType.ReturnAndRefund => statusChangedOnUtc + ReturnReceiptTimeout,
            AfterSalesStatus.ReturnReceived when type == AfterSalesType.ReturnAndRefund => statusChangedOnUtc + RefundTimeout,
            AfterSalesStatus.ReplacementShipped => statusChangedOnUtc + ReplacementReceiptTimeout,
            _ => null
        };
}
