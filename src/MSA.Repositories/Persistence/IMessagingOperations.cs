namespace MSA.Repositories.Persistence;

/// <summary>An outbox message whose retries are exhausted: it will never publish itself again.</summary>
public sealed record FailedOutboxMessageDto(
    Guid Id,
    string EventType,
    int RetryCount,
    DateTimeOffset OccurredOnUtc,
    string? LastError);

/// <summary>A saga that failed, or has been sitting in the same state for too long.</summary>
public sealed record StalledSagaDto(
    Guid CorrelationId,
    string SagaType,
    string State,
    string Status,
    DateTimeOffset UpdatedOnUtc,
    string? FailureReason);

/// <summary>
/// 最终一致性的运维出口：查看发布不出去的消息与卡住的流程，并在修复根因后重新投递。
/// 没有这层，失败只会静静躺在表里。
/// </summary>
public interface IMessagingOperations
{
    Task<IReadOnlyList<FailedOutboxMessageDto>> ListFailedOutboxAsync(int take, CancellationToken cancellationToken);

    /// <summary>Puts failed messages back in the publish queue; with no id, the whole failed backlog.</summary>
    Task<int> RequeueFailedOutboxAsync(Guid? messageId, int max, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Failed sagas plus anything still running or compensating since before <paramref name="stalledBefore"/>.</summary>
    Task<IReadOnlyList<StalledSagaDto>> ListStalledSagasAsync(DateTimeOffset stalledBefore, int take, CancellationToken cancellationToken);

    /// <summary>Deletes published outbox rows and consumed inbox rows older than the given instants.</summary>
    Task<(int Outbox, int Inbox)> PurgeProcessedMessagesAsync(
        DateTimeOffset outboxBefore,
        DateTimeOffset inboxBefore,
        int batchSize,
        CancellationToken cancellationToken);
}
