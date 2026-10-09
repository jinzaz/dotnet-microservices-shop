using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MSA.CatalogService.Domain;
using MSA.Contracts;
using MSA.EventBusOutbox;

namespace MSA.CatalogService.Infrastructure;

/// <summary>
/// 生产问题 #1（双写不一致）：不在请求里直接通知搜索服务——业务写库成功、通知失败（或反过来）会让两边永久不一致。
/// 这里在 SaveChanges 里往 <b>同一事务</b> 的 Outbox 写一条「这些商品变了」，由 Outbox 发布器可靠投递；
/// 搜索服务宕机、RabbitMQ 宕机都只是延迟，不会丢。
/// <para>
/// 挂在 SaveChanges 上而不是业务代码里：改名、改价、加规格、换图、上下架任何一处写库都会被捕获，
/// 新加的接口不会「忘了通知搜索」。消息只带 id 不带内容，搜索服务收到后回读最新快照。
/// </para>
/// </summary>
internal sealed class SearchInvalidationInterceptor : SaveChangesInterceptor
{
    /// <summary>一次保存涉及很多商品（批量操作、导入）时拆成多条，单条消息保持在几十 KB 以内。</summary>
    internal const int MaxIdsPerEvent = 500;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Collect(DbContext? context)
    {
        if (context is not IOutboxStore outbox)
            return;

        // SavingChanges 在 EF 自动检测变更之前触发，先检测一遍才能看到本次真正落库的实体。
        context.ChangeTracker.DetectChanges();
        var products = new HashSet<Guid>();
        var categories = new HashSet<Guid>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;
            switch (entry.Entity)
            {
                case Product product:
                    products.Add(product.Id);
                    break;
                case ProductVariant variant:
                    products.Add(variant.ProductId);
                    break;
                case ProductMedia media:
                    products.Add(media.ProductId);
                    break;
                // 索引文档冗余了分类名（搜「耳机」也能命中名字里没写耳机的商品），所以分类改名要刷新它下面的商品。
                case Category category when entry.State == EntityState.Modified
                                            && entry.Property(nameof(Category.Name)).IsModified:
                    categories.Add(category.Id);
                    break;
            }
        }

        foreach (var chunk in products.Chunk(MaxIdsPerEvent))
            outbox.AddIntegrationEvent(new CatalogSearchInvalidatedIntegrationEvent { ProductIds = chunk });
        if (categories.Count > 0)
            outbox.AddIntegrationEvent(new CatalogSearchInvalidatedIntegrationEvent { CategoryIds = categories.ToArray() });
    }
}
