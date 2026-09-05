using MediatR;
using Microsoft.Extensions.Options;
using MSA.InventoryService.Application.FlashSales;

namespace MSA.InventoryService.Infrastructure;

internal sealed class FlashSaleExpiryService(
    IServiceScopeFactory scopeFactory,
    IOptions<FlashSaleOptions> options,
    TimeProvider clock,
    ILogger<FlashSaleExpiryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CloseExpiredCampaignsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to close expired flash-sale campaigns.");
            }

            await Task.Delay(options.Value.ExpiryScanInterval, clock, stoppingToken);
        }
    }

    private async Task CloseExpiredCampaignsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var campaignIds = await sender.Send(
            new GetExpiredFlashSalesQuery(options.Value.ExpiryBatchSize),
            cancellationToken);

        foreach (var campaignId in campaignIds)
            await sender.Send(new CloseFlashSaleCommand(campaignId), cancellationToken);
    }
}
