using MSA.Contracts;
using MSA.EventBus.Abstractions;

namespace MSA.SearchService;

/// <summary>
/// 目录说「这些商品 / 分类变了」，就回读最新快照写进索引。处理是幂等的（外部版本号 + 总读最新状态），
/// 所以不需要 Inbox 去重表：重复投递只是多写一次相同内容。
/// 失败抛出交给事件总线：按 RabbitMQ:RetryDelay 重投，超过 MaxDeliveryAttempts 进死信队列 msa.search.dlq。
/// ES 长时间不可用导致的死信，恢复后从 RabbitMQ 管理台重投，或直接全量重建一次。
/// </summary>
internal sealed class CatalogSearchInvalidatedHandler(ProductIndexWriter writer)
    : IIntegrationEventHandler<CatalogSearchInvalidatedIntegrationEvent>
{
    public async Task HandleAsync(CatalogSearchInvalidatedIntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event.ProductIds.Count > 0)
            await writer.RefreshAsync(@event.ProductIds, cancellationToken);
        foreach (var categoryId in @event.CategoryIds.Distinct())
            await writer.RefreshCategoryAsync(categoryId, cancellationToken);
    }
}
