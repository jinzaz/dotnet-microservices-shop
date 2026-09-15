using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Infrastrucute.Mediator;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Infrastructure;
using System.Security.Cryptography;
using System.Text.Json;

namespace MSA.InventoryService.Application;

public sealed record AdjustStockCommand(
    string Sku,
    int QuantityDelta,
    string Reason,
    string IdempotencyKey,
    string OperatorId) : ICommand<AdjustStockResult>;

public sealed record AdjustStockResult(
    Guid MovementId,
    Guid StockItemId,
    string Sku,
    int OnHand,
    int Reserved,
    int Available,
    bool Replayed);

public sealed class AdjustStockCommandValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockCommandValidator()
    {
        RuleFor(command => command.Sku).NotEmpty().MaximumLength(80);
        RuleFor(command => command.QuantityDelta).NotEqual(0);
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(500);
        RuleFor(command => command.IdempotencyKey).NotEmpty().MaximumLength(100);
        RuleFor(command => command.OperatorId).NotEmpty().MaximumLength(200);
    }
}

internal sealed class AdjustStockCommandHandler(
    InventoryDbContext db,
    IInventoryTransactionLock transactionLock,
    InventoryCacheInvalidator cache,
    TimeProvider clock) : IRequestHandler<AdjustStockCommand, AdjustStockResult>
{
    public async Task<AdjustStockResult> Handle(
        AdjustStockCommand request,
        CancellationToken cancellationToken)
    {
        var sku = request.Sku.Trim().ToUpperInvariant();
        var idempotencyKey = request.IdempotencyKey.Trim();
        var reason = request.Reason.Trim();
        var operatorId = request.OperatorId.Trim();
        var requestHash = StockAdjustmentRequestHash.Compute(sku, request.QuantityDelta, reason, operatorId);

        await transactionLock.AcquireStockAdjustmentAsync(
            sku,
            operatorId,
            idempotencyKey,
            cancellationToken);

        var existing = await db.StockMovements
            .AsNoTracking()
            .SingleOrDefaultAsync(
                movement => movement.OperatorId == operatorId
                    && movement.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existing is not null)
        {
            if (existing.Type != StockMovementType.StockAdjusted
                || !string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The idempotency key is already bound to a different stock adjustment.");
            }

            return ToResult(existing, replayed: true);
        }

        var stockItem = await db.StockItems
            .SingleOrDefaultAsync(item => item.Sku == sku, cancellationToken);
        if (stockItem is null)
        {
            if (request.QuantityDelta < 0)
                throw new KeyNotFoundException($"Inventory item '{sku}' was not found.");

            stockItem = new StockItem(Guid.NewGuid(), sku, request.QuantityDelta);
            db.StockItems.Add(stockItem);
        }
        else
        {
            stockItem.Adjust(request.QuantityDelta);
        }

        var movement = StockMovement.Record(
            stockItem,
            StockMovementType.StockAdjusted,
            request.QuantityDelta,
            reservedDelta: 0,
            StockMovementReferenceType.None,
            referenceId: null,
            reason,
            clock.GetUtcNow(),
            operatorId,
            idempotencyKey,
            requestHash);
        db.StockMovements.Add(movement);

        await cache.RemoveStockAsync(sku, cancellationToken);
        return ToResult(movement, replayed: false);
    }

    private static AdjustStockResult ToResult(StockMovement movement, bool replayed) =>
        new(
            movement.Id,
            movement.StockItemId,
            movement.Sku,
            movement.OnHandAfter,
            movement.ReservedAfter,
            movement.OnHandAfter - movement.ReservedAfter,
            replayed);
}

internal static class StockAdjustmentRequestHash
{
    public static string Compute(string sku, int quantityDelta, string reason, string operatorId)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Sku = sku,
            QuantityDelta = quantityDelta,
            Reason = reason,
            OperatorId = operatorId
        });
        return Convert.ToHexString(SHA256.HashData(payload));
    }
}

public sealed record GetStockMovementsQuery(string Sku, int Take)
    : IQuery<IReadOnlyList<StockMovementDto>>;

public sealed record StockMovementDto(
    Guid Id,
    Guid StockItemId,
    string Sku,
    StockMovementType Type,
    int OnHandDelta,
    int ReservedDelta,
    int OnHandAfter,
    int ReservedAfter,
    StockMovementReferenceType ReferenceType,
    Guid? ReferenceId,
    string Reason,
    string? OperatorId,
    DateTimeOffset OccurredOnUtc);

internal sealed class GetStockMovementsQueryHandler(InventoryDbContext db)
    : IRequestHandler<GetStockMovementsQuery, IReadOnlyList<StockMovementDto>>
{
    public async Task<IReadOnlyList<StockMovementDto>> Handle(
        GetStockMovementsQuery request,
        CancellationToken cancellationToken)
    {
        var sku = request.Sku.Trim().ToUpperInvariant();
        var take = Math.Clamp(request.Take, 1, 200);
        return await db.StockMovements
            .AsNoTracking()
            .Where(movement => movement.Sku == sku)
            .OrderByDescending(movement => movement.OccurredOnUtc)
            .ThenByDescending(movement => movement.Id)
            .Take(take)
            .Select(movement => new StockMovementDto(
                movement.Id,
                movement.StockItemId,
                movement.Sku,
                movement.Type,
                movement.OnHandDelta,
                movement.ReservedDelta,
                movement.OnHandAfter,
                movement.ReservedAfter,
                movement.ReferenceType,
                movement.ReferenceId,
                movement.Reason,
                movement.OperatorId,
                movement.OccurredOnUtc))
            .ToArrayAsync(cancellationToken);
    }
}
