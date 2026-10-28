using Microsoft.EntityFrameworkCore;
using MSA.Persistence.Postgres;
using Microsoft.Extensions.Options;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.InventoryService.Application;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Integration;

namespace MSA.InventoryService.Infrastructure;

/// <summary>
/// 预占过期兜底：订单侧的取消消息可能永远到不了（进了死信队列、或订单服务长时间不可用），
/// 没有这个清理，库存会被永久锁死。过期时间取订单的支付截止时间再加宽限期（<see cref="InventoryReservationOptions.ExpiryGracePeriod"/>），
/// 到点仍未提交说明订单已不可能再确认，即可释放；宽限期是给积压后晚到的提交库存消息留的余量。
/// </summary>
internal sealed class InventoryReservationExpiryService(
    IServiceScopeFactory scopes,
    IOptions<InventoryReservationOptions> options,
    TimeProvider clock,
    ILogger<InventoryReservationExpiryService> logger) : BackgroundService
{
    private const string Reason = "Reservation expired before the order was paid.";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.ExpiryScanInterval, clock);
        do
        {
            try
            {
                await ReleaseExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Expired reservation sweep failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ReleaseExpiredAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        Guid[] expired;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            expired = await db.Reservations
                .AsNoTracking()
                .Where(value => value.Status == ReservationStatus.Reserved && value.ExpiresOnUtc <= now)
                .OrderBy(value => value.ExpiresOnUtc)
                .Take(Math.Clamp(options.Value.ExpiryBatchSize, 1, 500))
                .Select(value => value.Id)
                .ToArrayAsync(cancellationToken);
        }

        // 每个预占一个事务与一个 DbContext：单个失败不会影响其他预占的释放。
        foreach (var reservationId in expired)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            try
            {
                await ReleaseAsync(scope.ServiceProvider, db, reservationId, clock.GetUtcNow(), cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Releasing expired reservation {ReservationId} failed.", reservationId);
            }
        }
    }

    private async Task ReleaseAsync(
        IServiceProvider services,
        InventoryDbContext db,
        Guid reservationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var transactionLock = services.GetRequiredService<IInventoryTransactionLock>();
        var outbox = services.GetRequiredService<IOutboxStore>();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            var reservation = await db.Reservations
                .Where(value => value.Id == reservationId)
                .ForUpdate()
                .SingleOrDefaultAsync(cancellationToken);
            if (reservation is null)
            {
                await db.RollbackTransactionAsync(cancellationToken);
                return;
            }

            await db.Entry(reservation).Collection(value => value.Lines).LoadAsync(cancellationToken);
            // 加锁后重新判断：释放消息可能刚刚处理完，或订单已经支付并提交了库存。
            if (reservation.Status != ReservationStatus.Reserved || reservation.ExpiresOnUtc > now)
            {
                await db.RollbackTransactionAsync(cancellationToken);
                return;
            }

            await InventoryRelease.ApplyAsync(db, transactionLock, reservation, Reason, now, cancellationToken);
            reservation.TryRelease(now, Reason);
            // 通知订单侧，让还在补偿中的 Saga 能够收敛。
            outbox.AddIntegrationEvent(new InventoryReleasedIntegrationEvent
            {
                OrderId = reservation.OrderId,
                ReservationId = reservation.Id
            });
            await db.CommitTransactionAsync(transaction, cancellationToken);
            logger.LogWarning(
                "Released expired reservation {ReservationId} of order {OrderId}.",
                reservation.Id,
                reservation.OrderId);
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
