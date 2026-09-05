using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.PaymentService.Domain;
using MSA.Repositories.Persistence;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace MSA.PaymentService.Infrastructure;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options, IMediator mediator)
    : MessagingDbContext(options, mediator)
{
    public DbSet<Payment> Payments => Set<Payment>();
    internal DbSet<PaymentProviderOperation> ProviderOperations => Set<PaymentProviderOperation>();
    internal DbSet<PaymentNotificationRecord> PaymentNotifications => Set<PaymentNotificationRecord>();
    internal DbSet<PaymentStatusHistory> PaymentStatusHistory => Set<PaymentStatusHistory>();
    internal DbSet<PaymentRefund> PaymentRefunds => Set<PaymentRefund>();
    internal DbSet<PaymentCancellationIntent> PaymentCancellationIntents => Set<PaymentCancellationIntent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentDbContext).Assembly);
    }

    internal Task<int> TryInsertInboxMessageAsync(
        Guid messageId,
        string consumer,
        DateTimeOffset processedOnUtc,
        CancellationToken cancellationToken) =>
        Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO inbox_messages ("MessageId", "Consumer", "ProcessedOnUtc")
            VALUES ({messageId}, {consumer}, {processedOnUtc})
            ON CONFLICT ("MessageId", "Consumer") DO NOTHING
            """,
            cancellationToken);

    internal async Task<Payment?> LockPaymentByMerchantTradeNoAsync(
        string merchantTradeNo,
        CancellationToken cancellationToken)
    {
        var rows = await Payments
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM payments
                WHERE "MerchantTradeNo" = {merchantTradeNo}
                FOR UPDATE
                """)
            .AsTracking()
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    internal async Task<Payment> LockPaymentAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var rows = await Payments
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM payments
                WHERE "Id" = {paymentId}
                FOR UPDATE
                """)
            .AsTracking()
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault()
            ?? throw new InvalidOperationException($"Payment {paymentId} was not found.");
    }

    internal async Task<Payment?> LockPaymentByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var rows = await Payments
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM payments
                WHERE "OrderId" = {orderId}
                FOR UPDATE
                """)
            .AsTracking()
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    internal async Task<PaymentProviderOperation?> LockProviderOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var rows = await ProviderOperations
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM payment_provider_operations
                WHERE "Id" = {operationId}
                FOR UPDATE
                """)
            .AsTracking()
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    internal async Task<PaymentRefund> LockRefundAsync(
        Guid refundId,
        CancellationToken cancellationToken)
    {
        var rows = await PaymentRefunds
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM payment_refunds
                WHERE "Id" = {refundId}
                FOR UPDATE
                """)
            .AsTracking()
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault()
            ?? throw new InvalidOperationException($"Refund {refundId} was not found.");
    }

    internal async Task<PaymentRefund?> LockRefundByRequestIdAsync(
        Guid refundRequestId,
        CancellationToken cancellationToken)
    {
        var rows = await PaymentRefunds
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM payment_refunds
                WHERE "RefundRequestId" = {refundRequestId}
                FOR UPDATE
                """)
            .AsTracking()
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    internal async Task AcquireOrderTransactionLockAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var transaction = Database.CurrentTransaction
            ?? throw new InvalidOperationException("Payment order locks require an active transaction.");
        var connection = Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@resourceKey, 0));";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "resourceKey";
        parameter.Value = $"payment-order:{orderId:N}";
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
