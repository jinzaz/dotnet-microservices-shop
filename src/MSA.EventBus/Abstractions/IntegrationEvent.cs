using System.Text.Json.Serialization;

namespace MSA.EventBus.Abstractions;

/// <summary>
/// 集成事件基类，业务事件继承它即可获得 Id/时间戳等公共字段
/// </summary>
public abstract class IntegrationEvent : IIntegrationEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime CreationDate { get; init; } = DateTime.UtcNow;

    [JsonIgnore]
    public string EventType => GetType().Name;

    protected IntegrationEvent() { }

    [JsonConstructor]
    protected IntegrationEvent(Guid id, DateTime creationDate)
    {
        Id = id;
        CreationDate = creationDate;
    }
}
