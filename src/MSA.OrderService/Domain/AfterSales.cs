using MSA.Infrastrucute.Entities;
using MSA.Infrastrucute.Events;

namespace MSA.OrderService.Domain;

/// <summary>
/// 售后单：一次针对已付款订单部分或全部商品的退款、退货、换货或补寄请求。
/// 与订单是同一限界上下文内的两个聚合：订单只提供资格与金额快照，售后进度不回写订单状态；
/// 唯一例外是整单仅退款后关闭履约（见 <see cref="Order.CloseFulfillmentAfterFullRefund"/>）。
/// </summary>
public sealed class AfterSalesRequest : AggregateRoot<Guid>
{
    public static readonly TimeSpan ReturnWindow = TimeSpan.FromDays(7);
    public const int MaxEvidenceCount = 6;

    private readonly List<AfterSalesLine> _lines = [];
    private readonly List<AfterSalesHistory> _history = [];

    private AfterSalesRequest()
    {
    }

    public string RequestNumber { get; private set; } = string.Empty;
    public Guid OrderId { get; private set; }
    public string OrderNumber { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public Guid PaymentId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public AfterSalesType Type { get; private set; }
    /// <summary>The parcel had left the warehouse but was not yet delivered when the customer applied.</summary>
    public bool InTransitAtSubmission { get; private set; }
    public AfterSalesStatus Status { get; private set; }
    public AfterSalesReason Reason { get; private set; }
    public string? Description { get; private set; }
    public string[] EvidenceObjectKeys { get; private set; } = [];
    public decimal RefundAmount { get; private set; }
    public string Currency { get; private set; } = "CNY";
    public string? ReturnAddress { get; private set; }
    public string? ReturnCarrierName { get; private set; }
    public string? ReturnTrackingNumber { get; private set; }
    public DateTimeOffset? ReturnShippedOnUtc { get; private set; }
    public string? ReplacementCarrierCode { get; private set; }
    public string? ReplacementCarrierName { get; private set; }
    public string? ReplacementTrackingNumber { get; private set; }
    public DateTimeOffset? ReplacementShippedOnUtc { get; private set; }
    public string? RejectionReason { get; private set; }
    public Guid? RefundRequestId { get; private set; }
    public string? RefundFailureReason { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public DateTimeOffset? CompletedOnUtc { get; private set; }
    /// <summary>When the current status was entered; after-sales timeouts count from here.</summary>
    public DateTimeOffset StatusChangedOnUtc { get; private set; }
    public ReplacementStockStatus ReplacementStock { get; private set; }
    public string? ReplacementStockFailure { get; private set; }
    public ReturnDisposition ReturnDisposition { get; private set; }
    public string? DispositionNote { get; private set; }
    public string? DisposedBy { get; private set; }
    public DateTimeOffset? DisposedOnUtc { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<AfterSalesLine> Lines => _lines;
    public IReadOnlyCollection<AfterSalesHistory> History => _history;

    /// <summary>Still holds its quantities: rejected and withdrawn requests release them for a new request.</summary>
    public bool IsActive => Status is not (AfterSalesStatus.Rejected or AfterSalesStatus.Cancelled);

    /// <summary>Waiting on the merchant, the customer or the payment provider.</summary>
    public bool IsOpen => IsActive && Status is not (AfterSalesStatus.Refunded or AfterSalesStatus.Completed);

    public bool IsRefundType => Type is AfterSalesType.RefundOnly or AfterSalesType.ReturnAndRefund;

    /// <summary>Types the order currently qualifies for; the storefront mirrors this rule but the server decides.</summary>
    public static IReadOnlyList<AfterSalesType> AllowedTypes(Order order, DateTimeOffset now)
    {
        if (order.Status != OrderStatus.Confirmed || order.PaymentId is null)
            return [];
        return order.FulfillmentStatus switch
        {
            // 未签收只能仅退款：商品还在仓库或路上，退货、换货无从谈起。
            OrderFulfillmentStatus.AwaitingShipment or OrderFulfillmentStatus.Shipped or OrderFulfillmentStatus.InTransit =>
                [AfterSalesType.RefundOnly],
            // 早期订单可能缺签收时间，用最后更新时间近似，与前台保持一致。
            OrderFulfillmentStatus.Delivered when now <= (order.DeliveredOnUtc ?? order.UpdatedOnUtc) + ReturnWindow =>
                [AfterSalesType.ReturnAndRefund, AfterSalesType.Exchange, AfterSalesType.RefundOnly, AfterSalesType.Reship],
            _ => []
        };
    }

    /// <param name="existing">Every other after-sales request of the same order, used to cap quantities and refunds.</param>
    public static AfterSalesRequest Submit(
        Guid id,
        Order order,
        IEnumerable<AfterSalesRequest> existing,
        string idempotencyKey,
        AfterSalesType type,
        AfterSalesReason reason,
        string? description,
        IReadOnlyCollection<AfterSalesItem> items,
        IReadOnlyCollection<string> evidenceObjectKeys,
        DateTimeOffset now)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("After-sales id is required.", nameof(id));
        if (!AllowedTypes(order, now).Contains(type))
            throw new AfterSalesRuleException("该订单当前不支持此售后类型，可能已超过售后期限。");
        if (items.Count == 0)
            throw new AfterSalesRuleException("请至少选择一件商品。");
        if (items.Select(item => item.Sku).Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new AfterSalesRuleException("同一商品只能选择一次。");
        if (evidenceObjectKeys.Count > MaxEvidenceCount
            || evidenceObjectKeys.Distinct(StringComparer.Ordinal).Count() != evidenceObjectKeys.Count)
            throw new AfterSalesRuleException($"最多上传 {MaxEvidenceCount} 张不重复的凭证图片。");
        var normalizedDescription = Optional(description, 500, nameof(description));
        if (reason == AfterSalesReason.Other && normalizedDescription is null)
            throw new AfterSalesRuleException("选择“其他原因”时请填写问题描述。");

        var request = new AfterSalesRequest
        {
            Id = id,
            RequestNumber = $"A{now:yyyyMMdd}{id:N}",
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            UserId = order.UserId,
            PaymentId = order.PaymentId!.Value,
            IdempotencyKey = Required(idempotencyKey, 100, nameof(idempotencyKey)),
            Type = type,
            InTransitAtSubmission = order.FulfillmentStatus is OrderFulfillmentStatus.Shipped or OrderFulfillmentStatus.InTransit,
            Reason = reason,
            Description = normalizedDescription,
            EvidenceObjectKeys = evidenceObjectKeys.ToArray(),
            Currency = order.Currency,
            Status = AfterSalesStatus.Requested,
            CreatedOnUtc = now,
            UpdatedOnUtc = now,
            StatusChangedOnUtc = now
        };

        var active = existing.Where(other => other.OrderId == order.Id && other.IsActive).ToArray();
        foreach (var item in items)
        {
            var line = order.Lines.SingleOrDefault(value => value.Sku == item.Sku)
                ?? throw new AfterSalesRuleException($"商品 {item.Sku} 不在该订单中。");
            if (item.Quantity <= 0)
                throw new AfterSalesRuleException("售后件数必须大于 0。");
            var claimed = active.SelectMany(other => other.Lines).Where(value => value.Sku == line.Sku).Sum(value => value.Quantity);
            if (claimed + item.Quantity > line.Quantity)
                throw new AfterSalesRuleException($"「{line.ProductName}」还可申请售后 {line.Quantity - claimed} 件，其余已在其他售后单中。");

            var refund = type is AfterSalesType.RefundOnly or AfterSalesType.ReturnAndRefund
                ? AllocateRefund(line, item.Quantity, active.Where(other => other.IsRefundType).SelectMany(other => other.Lines).Where(value => value.Sku == line.Sku))
                : 0m;
            request._lines.Add(new AfterSalesLine(line.Sku, line.ProductName, line.VariantName, item.Quantity, refund));
        }

        request.RefundAmount = Money.Round(request._lines.Sum(line => line.RefundAmount));
        if (request.IsRefundType && request.RefundAmount <= 0)
            throw new AfterSalesRuleException("所选商品没有可退的实付金额。");
        request._history.Add(new AfterSalesHistory(Guid.NewGuid(), id, null, AfterSalesStatus.Requested, normalizedDescription, order.UserId, now));
        request.AddDomainEvent(new AfterSalesStatusChangedDomainEvent(request, null, AfterSalesStatus.Requested, normalizedDescription, order.UserId, now));
        return request;
    }

    /// <summary>
    /// 按实付金额折算退款：部分退货按件数比例四舍五入，最后一批件数拿走剩余金额，
    /// 保证同一订单行多次部分退款的累计值恰好等于该行实付、永不超出。
    /// </summary>
    internal static decimal AllocateRefund(OrderLine line, int quantity, IEnumerable<AfterSalesLine> refundedBefore)
    {
        var previous = refundedBefore.ToArray();
        var previousQuantity = previous.Sum(value => value.Quantity);
        var remaining = line.LineAmount - previous.Sum(value => value.RefundAmount);
        return previousQuantity + quantity == line.Quantity
            ? remaining
            : Math.Min(Money.Round(line.LineAmount * quantity / line.Quantity), remaining);
    }

    public bool Cancel(string actor, DateTimeOffset now, string note = "买家撤销了售后申请")
    {
        if (Status == AfterSalesStatus.Cancelled)
            return false;
        // 商品寄出后撤销会让退货悬空，只允许在审核前或寄回前撤销。
        if (Status is not (AfterSalesStatus.Requested or AfterSalesStatus.AwaitingReturn))
            throw new AfterSalesRuleException("商品寄回后不能撤销申请，如有疑问请联系客服。");
        CompletedOnUtc = now;
        ReleaseReplacementStock();
        TransitionTo(AfterSalesStatus.Cancelled, note, actor, now);
        return true;
    }

    /// <summary>Rejects at review, or after inspecting returned goods; the reply is shown to the customer.</summary>
    public bool Reject(string reply, string actor, DateTimeOffset now)
    {
        var normalizedReply = Required(reply, 500, nameof(reply));
        if (Status == AfterSalesStatus.Rejected)
            return false;
        if (Status is not (AfterSalesStatus.Requested or AfterSalesStatus.ReturnReceived))
            throw new AfterSalesRuleException("只有待审核或已签收退货的售后单可以拒绝。");
        // 验货不通过意味着商品退回买家；已经做过入库处理的退货不能再拒绝，否则库存与实物对不上。
        if (Status == AfterSalesStatus.ReturnReceived)
        {
            if (ReturnDisposition != ReturnDisposition.Pending)
                throw new AfterSalesRuleException("退货已做入库处理，不能再拒绝。");
            ReturnDisposition = ReturnDisposition.ReturnedToCustomer;
        }
        RejectionReason = normalizedReply;
        ReleaseReplacementStock();
        CompletedOnUtc = now;
        TransitionTo(AfterSalesStatus.Rejected, normalizedReply, actor, now);
        return true;
    }

    /// <summary>Approves a pending request. A refund-only approval starts the refund immediately; check <see cref="Status"/>.</summary>
    public bool Approve(string? returnAddress, string? note, string actor, DateTimeOffset now)
    {
        if (Status is AfterSalesStatus.Rejected or AfterSalesStatus.Cancelled)
            throw new AfterSalesRuleException("售后单已关闭，不能审核通过。");
        // 已越过审核的请求重复审核视为重放成功，不再推进状态。
        if (Status != AfterSalesStatus.Requested)
            return false;

        var normalizedNote = Optional(note, 1000, nameof(note));
        switch (Type)
        {
            case AfterSalesType.RefundOnly:
                BeginRefund(normalizedNote ?? "审核通过，已发起原路退款", actor, now);
                break;
            case AfterSalesType.ReturnAndRefund or AfterSalesType.Exchange:
                ReturnAddress = Required(returnAddress!, 500, nameof(returnAddress));
                // 换货在审核时就锁定新品：买家寄回期间新品不会被其他订单买走。
                if (Type == AfterSalesType.Exchange)
                    RequestReplacementStock(actor);
                TransitionTo(AfterSalesStatus.AwaitingReturn, normalizedNote ?? "审核通过，请按退货地址寄回商品", actor, now);
                break;
            default:
                RequestReplacementStock(actor);
                TransitionTo(AfterSalesStatus.Approved, normalizedNote ?? "审核通过，商家将尽快补寄", actor, now);
                break;
        }
        return true;
    }

    /// <summary>Records (or corrects) the customer's return waybill.</summary>
    public bool SubmitReturnShipment(string carrierName, string trackingNumber, string actor, DateTimeOffset now)
    {
        var carrier = Required(carrierName, 120, nameof(carrierName));
        var tracking = Required(trackingNumber, 120, nameof(trackingNumber)).ToUpperInvariant();
        if (Status is not (AfterSalesStatus.AwaitingReturn or AfterSalesStatus.ReturnInTransit))
            throw new AfterSalesRuleException("当前状态不需要填写寄回运单。");
        if (Status == AfterSalesStatus.ReturnInTransit && ReturnCarrierName == carrier && ReturnTrackingNumber == tracking)
            return false;

        ReturnCarrierName = carrier;
        ReturnTrackingNumber = tracking;
        ReturnShippedOnUtc = now;
        TransitionTo(AfterSalesStatus.ReturnInTransit, $"{carrier} {tracking}", actor, now);
        return true;
    }

    public bool ConfirmReturnReceived(string? note, string actor, DateTimeOffset now)
    {
        if (Status == AfterSalesStatus.ReturnReceived)
            return false;
        // 买家没填运单但商家已收到货也允许签收，避免流程卡死。
        if (Status is not (AfterSalesStatus.AwaitingReturn or AfterSalesStatus.ReturnInTransit))
            throw new AfterSalesRuleException("当前状态不能确认收到退货。");
        ReturnDisposition = ReturnDisposition.Pending;
        TransitionTo(AfterSalesStatus.ReturnReceived, Optional(note, 1000, nameof(note)) ?? "商家已签收退回的商品", actor, now);
        return true;
    }

    /// <summary>Refunds inspected returned goods, or retries a failed refund under a new refund request id.</summary>
    public bool StartRefund(string? note, string actor, DateTimeOffset now)
    {
        if (Status == AfterSalesStatus.Refunding)
            return false;
        var allowed = Type == AfterSalesType.ReturnAndRefund && Status == AfterSalesStatus.ReturnReceived
            || IsRefundType && Status == AfterSalesStatus.RefundFailed;
        if (!allowed)
            throw new AfterSalesRuleException("当前状态不能发起退款。");
        BeginRefund(Optional(note, 1000, nameof(note)) ?? "已发起原路退款", actor, now);
        return true;
    }

    /// <summary>Applies the payment acknowledgement; acknowledgements for a superseded refund attempt are ignored.</summary>
    public bool MarkRefunded(Guid refundRequestId, DateTimeOffset now)
    {
        if (RefundRequestId != refundRequestId || Status != AfterSalesStatus.Refunding)
            return false;
        RefundFailureReason = null;
        CompletedOnUtc = now;
        TransitionTo(AfterSalesStatus.Refunded, "退款已原路退回", "system", now);
        return true;
    }

    public bool MarkRefundFailed(Guid refundRequestId, string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (RefundRequestId != refundRequestId || Status != AfterSalesStatus.Refunding)
            return false;
        var normalized = reason.Trim();
        RefundFailureReason = normalized[..Math.Min(normalized.Length, 1000)];
        TransitionTo(AfterSalesStatus.RefundFailed, "退款失败，商家处理中", "system", now);
        return true;
    }

    public bool ShipReplacement(string carrierCode, string carrierName, string trackingNumber, string? note, string actor, DateTimeOffset now)
    {
        var code = Required(carrierCode, 50, nameof(carrierCode)).ToUpperInvariant();
        var name = Required(carrierName, 120, nameof(carrierName));
        var tracking = Required(trackingNumber, 120, nameof(trackingNumber)).ToUpperInvariant();
        if (Status == AfterSalesStatus.ReplacementShipped && ReplacementTrackingNumber == tracking)
            return false;
        // 补寄审核通过即可发；换货必须先签收退回的商品再发新品。
        var allowed = Type == AfterSalesType.Reship && Status == AfterSalesStatus.Approved
            || Type == AfterSalesType.Exchange && Status == AfterSalesStatus.ReturnReceived;
        if (!allowed)
            throw new AfterSalesRuleException("当前状态不能发出换货或补寄商品。");
        if (ReplacementStock != ReplacementStockStatus.Reserved)
            throw new AfterSalesRuleException("新品库存尚未锁定成功，请先在售后详情中处理库存。");

        ReplacementStock = ReplacementStockStatus.Committed;
        RaiseStock(AfterSalesStockOperation.Commit, AllLines(), actor);
        ReplacementCarrierCode = code;
        ReplacementCarrierName = name;
        ReplacementTrackingNumber = tracking;
        ReplacementShippedOnUtc = now;
        TransitionTo(AfterSalesStatus.ReplacementShipped, Optional(note, 1000, nameof(note)) ?? $"{name} {tracking}", actor, now);
        return true;
    }

    /// <summary>Closes an exchange or reship once the customer has the replacement.</summary>
    public bool Complete(string note, string actor, DateTimeOffset now)
    {
        if (Status == AfterSalesStatus.Completed)
            return false;
        if (Status != AfterSalesStatus.ReplacementShipped)
            throw new AfterSalesRuleException("商品发出后才能确认完成。");
        CompletedOnUtc = now;
        TransitionTo(AfterSalesStatus.Completed, Required(note, 1000, nameof(note)), actor, now);
        return true;
    }

    /// <summary>Applies Inventory's answer to the replacement stock hold; duplicates and stale answers are ignored.</summary>
    public bool ApplyStockReservation(bool reserved, string? reason, DateTimeOffset now)
    {
        if (ReplacementStock != ReplacementStockStatus.Reserving)
            return false;
        UpdatedOnUtc = now;
        Version++;
        if (!reserved)
        {
            ReplacementStock = ReplacementStockStatus.Insufficient;
            var failure = string.IsNullOrWhiteSpace(reason) ? "库存不足" : reason.Trim();
            ReplacementStockFailure = failure[..Math.Min(failure.Length, 500)];
            return true;
        }

        ReplacementStockFailure = null;
        // 锁定结果回来前售后单已关闭：立即归还，避免库存被永久占住。
        if (!IsActive)
        {
            ReplacementStock = ReplacementStockStatus.Released;
            RaiseStock(AfterSalesStockOperation.Release, AllLines(), "system");
            return true;
        }
        ReplacementStock = ReplacementStockStatus.Reserved;
        return true;
    }

    /// <summary>Asks Inventory again after a shortage, for example once stock has been replenished.</summary>
    public bool RetryReplacementStock(string actor, DateTimeOffset now)
    {
        if (ReplacementStock is ReplacementStockStatus.Reserving or ReplacementStockStatus.Reserved)
            return false;
        if (ReplacementStock != ReplacementStockStatus.Insufficient || !IsOpen)
            throw new AfterSalesRuleException("当前没有需要重新锁定的新品库存。");
        RequestReplacementStock(actor);
        UpdatedOnUtc = now;
        Version++;
        return true;
    }

    /// <summary>
    /// 退货入库处理：验货后决定每件商品是否回到可售库存；未入库的件数必须写明原因（破损、缺件等），一经处理不可更改。
    /// </summary>
    public bool DisposeReturn(IReadOnlyCollection<AfterSalesItem> restock, string? note, string actor, DateTimeOffset now)
    {
        if (ReturnDisposition is ReturnDisposition.Restocked or ReturnDisposition.PartiallyRestocked or ReturnDisposition.NotRestocked)
            return false;
        if (ReturnDisposition != ReturnDisposition.Pending)
            throw new AfterSalesRuleException("当前没有待入库处理的退货。");
        if (restock.Select(item => item.Sku).Distinct(StringComparer.Ordinal).Count() != restock.Count)
            throw new AfterSalesRuleException("同一商品只能填写一次入库数量。");
        if (restock.FirstOrDefault(item => _lines.All(line => line.Sku != item.Sku)) is { } unknown)
            throw new AfterSalesRuleException($"商品 {unknown.Sku} 不在该售后单中。");

        var quantities = restock.ToDictionary(item => item.Sku, item => item.Quantity, StringComparer.Ordinal);
        foreach (var line in _lines)
        {
            var quantity = quantities.GetValueOrDefault(line.Sku);
            if (quantity < 0 || quantity > line.Quantity)
                throw new AfterSalesRuleException($"「{line.ProductName}」入库数量必须在 0 到 {line.Quantity} 之间。");
        }
        var normalizedNote = Optional(note, 500, nameof(note));
        var restocked = _lines.Sum(line => quantities.GetValueOrDefault(line.Sku));
        var total = _lines.Sum(line => line.Quantity);
        if (restocked < total && normalizedNote is null)
            throw new AfterSalesRuleException("有商品不入库时，请注明原因（如破损、缺件、影响二次销售）。");

        foreach (var line in _lines)
            line.Restock(quantities.GetValueOrDefault(line.Sku));
        ReturnDisposition = restocked == total ? ReturnDisposition.Restocked
            : restocked == 0 ? ReturnDisposition.NotRestocked
            : ReturnDisposition.PartiallyRestocked;
        DispositionNote = normalizedNote;
        DisposedBy = Required(actor, 200, nameof(actor));
        DisposedOnUtc = now;
        UpdatedOnUtc = now;
        Version++;
        if (restocked > 0)
        {
            RaiseStock(
                AfterSalesStockOperation.Restock,
                _lines.Where(line => line.RestockedQuantity > 0).Select(line => new AfterSalesItem(line.Sku, line.RestockedQuantity!.Value)),
                actor);
        }
        return true;
    }

    /// <summary>
    /// 仅退款的商品若从未发出，退款到账即全部回库：付款时库存已按出库扣减，不补回会少算实物。
    /// </summary>
    public bool RestockUnshippedGoods(DateTimeOffset now)
    {
        if (Type != AfterSalesType.RefundOnly || Status != AfterSalesStatus.Refunded || ReturnDisposition != ReturnDisposition.NotApplicable)
            return false;
        ReturnDisposition = ReturnDisposition.Pending;
        return DisposeReturn(AllLines().ToArray(), "订单未发货，退款后商品自动回库", "system", now);
    }

    /// <summary>
    /// 整单仅退款后包裹被拒收退回：转为待入库处理，仓库收到包裹后沿用退货入库流程验货回库。
    /// </summary>
    public bool AwaitReturnedParcel(DateTimeOffset now)
    {
        if (Type != AfterSalesType.RefundOnly || Status != AfterSalesStatus.Refunded || ReturnDisposition != ReturnDisposition.NotApplicable)
            return false;
        ReturnDisposition = ReturnDisposition.Pending;
        UpdatedOnUtc = now;
        Version++;
        return true;
    }

    private IEnumerable<AfterSalesItem> AllLines() => _lines.Select(line => new AfterSalesItem(line.Sku, line.Quantity));

    private void RequestReplacementStock(string actor)
    {
        ReplacementStock = ReplacementStockStatus.Reserving;
        ReplacementStockFailure = null;
        RaiseStock(AfterSalesStockOperation.Reserve, AllLines(), actor);
    }

    private void ReleaseReplacementStock()
    {
        // 还在等锁定结果的由 ApplyStockReservation 归还；没锁到或已发出的无需处理。
        if (ReplacementStock != ReplacementStockStatus.Reserved)
            return;
        ReplacementStock = ReplacementStockStatus.Released;
        RaiseStock(AfterSalesStockOperation.Release, AllLines(), "system");
    }

    private void RaiseStock(AfterSalesStockOperation operation, IEnumerable<AfterSalesItem> items, string actor) =>
        AddDomainEvent(new AfterSalesStockRequestedDomainEvent(Id, RequestNumber, operation, items.ToArray(), actor));

    private void BeginRefund(string note, string actor, DateTimeOffset now)
    {
        // 每次发起（含失败重试）用新的退款请求号：支付侧按它幂等，旧请求的失败回执不会覆盖新尝试。
        RefundRequestId = Guid.NewGuid();
        RefundFailureReason = null;
        AddDomainEvent(new AfterSalesRefundRequestedDomainEvent(RefundRequestId.Value, OrderId, PaymentId, RefundAmount, Currency, RequestNumber));
        TransitionTo(AfterSalesStatus.Refunding, note, actor, now);
    }

    private void TransitionTo(AfterSalesStatus next, string? note, string actor, DateTimeOffset now)
    {
        var previous = Status;
        Status = next;
        UpdatedOnUtc = now;
        StatusChangedOnUtc = now;
        Version++;
        _history.Add(new AfterSalesHistory(Guid.NewGuid(), Id, previous, next, note, Required(actor, 200, nameof(actor)), now));
        AddDomainEvent(new AfterSalesStatusChangedDomainEvent(this, previous, next, note, actor, now));
    }

    private static string Required(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
    }

    private static string? Optional(string? value, int maximumLength, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, maximumLength, parameterName);
}

public sealed record AfterSalesItem(string Sku, int Quantity);

/// <summary>An after-sales rule rejected the operation; the message is written for customers and operators.</summary>
public sealed class AfterSalesRuleException(string message) : InvalidOperationException(message);

public sealed class AfterSalesLine
{
    private AfterSalesLine()
    {
    }

    public AfterSalesLine(string sku, string productName, string variantName, int quantity, decimal refundAmount)
    {
        Sku = sku;
        ProductName = productName;
        VariantName = variantName;
        Quantity = quantity;
        RefundAmount = refundAmount;
    }

    public long Id { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public string VariantName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal RefundAmount { get; private set; }
    /// <summary>Units put back into sellable stock after inspection; null until the return is disposed.</summary>
    public int? RestockedQuantity { get; private set; }

    internal void Restock(int quantity) => RestockedQuantity = quantity;
}

public sealed class AfterSalesHistory
{
    private AfterSalesHistory()
    {
    }

    public AfterSalesHistory(
        Guid id,
        Guid requestId,
        AfterSalesStatus? fromStatus,
        AfterSalesStatus toStatus,
        string? note,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        Id = id;
        RequestId = requestId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Note = note;
        Actor = actor;
        OccurredOnUtc = occurredOnUtc;
    }

    public Guid Id { get; private set; }
    public Guid RequestId { get; private set; }
    public AfterSalesStatus? FromStatus { get; private set; }
    public AfterSalesStatus ToStatus { get; private set; }
    public string? Note { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public DateTimeOffset OccurredOnUtc { get; private set; }
}

public enum AfterSalesType
{
    RefundOnly = 0,
    ReturnAndRefund = 1,
    Exchange = 2,
    Reship = 3
}

public enum AfterSalesStatus
{
    Requested = 0,
    /// <summary>Reship approved; waiting for the merchant to ship.</summary>
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,
    AwaitingReturn = 4,
    ReturnInTransit = 5,
    ReturnReceived = 6,
    Refunding = 7,
    RefundFailed = 8,
    Refunded = 9,
    ReplacementShipped = 10,
    Completed = 11
}

public enum ReplacementStockStatus
{
    NotRequired = 0,
    Reserving = 1,
    Reserved = 2,
    Insufficient = 3,
    Committed = 4,
    Released = 5
}

public enum ReturnDisposition
{
    NotApplicable = 0,
    Pending = 1,
    Restocked = 2,
    PartiallyRestocked = 3,
    NotRestocked = 4,
    /// <summary>Inspection failed and the goods go back to the customer.</summary>
    ReturnedToCustomer = 5
}

public enum AfterSalesStockOperation
{
    Reserve,
    Commit,
    Release,
    Restock
}

/// <param name="Status">Captured when raised: one transaction may move the request through several states.</param>
public sealed record AfterSalesStatusChangedDomainEvent(
    AfterSalesRequest Request,
    AfterSalesStatus? PreviousStatus,
    AfterSalesStatus Status,
    string? Note,
    string Actor,
    DateTimeOffset ChangedOnUtc) : DomainEvent;

public sealed record AfterSalesRefundRequestedDomainEvent(
    Guid RefundRequestId,
    Guid OrderId,
    Guid PaymentId,
    decimal Amount,
    string Currency,
    string RequestNumber) : DomainEvent;

public sealed record AfterSalesStockRequestedDomainEvent(
    Guid RequestId,
    string RequestNumber,
    AfterSalesStockOperation Operation,
    IReadOnlyList<AfterSalesItem> Items,
    string Actor) : DomainEvent;

public enum AfterSalesReason
{
    NoLongerNeeded = 0,
    QualityIssue = 1,
    DamagedInTransit = 2,
    WrongItem = 3,
    NotAsDescribed = 4,
    MissingParts = 5,
    NotReceived = 6,
    Other = 7
}
