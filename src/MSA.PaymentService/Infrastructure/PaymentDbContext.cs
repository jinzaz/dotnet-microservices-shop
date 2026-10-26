using MediatR;
using MSA.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using MSA.PaymentService.Domain;
using MSA.Repositories.Persistence;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace MSA.PaymentService.Infrastructure;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options, IMediator mediator)
    : MessagingDbContext(options, mediator, "payment")
{
    public DbSet<Payment> Payments => Set<Payment>();
    internal DbSet<PaymentProviderOperation> ProviderOperations => Set<PaymentProviderOperation>();
    internal DbSet<PaymentNotificationRecord> PaymentNotifications => Set<PaymentNotificationRecord>();
    internal DbSet<PaymentStatusHistory> PaymentStatusHistory => Set<PaymentStatusHistory>();
    internal DbSet<PaymentRefund> PaymentRefunds => Set<PaymentRefund>();
    internal DbSet<PaymentCancellationIntent> PaymentCancellationIntents => Set<PaymentCancellationIntent>();
    internal DbSet<BalanceAccount> BalanceAccounts => Set<BalanceAccount>();
    internal DbSet<BalanceEntry> BalanceEntries => Set<BalanceEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentDbContext).Assembly);
    }

    internal Task<Payment?> LockPaymentByMerchantTradeNoAsync(
        string merchantTradeNo,
        CancellationToken cancellationToken) =>
        Payments
            .Where(payment => payment.MerchantTradeNo == merchantTradeNo)
            .ForUpdate()
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);

    internal async Task<Payment> LockPaymentAsync(
        Guid paymentId,
        CancellationToken cancellationToken) =>
        await Payments
            .Where(payment => payment.Id == paymentId)
            .ForUpdate()
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException($"Payment {paymentId} was not found.");

    internal Task<Payment?> LockPaymentByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken) =>
        Payments
            .Where(payment => payment.OrderId == orderId)
            .ForUpdate()
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);

    internal Task<PaymentProviderOperation?> LockProviderOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken) =>
        ProviderOperations
            .Where(operation => operation.Id == operationId)
            .ForUpdate()
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);

    internal async Task<PaymentRefund> LockRefundAsync(
        Guid refundId,
        CancellationToken cancellationToken) =>
        await PaymentRefunds
            .Where(refund => refund.Id == refundId)
            .ForUpdate()
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException($"Refund {refundId} was not found.");

    internal Task<PaymentRefund?> LockRefundByRequestIdAsync(
        Guid refundRequestId,
        CancellationToken cancellationToken) =>
        PaymentRefunds
            .Where(refund => refund.RefundRequestId == refundRequestId)
            .ForUpdate()
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);

    internal Task<BalanceAccount?> LockBalanceAccountAsync(
        string userId,
        CancellationToken cancellationToken) =>
        BalanceAccounts
            .Where(account => account.Id == userId)
            .ForUpdate()
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>按订单维度加事务级咨询锁，串行化「创建支付、取消、退款」等可能交叉到达的消息。</summary>
    internal Task AcquireOrderTransactionLockAsync(Guid orderId, CancellationToken cancellationToken) =>
        this.AcquireTransactionLockAsync($"payment-order:{orderId:N}", cancellationToken);
}
