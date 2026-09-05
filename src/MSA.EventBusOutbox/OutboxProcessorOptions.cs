namespace MSA.EventBusOutbox;

public sealed class OutboxProcessorOptions
{
    public int BatchSize { get; set; } = 50;
    public int MaxRetries { get; set; } = 10;
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
}
