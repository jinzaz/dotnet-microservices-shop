using Microsoft.Extensions.Options;
using MSA.PaymentCore;
using MSA.PaymentService.Domain;
using MSA.PaymentService.Integration;

namespace MSA.PaymentService.Infrastructure;

internal sealed class PaymentProviderOperationDispatcher(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<PaymentProcessingOptions> configured,
    ILogger<PaymentProviderOperationDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            configured.Value.ProviderOperationPollingInterval,
            clock);

        do
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Payment provider operation polling failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var options = configured.Value;
        IReadOnlyList<PaymentProviderOperationLease> leases;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<PaymentProviderOperationStore>();
            leases = await store.ClaimDueAsync(
                Math.Min(
                    options.ProviderOperationBatchSize,
                    options.ProviderOperationConcurrency),
                options.ProviderOperationLockDuration,
                cancellationToken);
        }

        await Task.WhenAll(leases.Select(lease =>
            ProcessOperationAsync(lease, cancellationToken)));
    }

    private async Task ProcessOperationAsync(
        PaymentProviderOperationLease lease,
        CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<PaymentProviderOperationStore>();
        var snapshot = await store.GetSnapshotAsync(lease, stoppingToken);
        if (snapshot is null)
            return;

        try
        {
            var gateway = scope.ServiceProvider
                .GetRequiredService<PaymentGatewayResolver>()
                .Resolve(snapshot.Channel);
            using var providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            providerCancellation.CancelAfter(configured.Value.ProviderCallTimeout);

            var completed = snapshot.OperationType switch
            {
                PaymentProviderOperationType.CreateNativePayment =>
                    await CreatePaymentAsync(
                        scope.ServiceProvider,
                        store,
                        gateway,
                        lease,
                        snapshot,
                        providerCancellation.Token,
                        stoppingToken),
                PaymentProviderOperationType.CancelPayment =>
                    await CancelPaymentAsync(
                        store,
                        gateway,
                        lease,
                        snapshot,
                        providerCancellation.Token,
                        stoppingToken),
                PaymentProviderOperationType.RefundPayment =>
                    await RefundPaymentAsync(
                        store,
                        gateway,
                        lease,
                        snapshot,
                        providerCancellation.Token,
                        stoppingToken),
                _ => throw new ArgumentOutOfRangeException(nameof(snapshot.OperationType))
            };
            if (!completed)
            {
                logger.LogWarning(
                    "Provider operation {OperationId} completed after its lease was lost.",
                    lease.OperationId);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var delay = GetRetryDelay(
                snapshot.AttemptCount,
                snapshot.OperationId,
                configured.Value.MaximumRetryDelay);
            if (await store.RecordFailureAsync(
                    lease,
                    exception,
                    clock.GetUtcNow().Add(delay),
                    configured.Value.ProviderOperationMaxAttempts,
                    stoppingToken))
            {
                logger.LogWarning(
                    exception,
                    "Payment provider operation {OperationId} failed and was scheduled according to its retry policy.",
                    lease.OperationId);
            }
        }
    }

    private async Task<bool> CreatePaymentAsync(
        IServiceProvider services,
        PaymentProviderOperationStore store,
        IPaymentGateway gateway,
        PaymentProviderOperationLease lease,
        PaymentProviderOperationSnapshot snapshot,
        CancellationToken providerCancellationToken,
        CancellationToken persistenceCancellationToken)
    {
        var now = clock.GetUtcNow();
        if (snapshot.PaymentStatus != PaymentStatus.Pending
            || snapshot.PaymentUrl is not null
            || snapshot.ExpiresOnUtc <= now)
        {
            return await store.CancelAsync(
                lease,
                "Payment is no longer eligible for provider creation.",
                persistenceCancellationToken);
        }

        var endpointOptions = services
            .GetRequiredService<IOptions<PaymentEndpointOptions>>()
            .Value;
        var notifyUrl = $"{endpointOptions.NotifyBaseUrl.TrimEnd('/')}/{snapshot.Channel.ToString().ToLowerInvariant()}";
        var result = await gateway.CreateNativePaymentAsync(
            new CreatePaymentRequest(
                snapshot.MerchantTradeNo,
                snapshot.Amount,
                snapshot.Currency,
                $"Order {snapshot.OrderId:N}",
                notifyUrl,
                snapshot.ExpiresOnUtc),
            providerCancellationToken);
        EnsureResultMatches(snapshot, result);
        return await store.CompleteCreateAsync(
            lease,
            result,
            persistenceCancellationToken);
    }

    private static async Task<bool> CancelPaymentAsync(
        PaymentProviderOperationStore store,
        IPaymentGateway gateway,
        PaymentProviderOperationLease lease,
        PaymentProviderOperationSnapshot snapshot,
        CancellationToken providerCancellationToken,
        CancellationToken persistenceCancellationToken)
    {
        if (snapshot.PaymentStatus != PaymentStatus.Cancelling)
        {
            return await store.CancelAsync(
                lease,
                "Payment is no longer awaiting cancellation.",
                persistenceCancellationToken);
        }
        if (gateway is not IPaymentCancellationGateway cancellationGateway)
            throw new NotSupportedException($"Payment channel {snapshot.Channel} does not support cancellation.");

        var result = await cancellationGateway.CancelPaymentAsync(
            new CancelPaymentRequest(snapshot.MerchantTradeNo),
            providerCancellationToken);
        if (!string.Equals(result.MerchantTradeNo, snapshot.MerchantTradeNo, StringComparison.Ordinal)
            || result.Channel != snapshot.Channel)
        {
            throw new InvalidOperationException(
                $"Provider cancellation result does not match operation {snapshot.OperationId}.");
        }

        return await store.CompleteCancellationAsync(
            lease,
            result,
            persistenceCancellationToken);
    }

    private static async Task<bool> RefundPaymentAsync(
        PaymentProviderOperationStore store,
        IPaymentGateway gateway,
        PaymentProviderOperationLease lease,
        PaymentProviderOperationSnapshot snapshot,
        CancellationToken providerCancellationToken,
        CancellationToken persistenceCancellationToken)
    {
        if (snapshot.PaymentStatus != PaymentStatus.Refunding
            || snapshot.ProviderTransactionId is null
            || !snapshot.RefundId.HasValue
            || snapshot.MerchantRefundNo is null
            || !snapshot.RefundAmount.HasValue
            || snapshot.RefundCurrency is null
            || snapshot.RefundReason is null)
        {
            return await store.CancelAsync(
                lease,
                "Payment or refund is no longer eligible for provider refund.",
                persistenceCancellationToken);
        }
        if (gateway is not IPaymentRefundGateway refundGateway)
            throw new NotSupportedException($"Payment channel {snapshot.Channel} does not support refunds.");

        var result = await refundGateway.RefundPaymentAsync(
            new RefundPaymentRequest(
                snapshot.MerchantTradeNo,
                snapshot.ProviderTransactionId,
                snapshot.MerchantRefundNo,
                snapshot.RefundAmount.Value,
                snapshot.Amount,
                snapshot.RefundCurrency,
                snapshot.RefundReason),
            providerCancellationToken);
        if (!string.Equals(result.MerchantRefundNo, snapshot.MerchantRefundNo, StringComparison.Ordinal)
            || result.Channel != snapshot.Channel)
        {
            throw new InvalidOperationException(
                $"Provider refund result does not match operation {snapshot.OperationId}.");
        }
        if (!result.Completed)
            throw new InvalidOperationException("Provider refund is still processing.");

        return await store.CompleteRefundAsync(
            lease,
            result,
            persistenceCancellationToken);
    }

    private static void EnsureResultMatches(
        PaymentProviderOperationSnapshot snapshot,
        CreatePaymentResult result)
    {
        if (!string.Equals(
                result.MerchantTradeNo,
                snapshot.MerchantTradeNo,
                StringComparison.Ordinal)
            || result.Channel != snapshot.Channel
            || string.IsNullOrWhiteSpace(result.PaymentUrl))
        {
            throw new InvalidOperationException(
                $"Provider result does not match payment operation {snapshot.OperationId}.");
        }
    }

    private static TimeSpan GetRetryDelay(
        int attemptCount,
        Guid operationId,
        TimeSpan maximumDelay)
    {
        // Deterministic jitter avoids synchronized retries without changing the provider idempotency key.
        var jitterMilliseconds = Math.Abs((long)operationId.GetHashCode() % 1000);
        var exponent = Math.Min(Math.Max(attemptCount, 1), 20);
        var baseDelay = Math.Min(Math.Pow(2, exponent), maximumDelay.TotalSeconds);
        var delay = TimeSpan.FromSeconds(baseDelay).Add(TimeSpan.FromMilliseconds(jitterMilliseconds));
        return delay <= maximumDelay ? delay : maximumDelay;
    }
}
