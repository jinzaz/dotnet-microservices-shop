using Microsoft.Extensions.Options;
using MSA.Contracts;
using MSA.EventBus.Abstractions;
using MSA.PaymentService.Application;
using MSA.PaymentService.Infrastructure;

namespace MSA.PaymentService.Integration;

/// <summary>
/// 新用户注册即开户并赠送余额。开关关闭时直接确认消息、不补发，之后打开也只对新注册的用户生效。
/// </summary>
internal sealed class UserRegisteredHandler(
    PaymentDbContext db,
    BalanceLedger ledger,
    TimeProvider clock,
    IOptions<BalanceWelcomeOptions> welcome)
    : IIntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    private const string ConsumerId = "payment.user-registered.v1";

    public async Task HandleAsync(
        UserRegisteredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        if (!welcome.Value.Enabled)
            return;

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await db.TryBeginMessageAsync(integrationEvent.Id, ConsumerId, cancellationToken))
            {
                await db.RollbackTransactionAsync(cancellationToken);
                return;
            }

            await ledger.GrantWelcomeAsync(
                integrationEvent.UserId,
                welcome.Value.Amount,
                clock.GetUtcNow(),
                cancellationToken);
            await db.CommitTransactionAsync(transaction, cancellationToken);
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
