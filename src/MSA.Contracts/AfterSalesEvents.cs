using MSA.EventBus.Abstractions;

namespace MSA.Contracts;

public sealed record AfterSalesStockItemContract(string Sku, int Quantity);

/// <summary>Inventory operations Ordering requests for an after-sales request (the request id is the reference).</summary>
public static class AfterSalesStockOperations
{
    /// <summary>Hold stock for an exchange or reship replacement; answered by <see cref="AfterSalesStockReservationResultIntegrationEvent"/>.</summary>
    public const string Reserve = "Reserve";
    /// <summary>The replacement shipped: the held stock leaves the warehouse.</summary>
    public const string Commit = "Commit";
    /// <summary>The request closed before shipping: return the held stock.</summary>
    public const string Release = "Release";
    /// <summary>Returned goods passed inspection and go back into sellable stock.</summary>
    public const string Restock = "Restock";
}

public sealed class AfterSalesStockRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid RequestId { get; init; }
    public required string RequestNumber { get; init; }
    public required string Operation { get; init; }
    public required IReadOnlyList<AfterSalesStockItemContract> Items { get; init; }
    public string? OperatorId { get; init; }
}

public sealed class AfterSalesStockReservationResultIntegrationEvent : IntegrationEvent
{
    public required Guid RequestId { get; init; }
    public required bool Reserved { get; init; }
    public string? Reason { get; init; }
}

public sealed class AfterSalesStatusChangedIntegrationEvent : IntegrationEvent
{
    public required Guid RequestId { get; init; }
    public required string RequestNumber { get; init; }
    public required Guid OrderId { get; init; }
    public required string OrderNumber { get; init; }
    public required string UserId { get; init; }
    public required string Type { get; init; }
    public string? PreviousStatus { get; init; }
    public required string Status { get; init; }
    public string? Note { get; init; }
    /// <summary>The customer made this change themselves, so there is nothing to tell them.</summary>
    public bool ByCustomer { get; init; }
    public required decimal RefundAmount { get; init; }
    public required string Currency { get; init; }
    public string? ReturnAddress { get; init; }
    public string? CarrierName { get; init; }
    public string? TrackingNumber { get; init; }
    public required DateTimeOffset OccurredOnUtc { get; init; }
}
