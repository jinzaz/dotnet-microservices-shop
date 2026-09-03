namespace MSA.Saga;
public sealed class SagaTimeoutOptions
{
    public int BatchSize { get; set; } = 25;
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(1);
}
