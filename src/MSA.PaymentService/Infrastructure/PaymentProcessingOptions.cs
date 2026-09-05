namespace MSA.PaymentService.Infrastructure;

internal sealed class PaymentProcessingOptions
{
    public TimeSpan PaymentLifetime { get; set; } = TimeSpan.FromMinutes(15);
    public int ProviderOperationBatchSize { get; set; } = 25;
    public int ProviderOperationConcurrency { get; set; } = 8;
    public int ProviderOperationMaxAttempts { get; set; } = 10;
    public TimeSpan ProviderOperationPollingInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan ProviderOperationLockDuration { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan ProviderCallTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
    public int ExpiryBatchSize { get; set; } = 100;
    public TimeSpan ExpiryPollingInterval { get; set; } = TimeSpan.FromSeconds(15);

    public bool IsValid() =>
        PaymentLifetime > TimeSpan.Zero
        && ProviderOperationBatchSize > 0
        && ProviderOperationConcurrency > 0
        && ProviderOperationMaxAttempts > 0
        && ProviderOperationPollingInterval > TimeSpan.Zero
        && ProviderOperationLockDuration > ProviderCallTimeout
        && ProviderCallTimeout > TimeSpan.Zero
        && MaximumRetryDelay > TimeSpan.Zero
        && ExpiryBatchSize > 0
        && ExpiryPollingInterval > TimeSpan.Zero;
}
