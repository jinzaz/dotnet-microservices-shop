using MSA.BasketService.Infrastructure;
using MSA.EventBus.Abstractions;

namespace MSA.BasketService.Integration;

internal abstract class BasketIntegrationEventHandler<TEvent>(
    BasketDbContext db,
    string consumerId) : IIntegrationEventHandler<TEvent>
    where TEvent : IntegrationEvent
{
    public async Task HandleAsync(
        TEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await db.TryBeginMessageAsync(
                    integrationEvent.Id,
                    consumerId,
                    cancellationToken))
            {
                await db.RollbackTransactionAsync(cancellationToken);
                return;
            }

            await HandleCoreAsync(integrationEvent, cancellationToken);
            await db.CommitTransactionAsync(transaction, cancellationToken);
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    protected BasketDbContext Db => db;

    protected abstract Task HandleCoreAsync(
        TEvent integrationEvent,
        CancellationToken cancellationToken);
}
