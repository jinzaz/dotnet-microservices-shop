using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.OrderService.Application.AfterSales;

namespace MSA.OrderService.Infrastructure;

/// <summary>
/// 定时找出超时的售后单并逐个处理。每个售后单一个作用域与事务：单个失败只记日志，下一轮重试，不影响其他单据；
/// 多实例并发扫描时由订单行锁串行化，后到的实例加锁后发现已不再超时即跳过。
/// </summary>
internal sealed class AfterSalesTimeoutService(
    IServiceScopeFactory scopes,
    IOptions<AfterSalesOptions> options,
    TimeProvider clock,
    ILogger<AfterSalesTimeoutService> logger) : BackgroundService
{
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.ScanInterval, clock);
        do
        {
            try
            {
                await ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "After-sales timeout sweep failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        Guid[] due;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            due = await db.AfterSalesRequests
                .AsNoTracking()
                .Where(options.Value.Due(clock.GetUtcNow()))
                .OrderBy(request => request.StatusChangedOnUtc)
                .Take(BatchSize)
                .Select(request => request.Id)
                .ToArrayAsync(cancellationToken);
        }

        foreach (var id in due)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new AfterSalesTimeoutCommand(id), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Automatic handling of after-sales request {RequestId} failed.", id);
            }
        }
    }
}
