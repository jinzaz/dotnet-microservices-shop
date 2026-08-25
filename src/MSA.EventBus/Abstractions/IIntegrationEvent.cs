namespace MSA.EventBus.Abstractions;

/// <summary>
/// 所有跨服务传递的集成事件的公共契约
/// </summary>
public interface IIntegrationEvent
{
    Guid Id { get; }
    DateTime CreationDate { get; }

    /// <summary>
    /// 事件的路由/类型标识，默认使用类型名。
    /// EventBus 用它做 RabbitMQ 的 routing key 及订阅匹配。
    /// </summary>
    string EventType { get; }
}
