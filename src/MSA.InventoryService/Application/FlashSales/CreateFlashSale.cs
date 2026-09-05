using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.Infrastrucute.Mediator;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Infrastructure;
using System.Text.Json;

namespace MSA.InventoryService.Application.FlashSales;

public sealed record CreateFlashSaleCommand(
    string Sku,
    int TotalQuantity,
    DateTimeOffset StartsOnUtc,
    DateTimeOffset EndsOnUtc,
    int PurchaseLimit,
    FlashSaleStockStrategy? Strategy,
    int? BucketCount) : ICommand<Guid>;

public sealed class CreateFlashSaleCommandValidator : AbstractValidator<CreateFlashSaleCommand>
{
    public CreateFlashSaleCommandValidator(IOptions<FlashSaleOptions> options)
    {
        RuleFor(command => command.Sku).NotEmpty().MaximumLength(80);
        RuleFor(command => command.TotalQuantity).InclusiveBetween(1, 10_000_000);
        RuleFor(command => command.PurchaseLimit).GreaterThan(0);
        RuleFor(command => command)
            .Must(command => command.PurchaseLimit <= command.TotalQuantity)
            .WithMessage("PurchaseLimit cannot exceed TotalQuantity.");
        RuleFor(command => command)
            .Must(command => command.StartsOnUtc < command.EndsOnUtc)
            .WithMessage("EndsOnUtc must be after StartsOnUtc.");
        RuleFor(command => command.BucketCount)
            .InclusiveBetween(2, options.Value.MaximumBucketCount)
            .When(command => command.BucketCount.HasValue && command.Strategy != FlashSaleStockStrategy.SingleRow);
        RuleFor(command => command)
            .Must(command => !command.BucketCount.HasValue || command.BucketCount <= command.TotalQuantity)
            .WithMessage("BucketCount cannot exceed TotalQuantity.");
        RuleFor(command => command)
            .Must(command =>
                !command.BucketCount.HasValue
                || command.PurchaseLimit <= 0
                || (long)command.BucketCount.Value * command.PurchaseLimit <= command.TotalQuantity)
            .WithMessage("Every stock bucket must hold at least one maximum-size purchase.");
        RuleFor(command => command)
            .Must(command =>
                command.Strategy != FlashSaleStockStrategy.Bucketed
                || command.PurchaseLimit <= 0
                || command.TotalQuantity / command.PurchaseLimit >= 2)
            .WithMessage("Bucketed stock requires enough inventory for at least two maximum-size purchases.");
        RuleFor(command => command)
            .Must(command => command.Strategy != FlashSaleStockStrategy.SingleRow || command.BucketCount is null or 1)
            .WithMessage("SingleRow strategy uses exactly one stock bucket.");
    }
}

internal sealed class CreateFlashSaleCommandHandler(
    InventoryDbContext db,
    IInventoryTransactionLock transactionLock,
    IOutboxStore outbox,
    IOptions<FlashSaleOptions> options,
    InventoryCacheInvalidator cache,
    TimeProvider clock) : IRequestHandler<CreateFlashSaleCommand, Guid>
{
    public async Task<Guid> Handle(CreateFlashSaleCommand request, CancellationToken cancellationToken)
    {
        var sku = request.Sku.Trim();
        await transactionLock.AcquireStockItemsAsync([sku], cancellationToken);

        var stockItem = await db.StockItems.SingleOrDefaultAsync(item => item.Sku == sku, cancellationToken)
            ?? throw new KeyNotFoundException($"Inventory item '{sku}' was not found.");

        stockItem.Reserve(request.TotalQuantity);

        var supportsBucketedStock = request.TotalQuantity / request.PurchaseLimit >= 2;
        var strategy = request.Strategy
            ?? (request.BucketCount > 1
                ? FlashSaleStockStrategy.Bucketed
                : request.TotalQuantity > options.Value.LargeStockThreshold && supportsBucketedStock
                    ? FlashSaleStockStrategy.Bucketed
                    : FlashSaleStockStrategy.SingleRow);
        var bucketCount = ResolveBucketCount(request, strategy, options.Value);
        var now = clock.GetUtcNow();
        var campaign = FlashSaleCampaign.Create(
            Guid.NewGuid(),
            sku,
            request.TotalQuantity,
            request.StartsOnUtc,
            request.EndsOnUtc,
            request.PurchaseLimit,
            strategy,
            bucketCount,
            now);

        db.FlashSaleCampaigns.Add(campaign);

        var integrationEvent = new FlashSaleCampaignOpenedIntegrationEvent
        {
            CampaignId = campaign.Id,
            Sku = campaign.Sku,
            StartsOnUtc = campaign.StartsOnUtc,
            EndsOnUtc = campaign.EndsOnUtc,
            PurchaseLimit = campaign.PurchaseLimit,
            Strategy = campaign.Strategy.ToString()
        };
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())));

        await cache.RemoveStockAsync(sku, cancellationToken);

        return campaign.Id;
    }

    private static int ResolveBucketCount(
        CreateFlashSaleCommand request,
        FlashSaleStockStrategy strategy,
        FlashSaleOptions options)
    {
        if (strategy == FlashSaleStockStrategy.SingleRow)
            return 1;

        var requested = request.BucketCount ?? options.DefaultBucketCount;
        var capacityBound = request.TotalQuantity / request.PurchaseLimit;
        return Math.Clamp(requested, 2, Math.Min(options.MaximumBucketCount, capacityBound));
    }
}
