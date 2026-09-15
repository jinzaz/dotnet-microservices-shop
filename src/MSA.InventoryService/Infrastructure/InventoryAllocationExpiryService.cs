using MediatR;
using Microsoft.Extensions.Options;
using MSA.InventoryService.Application.Allocations;

namespace MSA.InventoryService.Infrastructure;

internal sealed class InventoryAllocationExpiryService(
    IServiceScopeFactory scopeFactory,
    IOptions<InventoryAllocationOptions> options,
    TimeProvider clock,
    ILogger<InventoryAllocationExpiryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CloseExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to close expired inventory allocations.");
            }

            await Task.Delay(options.Value.ExpiryScanInterval, clock, stoppingToken);
        }
    }

    private async Task CloseExpiredAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var ids = await sender.Send(
            new GetExpiredInventoryAllocationsQuery(options.Value.ExpiryBatchSize),
            cancellationToken);
        foreach (var id in ids)
            await sender.Send(new CloseInventoryAllocationCommand(id), cancellationToken);
    }
}
