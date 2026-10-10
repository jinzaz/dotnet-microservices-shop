using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.Infrastrucute.Mediator;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;
using MSA.Persistence.Postgres;

namespace MSA.OrderService.Application.AfterSales;

public sealed record CreateAfterSalesCommand(
    Guid OrderId,
    string UserId,
    string IdempotencyKey,
    AfterSalesType Type,
    AfterSalesReason Reason,
    string? Description,
    IReadOnlyList<AfterSalesItem> Items,
    IReadOnlyList<string> EvidenceObjectKeys) : ICommand<Guid>;

public enum AfterSalesAction
{
    // 买家
    Cancel,
    SubmitReturnShipment,
    ConfirmReceipt,
    // 商家
    Approve,
    Reject,
    ConfirmReturnReceived,
    Refund,
    ShipReplacement,
    Complete,
    DisposeReturn,
    RetryReplacementStock
}

/// <param name="AsCustomer">Customer calls are scoped to their own requests; other users' requests read as missing.</param>
/// <param name="Restock">Per-SKU units returned to sellable stock, for <see cref="AfterSalesAction.DisposeReturn"/>.</param>
public sealed record AfterSalesActionCommand(
    Guid RequestId,
    string Actor,
    bool AsCustomer,
    AfterSalesAction Action,
    string? Note = null,
    string? ReturnAddress = null,
    string? CarrierCode = null,
    string? CarrierName = null,
    string? TrackingNumber = null,
    IReadOnlyList<AfterSalesItem>? Restock = null) : ICommand<Guid>;

/// <summary>Applies the automatic action of an overdue request; returns false when it is no longer due.</summary>
public sealed record AfterSalesTimeoutCommand(Guid RequestId) : ICommand<bool>;

internal sealed class CreateAfterSalesCommandHandler(
    OrderDbContext db,
    AfterSalesEvidenceStorage evidence,
    TimeProvider clock) : IRequestHandler<CreateAfterSalesCommand, Guid>
{
    public async Task<Guid> Handle(CreateAfterSalesCommand request, CancellationToken cancellationToken)
    {
        var key = request.IdempotencyKey.Trim();
        var replayed = await db.AfterSalesRequests
            .Where(value => value.UserId == request.UserId && value.IdempotencyKey == key)
            .Select(value => new { value.Id, value.OrderId })
            .SingleOrDefaultAsync(cancellationToken);
        if (replayed is not null)
        {
            return replayed.OrderId == request.OrderId
                ? replayed.Id
                : throw new AfterSalesRuleException("该提交已用于其他订单，请刷新页面后重试。");
        }

        foreach (var objectKey in request.EvidenceObjectKeys)
        {
            if (!AfterSalesEvidenceStorage.IsOwnedBy(objectKey, request.UserId))
                throw new AfterSalesRuleException("凭证图片无效，请重新上传。");
            await evidence.EnsureUploadedAsync(objectKey, cancellationToken);
        }

        // 锁订单行：同一订单的并发申请串行化，才能正确计算剩余可售后件数与可退金额。
        var order = await AfterSalesLocks.LockOrderAsync(db, request.OrderId, cancellationToken);
        if (order is null || !string.Equals(order.UserId, request.UserId, StringComparison.Ordinal))
            throw new KeyNotFoundException("Order was not found.");
        await db.Entry(order).Collection(value => value.Lines).LoadAsync(cancellationToken);
        var existing = await db.AfterSalesRequests
            .Include(value => value.Lines)
            .Where(value => value.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        var afterSales = AfterSalesRequest.Submit(
            Guid.NewGuid(),
            order,
            existing,
            key,
            request.Type,
            request.Reason,
            request.Description,
            request.Items,
            request.EvidenceObjectKeys,
            clock.GetUtcNow());
        db.AfterSalesRequests.Add(afterSales);
        return afterSales.Id;
    }
}

internal sealed class AfterSalesActionCommandHandler(
    OrderDbContext db,
    TimeProvider clock) : IRequestHandler<AfterSalesActionCommand, Guid>
{
    public async Task<Guid> Handle(AfterSalesActionCommand command, CancellationToken cancellationToken)
    {
        var request = await AfterSalesLocks.LockWithOrderAsync(
            db, command.RequestId, command.AsCustomer ? command.Actor : null, cancellationToken)
            ?? throw new KeyNotFoundException("After-sales request was not found.");

        var now = clock.GetUtcNow();
        var actor = command.Actor;
        if (StartsRefund(request, command.Action) && await AfterSalesLocks.RefundInFlightAsync(db, request, cancellationToken))
            throw new AfterSalesRuleException("该订单另一笔售后退款仍在处理中，请稍后再试。");

        _ = command.Action switch
        {
            AfterSalesAction.Cancel => request.Cancel(actor, now),
            AfterSalesAction.SubmitReturnShipment => request.SubmitReturnShipment(command.CarrierName!, command.TrackingNumber!, actor, now),
            AfterSalesAction.ConfirmReceipt => request.Complete("买家已确认收到商品", actor, now),
            AfterSalesAction.Approve => request.Approve(command.ReturnAddress, command.Note, actor, now),
            AfterSalesAction.Reject => request.Reject(command.Note!, actor, now),
            AfterSalesAction.ConfirmReturnReceived => request.ConfirmReturnReceived(command.Note, actor, now),
            AfterSalesAction.Refund => request.StartRefund(command.Note, actor, now),
            AfterSalesAction.ShipReplacement => request.ShipReplacement(command.CarrierCode!, command.CarrierName!, command.TrackingNumber!, command.Note, actor, now),
            AfterSalesAction.Complete => request.Complete(command.Note ?? "商家确认售后完成", actor, now),
            AfterSalesAction.DisposeReturn => request.DisposeReturn(command.Restock ?? [], command.Note, actor, now),
            AfterSalesAction.RetryReplacementStock => request.RetryReplacementStock(actor, now),
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        return request.Id;
    }

    private static bool StartsRefund(AfterSalesRequest request, AfterSalesAction action) =>
        action == AfterSalesAction.Refund
        || action == AfterSalesAction.Approve && request.Type == AfterSalesType.RefundOnly && request.Status == AfterSalesStatus.Requested;
}

/// <summary>
/// 超时自动处理：商家超时未审核自动同意、买家超时未寄回自动关闭、商家超时未收货 / 未退款自动退款、
/// 买家超时未确认收到新品自动完成。全部复用人工操作的领域方法，退款、库存与通知因此同样发出。
/// </summary>
internal sealed class AfterSalesTimeoutCommandHandler(
    OrderDbContext db,
    IOptions<AfterSalesOptions> options,
    TimeProvider clock) : IRequestHandler<AfterSalesTimeoutCommand, bool>
{
    private const string System = "system";

    public async Task<bool> Handle(AfterSalesTimeoutCommand command, CancellationToken cancellationToken)
    {
        var request = await AfterSalesLocks.LockWithOrderAsync(db, command.RequestId, null, cancellationToken);
        var now = clock.GetUtcNow();
        var policy = options.Value;
        // 加锁后再判一次：另一实例或人工操作可能刚处理过。
        if (request is null || !policy.Due(now).Compile()(request))
            return false;

        switch (request.Status)
        {
            case AfterSalesStatus.Requested:
                if (request.Type == AfterSalesType.RefundOnly && await AfterSalesLocks.RefundInFlightAsync(db, request, cancellationToken))
                    return false;
                return request.Approve(policy.DefaultReturnAddress, "商家超时未处理，系统已自动同意", System, now);
            case AfterSalesStatus.AwaitingReturn:
                return request.Cancel(System, now, "买家超时未寄回商品，售后已自动关闭");
            case AfterSalesStatus.ReturnInTransit:
                request.ConfirmReturnReceived("商家超时未确认收货，系统已自动确认", System, now);
                if (!await AfterSalesLocks.RefundInFlightAsync(db, request, cancellationToken))
                    request.StartRefund("系统已自动发起原路退款", System, now);
                return true;
            case AfterSalesStatus.ReturnReceived:
                if (await AfterSalesLocks.RefundInFlightAsync(db, request, cancellationToken))
                    return false;
                return request.StartRefund("商家超时未退款，系统已自动发起原路退款", System, now);
            case AfterSalesStatus.ReplacementShipped:
                return request.Complete("买家超时未确认收货，系统已自动完成", System, now);
            default:
                return false;
        }
    }
}

internal static class AfterSalesLocks
{
    public static Task<Order?> LockOrderAsync(OrderDbContext db, Guid orderId, CancellationToken cancellationToken) =>
        db.Orders.Where(order => order.Id == orderId).ForUpdate().SingleOrDefaultAsync(cancellationToken);

    /// <summary>Locks the order and then the request with its lines, in the order every after-sales writer uses.</summary>
    /// <param name="customerId">When set, other customers' requests read as missing.</param>
    public static async Task<AfterSalesRequest?> LockWithOrderAsync(
        OrderDbContext db,
        Guid requestId,
        string? customerId,
        CancellationToken cancellationToken)
    {
        var orderId = await db.AfterSalesRequests
            .Where(value => value.Id == requestId && (customerId == null || value.UserId == customerId))
            .Select(value => (Guid?)value.OrderId)
            .SingleOrDefaultAsync(cancellationToken);
        if (orderId is null)
            return null;

        // 先订单后售后单，与退款回执处理器一致，避免死锁；订单锁同时把同一订单的退款串行化。
        _ = await LockOrderAsync(db, orderId.Value, cancellationToken);
        return await LockRequestAsync(db, requestId, cancellationToken);
    }

    /// <summary>Row-locks a request and loads its lines (stock events list them).</summary>
    public static async Task<AfterSalesRequest?> LockRequestAsync(OrderDbContext db, Guid requestId, CancellationToken cancellationToken)
    {
        var request = await db.AfterSalesRequests.Where(value => value.Id == requestId).ForUpdate().SingleOrDefaultAsync(cancellationToken);
        if (request is not null)
            await db.Entry(request).Collection(value => value.Lines).LoadAsync(cancellationToken);
        return request;
    }

    /// <summary>
    /// 支付单同一时刻只允许一笔退款在途，第二笔会在支付侧失败并进入死信；调用方需持有订单锁。
    /// </summary>
    public static Task<bool> RefundInFlightAsync(OrderDbContext db, AfterSalesRequest request, CancellationToken cancellationToken) =>
        db.AfterSalesRequests.AnyAsync(
            value => value.OrderId == request.OrderId
                && value.Id != request.Id
                && value.Status == AfterSalesStatus.Refunding,
            cancellationToken);
}

/// <summary>Routes payment refund acknowledgements to the after-sales request that issued them.</summary>
internal static class AfterSalesRefundAcknowledgement
{
    /// <returns>False when the refund was not issued by after-sales (for example a late-payment compensation).</returns>
    public static async Task<bool> TryApplyAsync(
        OrderDbContext db,
        Order lockedOrder,
        Guid refundRequestId,
        string? failureReason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var request = await db.AfterSalesRequests
            .Where(value => value.RefundRequestId == refundRequestId)
            .ForUpdate()
            .SingleOrDefaultAsync(cancellationToken);
        if (request is null)
            return false;

        if (failureReason is not null)
        {
            request.MarkRefundFailed(refundRequestId, failureReason, now);
            return true;
        }
        if (request.MarkRefunded(refundRequestId, now) && request.Type == AfterSalesType.RefundOnly)
            await CloseFulfillmentIfFullyRefundedAsync(db, lockedOrder, request, now, cancellationToken);
        return true;
    }

    private static async Task CloseFulfillmentIfFullyRefundedAsync(
        OrderDbContext db,
        Order order,
        AfterSalesRequest current,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (order.FulfillmentStatus != OrderFulfillmentStatus.AwaitingShipment)
            return;
        await db.Entry(order).Collection(value => value.Lines).LoadAsync(cancellationToken);
        await db.Entry(current).Collection(value => value.Lines).LoadAsync(cancellationToken);
        var earlier = await db.AfterSalesRequests
            .Where(value => value.OrderId == order.Id
                && value.Id != current.Id
                && value.Type == AfterSalesType.RefundOnly
                && value.Status == AfterSalesStatus.Refunded)
            .SelectMany(value => value.Lines)
            .Select(line => new { line.Sku, line.Quantity })
            .ToListAsync(cancellationToken);
        var refunded = earlier
            .Concat(current.Lines.Select(line => new { line.Sku, line.Quantity }))
            .GroupBy(line => line.Sku)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));
        if (order.Lines.All(line => refunded.GetValueOrDefault(line.Sku) >= line.Quantity))
            order.CloseFulfillmentAfterFullRefund(now);
    }
}
