namespace MSA.EventBus.Abstractions;

/// <summary>
/// 事件总线抽象。业务代码一般不直接调用 PublishAsync 发布事件——
/// 而是通过 Outbox 写入数据库，由 OutboxProcessorService 调用它完成真正的发布，
/// 从而保证"业务数据变更"与"消息发出"的原子性。
/// Subscribe 仍由消费方服务在启动时直接调用。
/// </summary>
public interface IEventBus
{
    Task PublishAsync(IntegrationEvent @event, CancellationToken cancellationToken = default);
}
