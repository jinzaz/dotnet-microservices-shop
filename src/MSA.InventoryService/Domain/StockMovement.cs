using MSA.Infrastrucute.Entities;

namespace MSA.InventoryService.Domain;

/// <summary>库存余额每次变化对应的不可变审计流水。</summary>
public sealed class StockMovement : Entity<Guid>
{
    private StockMovement()
    {
    }

    private StockMovement(
        StockItem stockItem,
        StockMovementType type,
        int onHandDelta,
        int reservedDelta,
        StockMovementReferenceType referenceType,
        Guid? referenceId,
        string reason,
        string? operatorId,
        string? idempotencyKey,
        string? requestHash,
        DateTimeOffset occurredOnUtc)
    {
        ArgumentNullException.ThrowIfNull(stockItem);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 500)
            throw new ArgumentException("Stock movement reason cannot exceed 500 characters.", nameof(reason));
        if (operatorId?.Length > 200)
            throw new ArgumentException("Operator id cannot exceed 200 characters.", nameof(operatorId));
        if (idempotencyKey?.Length > 100)
            throw new ArgumentException("Idempotency key cannot exceed 100 characters.", nameof(idempotencyKey));
        if (onHandDelta == 0 && reservedDelta == 0)
            throw new ArgumentException("A stock movement must change at least one balance.");
        if (referenceType == StockMovementReferenceType.None && referenceId.HasValue)
            throw new ArgumentException("A reference id requires a reference type.", nameof(referenceId));
        if (referenceType != StockMovementReferenceType.None && !referenceId.HasValue)
            throw new ArgumentException("The selected reference type requires a reference id.", nameof(referenceId));
        var hasIdempotencyKey = !string.IsNullOrWhiteSpace(idempotencyKey);
        var hasRequestHash = !string.IsNullOrWhiteSpace(requestHash);
        if (hasIdempotencyKey != hasRequestHash)
            throw new ArgumentException("Idempotency key and request hash must either both be supplied or both be omitted.");
        if (hasIdempotencyKey && string.IsNullOrWhiteSpace(operatorId))
            throw new ArgumentException("An idempotent stock movement requires an operator id.", nameof(operatorId));
        if (hasRequestHash && requestHash!.Length != 64)
            throw new ArgumentException("Request hash must be a SHA-256 hexadecimal value.", nameof(requestHash));

        Id = Guid.NewGuid();
        StockItemId = stockItem.Id;
        Sku = stockItem.Sku;
        Type = type;
        OnHandDelta = onHandDelta;
        ReservedDelta = reservedDelta;
        OnHandAfter = stockItem.OnHand;
        ReservedAfter = stockItem.Reserved;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        Reason = reason.Trim();
        OperatorId = string.IsNullOrWhiteSpace(operatorId) ? null : operatorId.Trim();
        IdempotencyKey = hasIdempotencyKey ? idempotencyKey!.Trim() : null;
        RequestHash = hasRequestHash ? requestHash : null;
        OccurredOnUtc = occurredOnUtc;
    }

    public Guid StockItemId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public StockMovementType Type { get; private set; }
    public int OnHandDelta { get; private set; }
    public int ReservedDelta { get; private set; }
    public int OnHandAfter { get; private set; }
    public int ReservedAfter { get; private set; }
    public StockMovementReferenceType ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? OperatorId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? RequestHash { get; private set; }
    public DateTimeOffset OccurredOnUtc { get; private set; }

    public static StockMovement Record(
        StockItem stockItem,
        StockMovementType type,
        int onHandDelta,
        int reservedDelta,
        StockMovementReferenceType referenceType,
        Guid? referenceId,
        string reason,
        DateTimeOffset occurredOnUtc,
        string? operatorId = null,
        string? idempotencyKey = null,
        string? requestHash = null) =>
        new(
            stockItem,
            type,
            onHandDelta,
            reservedDelta,
            referenceType,
            referenceId,
            reason,
            operatorId,
            idempotencyKey,
            requestHash,
            occurredOnUtc);
}

public enum StockMovementType
{
    StockAdded = 0,
    StockAdjusted = 1,
    InventoryReserved = 2,
    InventoryCommitted = 3,
    InventoryReleased = 4,
    AllocationCreated = 5,
    AllocationReleased = 6,
    ReturnRestocked = 7
}

public enum StockMovementReferenceType
{
    None = 0,
    Reservation = 1,
    InventoryAllocation = 2,
    AfterSales = 3
}
