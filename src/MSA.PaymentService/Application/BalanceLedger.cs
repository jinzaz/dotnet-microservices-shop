using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Infrastrucute.Mediator;
using MSA.PaymentService.Domain;
using MSA.PaymentService.Infrastructure;

namespace MSA.PaymentService.Application;

/// <summary>
/// 余额记账。所有变动都先锁账户行（FOR UPDATE）再改余额、写流水，并且与调用方的支付单 / 退款单 / Outbox
/// 在同一个本地事务里提交；流水上 (用户, 业务键) 唯一，重复投递或重试不会重复记账。
/// </summary>
internal sealed class BalanceLedger(PaymentDbContext db)
{
    /// <summary>为支付单扣款；余额不足返回 null，同一支付单重复调用返回已记的那条流水。</summary>
    public async Task<BalanceEntry?> TryDebitAsync(
        Payment payment,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var key = BalanceEntry.PaymentKey(payment.Id);
        var account = await db.LockBalanceAccountAsync(payment.UserId, cancellationToken);
        if (account is null)
            return null;
        var existing = await FindAsync(account.Id, key, cancellationToken);
        if (existing is not null)
            return existing;
        if (account.Balance < payment.Amount)
            return null;

        var balanceAfter = account.Change(-payment.Amount, now);
        var entry = BalanceEntry.Record(
            account,
            BalanceEntryType.Payment,
            -payment.Amount,
            balanceAfter,
            key,
            "订单支付",
            now,
            orderId: payment.OrderId,
            paymentId: payment.Id);
        db.BalanceEntries.Add(entry);
        return entry;
    }

    /// <summary>把退款金额退回余额；同一退款单重复调用返回已记的那条流水。</summary>
    public async Task<BalanceEntry> CreditRefundAsync(
        Payment payment,
        PaymentRefund refund,
        string? afterSalesRequestNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var key = BalanceEntry.RefundKey(refund.Id);
        var account = await LockOrOpenAsync(payment.UserId, now, cancellationToken);
        var existing = await FindAsync(account.Id, key, cancellationToken);
        if (existing is not null)
            return existing;

        var balanceAfter = account.Change(refund.Amount, now);
        var entry = BalanceEntry.Record(
            account,
            BalanceEntryType.Refund,
            refund.Amount,
            balanceAfter,
            key,
            refund.Reason,
            now,
            orderId: payment.OrderId,
            paymentId: payment.Id,
            refundId: refund.Id,
            afterSalesRequestNumber: afterSalesRequestNumber);
        db.BalanceEntries.Add(entry);
        return entry;
    }

    public async Task<(BalanceEntry Entry, bool IsReplay)> AdjustAsync(
        string userId,
        decimal delta,
        string businessKey,
        string reason,
        string operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var key = BalanceEntry.AdjustmentKey(businessKey);
        var account = await LockOrOpenAsync(userId, now, cancellationToken);
        var existing = await FindAsync(account.Id, key, cancellationToken);
        if (existing is not null)
        {
            if (existing.Type != BalanceEntryType.Adjustment
                || existing.Amount != delta
                || !string.Equals(existing.Reason, reason.Trim(), StringComparison.Ordinal))
            {
                throw new BalanceIdempotencyConflictException("该幂等键已用于另一笔余额调整。");
            }
            return (existing, true);
        }

        var balanceAfter = account.Change(delta, now);
        var entry = BalanceEntry.Record(
            account,
            BalanceEntryType.Adjustment,
            delta,
            balanceAfter,
            key,
            reason,
            now,
            operatorId: operatorId);
        db.BalanceEntries.Add(entry);
        return (entry, false);
    }

    private async Task<BalanceAccount> LockOrOpenAsync(
        string userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var account = await db.LockBalanceAccountAsync(userId.Trim(), cancellationToken);
        if (account is not null)
            return account;

        // ponytail: 首次开户没有行可锁，两个并发请求同时开户时后提交的一方撞主键失败；调整带幂等键、消息会重投，重试即可。
        account = BalanceAccount.Open(userId, now);
        db.BalanceAccounts.Add(account);
        return account;
    }

    private Task<BalanceEntry?> FindAsync(string userId, string key, CancellationToken cancellationToken) =>
        db.BalanceEntries
            .AsTracking()
            .SingleOrDefaultAsync(entry => entry.UserId == userId && entry.BusinessKey == key, cancellationToken);
}

public sealed record BalanceDto(decimal Balance, string Currency, DateTimeOffset? UpdatedOnUtc);

public sealed record BalanceEntryDto(
    Guid Id,
    string Type,
    decimal Amount,
    decimal BalanceAfter,
    Guid? OrderId,
    Guid? RefundId,
    string? AfterSalesRequestNumber,
    string Reason,
    string? OperatorId,
    DateTimeOffset OccurredOnUtc);

public sealed record GetBalanceQuery(string UserId) : IQuery<BalanceDto>;

internal sealed class GetBalanceHandler(PaymentDbContext db) : IRequestHandler<GetBalanceQuery, BalanceDto>
{
    public async Task<BalanceDto> Handle(GetBalanceQuery request, CancellationToken cancellationToken) =>
        await db.BalanceAccounts
            .AsNoTracking()
            .Where(account => account.Id == request.UserId)
            .Select(account => new BalanceDto(account.Balance, BalanceAccount.Currency, account.UpdatedOnUtc))
            .SingleOrDefaultAsync(cancellationToken)
        ?? new BalanceDto(0m, BalanceAccount.Currency, null);
}

public sealed record ListBalanceEntriesQuery(string UserId, int Skip, int Take) : IQuery<IReadOnlyList<BalanceEntryDto>>;

internal sealed class ListBalanceEntriesHandler(PaymentDbContext db)
    : IRequestHandler<ListBalanceEntriesQuery, IReadOnlyList<BalanceEntryDto>>
{
    public async Task<IReadOnlyList<BalanceEntryDto>> Handle(
        ListBalanceEntriesQuery request,
        CancellationToken cancellationToken)
    {
        var rows = await db.BalanceEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == request.UserId)
            .OrderByDescending(entry => entry.OccurredOnUtc)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 100))
            .ToArrayAsync(cancellationToken);
        return rows.Select(ToDto).ToArray();
    }

    internal static BalanceEntryDto ToDto(BalanceEntry entry) => new(
        entry.Id,
        entry.Type.ToString(),
        entry.Amount,
        entry.BalanceAfter,
        entry.OrderId,
        entry.RefundId,
        entry.AfterSalesRequestNumber,
        entry.Reason,
        entry.OperatorId,
        entry.OccurredOnUtc);
}

public sealed record AdjustBalanceCommand(
    string UserId,
    decimal Delta,
    string BusinessKey,
    string Reason,
    string OperatorId) : ICommand<BalanceAdjustmentResult>;

public sealed record BalanceAdjustmentResult(BalanceEntryDto Entry, bool IsReplay);

internal sealed class AdjustBalanceHandler(BalanceLedger ledger, TimeProvider clock)
    : IRequestHandler<AdjustBalanceCommand, BalanceAdjustmentResult>
{
    public async Task<BalanceAdjustmentResult> Handle(AdjustBalanceCommand request, CancellationToken cancellationToken)
    {
        var (entry, isReplay) = await ledger.AdjustAsync(
            request.UserId,
            request.Delta,
            request.BusinessKey,
            request.Reason,
            request.OperatorId,
            clock.GetUtcNow(),
            cancellationToken);
        return new BalanceAdjustmentResult(ListBalanceEntriesHandler.ToDto(entry), isReplay);
    }
}
