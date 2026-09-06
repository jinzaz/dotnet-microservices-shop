using MSA.BasketService.Application.Abstractions;
using MSA.BasketService.Infrastructure;
using MSA.Contracts;

namespace MSA.BasketService.Integration;

internal sealed class OrderSubmittedHandler(
    BasketDbContext db,
    IBasketRepository baskets,
    IBasketMutationLock mutationLock,
    TimeProvider clock,
    ILogger<OrderSubmittedHandler> logger)
    : BasketIntegrationEventHandler<OrderSubmittedIntegrationEvent>(
        db,
        "basket.order-submitted.v1")
{
    protected override async Task HandleCoreAsync(
        OrderSubmittedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (!integrationEvent.SourceBasketId.HasValue
            && !integrationEvent.SourceBasketVersion.HasValue)
        {
            return;
        }
        if (integrationEvent.SourceBasketId is not Guid basketId
            || basketId == Guid.Empty
            || integrationEvent.SourceBasketVersion is not long submittedVersion
            || submittedVersion < 0
            || string.IsNullOrWhiteSpace(integrationEvent.UserId))
        {
            throw new InvalidOperationException("OrderSubmitted contains invalid source basket metadata.");
        }

        await mutationLock.AcquireAsync(integrationEvent.UserId, cancellationToken);
        var basket = await baskets.FindByUserAsync(integrationEvent.UserId, cancellationToken);
        if (basket is null || basket.Id != basketId)
        {
            logger.LogInformation(
                "Skipped checkout cleanup for order {OrderId}: source basket {BasketId} is not owned by user {UserId}.",
                integrationEvent.OrderId,
                basketId,
                integrationEvent.UserId);
            return;
        }
        if (basket.Version != submittedVersion)
        {
            logger.LogInformation(
                "Skipped checkout cleanup for order {OrderId}: basket {BasketId} advanced from submitted version {SubmittedVersion} to {CurrentVersion}.",
                integrationEvent.OrderId,
                basketId,
                submittedVersion,
                basket.Version);
            return;
        }

        basket.Clear(submittedVersion, clock.GetUtcNow());
    }
}
