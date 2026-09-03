namespace MSA.Saga;
public interface ISagaTimeoutHandler
{
    string SagaType { get; }
    Task HandleTimeoutAsync(SagaState saga, CancellationToken cancellationToken);
}
