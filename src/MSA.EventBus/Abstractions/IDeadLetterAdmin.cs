namespace MSA.EventBus.Abstractions;

/// <summary>A message that exhausted its delivery attempts and is parked in the service's dead-letter queue.</summary>
public sealed record DeadLetterMessage(string EventName, int Attempts, string Payload);

/// <summary>
/// 死信队列的运维入口：查看堆积了什么、修复根因后重放。实现必须保证重放是「取出即重投」，
/// 重复重放由消费端的 Inbox 去重兜底。
/// </summary>
public interface IDeadLetterAdmin
{
    Task<IReadOnlyList<DeadLetterMessage>> PeekAsync(int max, CancellationToken cancellationToken = default);

    /// <summary>Republishes up to <paramref name="max"/> dead letters to their original event route; returns how many.</summary>
    Task<int> ReplayAsync(int max, CancellationToken cancellationToken = default);
}
