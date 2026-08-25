namespace MSA.EventBus.Abstractions;

/// <summary>
/// 供内部反射使用的非泛型标记接口
/// </summary>
public interface IIntegrationEventHandler
{
    Task HandleAsync(IntegrationEvent @event, CancellationToken cancellationToken = default);
}

public interface IIntegrationEventHandler<in TEvent> : IIntegrationEventHandler
    where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);

    Task IIntegrationEventHandler.HandleAsync(IntegrationEvent @event, CancellationToken cancellationToken) => HandleAsync((TEvent)@event, cancellationToken);
}
