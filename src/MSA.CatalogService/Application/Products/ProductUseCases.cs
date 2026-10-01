using FluentValidation;
using MediatR;
using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Domain;
using MSA.Infrastrucute.Mediator;
using MSA.Redis;

namespace MSA.CatalogService.Application;

public sealed record CreateDraftProductCommand(
    Guid CategoryId,
    string Sku,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    string Actor) : ICommand<Guid>;

public sealed record UpdateProductDetailsCommand(
    Guid ProductId,
    string Name,
    string Description,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record ChangeProductPriceCommand(
    Guid ProductId,
    decimal Price,
    string Currency,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record PublishProductCommand(
    Guid ProductId,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record UnpublishProductCommand(
    Guid ProductId,
    string? Reason,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record ArchiveProductCommand(
    Guid ProductId,
    string? Reason,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record GetProductQuery(Guid Id) : IQuery<ProductDto?>;

public sealed record GetAdminProductQuery(Guid Id) : IQuery<ProductDto?>;

public sealed record ListProductsQuery(
    int Skip,
    int Take,
    string? Search = null,
    Guid? CategoryId = null) : IQuery<PagedResult<ProductDto>>;

public sealed record ListAdminProductsQuery(
    int Skip,
    int Take,
    string? Search,
    string? LifecycleStatus,
    Guid? CategoryId) : IQuery<PagedResult<ProductDto>>;

public sealed class CreateDraftProductValidator : AbstractValidator<CreateDraftProductCommand>
{
    public CreateDraftProductValidator()
    {
        RuleFor(command => command.CategoryId).NotEmpty();
        RuleFor(command => command.Sku).NotEmpty().MaximumLength(80);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Description).NotNull();
        RuleFor(command => command.Price).InclusiveBetween(0, CatalogValidation.MaximumPrice);
        RuleFor(command => command.Currency).Matches(CatalogValidation.CurrencyPattern);
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
    }
}

public sealed class ChangeProductPriceValidator : AbstractValidator<ChangeProductPriceCommand>
{
    public ChangeProductPriceValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.Price).InclusiveBetween(0, CatalogValidation.MaximumPrice);
        RuleFor(command => command.Currency).Matches(CatalogValidation.CurrencyPattern);
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
    }
}

internal sealed class CreateDraftProductHandler(
    ICategoryRepository categories,
    IProductRepository products,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<CreateDraftProductCommand, Guid>
{
    public async Task<Guid> Handle(CreateDraftProductCommand request, CancellationToken cancellationToken)
    {
        var category = await categories.GetRequiredAsync(request.CategoryId, cancellationToken);
        if (category.Status != CategoryLifecycleStatus.Active)
            throw new InvalidOperationException("Products can only be assigned to an active category.");

        var normalizedSku = Product.NormalizeSku(request.Sku);
        if (await products.SkuExistsAsync(normalizedSku, null, cancellationToken))
            throw new InvalidOperationException($"SKU '{request.Sku}' already exists.");

        var now = clock.GetUtcNow();
        var product = new Product(
            Guid.NewGuid(),
            request.CategoryId,
            request.Sku,
            request.Name,
            request.Description,
            request.Price,
            request.Currency,
            request.Actor,
            now);
        products.Add(product);
        snapshots.Publish(product);
        audit.Record("Product", product.Id, "DraftCreated", request.Actor, now, new { product.Sku });
        return product.Id;
    }
}

internal sealed class UpdateProductDetailsHandler(
    IProductRepository products,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<UpdateProductDetailsCommand, bool>
{
    public async Task<bool> Handle(UpdateProductDetailsCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetRequiredAsync(request.ProductId, cancellationToken);
        product.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        product.UpdateDetails(request.Name, request.Description, request.Actor, now);
        snapshots.Publish(product);
        audit.Record("Product", product.Id, "DetailsUpdated", request.Actor, now);
        return true;
    }
}

internal sealed class ChangeProductPriceHandler(
    IProductRepository products,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<ChangeProductPriceCommand, bool>
{
    public async Task<bool> Handle(ChangeProductPriceCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetRequiredAsync(request.ProductId, cancellationToken);
        product.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        product.ChangePrice(request.Price, request.Currency, request.Actor, now);
        snapshots.Publish(product);
        audit.Record("Product", product.Id, "PriceChanged", request.Actor, now, new { request.Price, request.Currency });
        return true;
    }
}

internal sealed class PublishProductHandler(
    ICategoryRepository categories,
    IProductRepository products,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<PublishProductCommand, bool>
{
    public async Task<bool> Handle(PublishProductCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetRequiredAsync(request.ProductId, cancellationToken);
        product.EnsureVersion(request.ExpectedVersion);
        var category = await categories.FindAsync(product.CategoryId, cancellationToken)
            ?? throw new KeyNotFoundException("Product category was not found.");
        if (category.Status != CategoryLifecycleStatus.Active)
            throw new InvalidOperationException("A product in an inactive category cannot be published.");

        var now = clock.GetUtcNow();
        product.Publish(request.Actor, now);
        snapshots.Publish(product);
        audit.Record("Product", product.Id, "Published", request.Actor, now);
        return true;
    }
}

internal sealed class UnpublishProductHandler(
    IProductRepository products,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<UnpublishProductCommand, bool>
{
    public async Task<bool> Handle(UnpublishProductCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetRequiredAsync(request.ProductId, cancellationToken);
        product.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        product.Unpublish(request.Actor, now);
        snapshots.Publish(product);
        audit.Record("Product", product.Id, "Unpublished", request.Actor, now, new { request.Reason });
        return true;
    }
}

internal sealed class ArchiveProductHandler(
    IProductRepository products,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<ArchiveProductCommand, bool>
{
    public async Task<bool> Handle(ArchiveProductCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetRequiredAsync(request.ProductId, cancellationToken);
        product.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        product.Archive(request.Actor, now);
        snapshots.Publish(product);
        audit.Record("Product", product.Id, "Archived", request.Actor, now, new { request.Reason });
        return true;
    }
}

/// <summary>前台商品详情走 Cache Aside（一级 + Redis），任何商品变更提交后失效；后台详情永远直读。</summary>
internal sealed class GetProductHandler(ICatalogReadService reads, ICacheStore cache)
    : IRequestHandler<GetProductQuery, ProductDto?>
{
    public Task<ProductDto?> Handle(GetProductQuery request, CancellationToken cancellationToken) =>
        cache.GetOrLoadAsync(
            CatalogCacheKeys.Product(request.Id),
            token => reads.GetProductAsync(request.Id, publicOnly: true, token),
            CatalogCachePolicies.ProductDetail,
            cancellationToken);
}

internal sealed class GetAdminProductHandler(ICatalogReadService reads)
    : IRequestHandler<GetAdminProductQuery, ProductDto?>
{
    public Task<ProductDto?> Handle(GetAdminProductQuery request, CancellationToken cancellationToken) =>
        reads.GetProductAsync(request.Id, publicOnly: false, cancellationToken);
}

/// <summary>
/// 前台列表：浏览类请求（首页、分类的前几页）命中率高，缓存起来；带搜索词或翻到很深的请求是长尾，直读。
/// 列表键里带命名空间代际，任何商品变更提交后整体作废，不需要知道变更影响了哪几页。
/// </summary>
internal sealed class ListProductsHandler(ICatalogReadService reads, ICacheStore cache)
    : IRequestHandler<ListProductsQuery, PagedResult<ProductDto>>
{
    private const int CachedDepth = 200;

    public async Task<PagedResult<ProductDto>> Handle(ListProductsQuery request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Search) || request.Skip >= CachedDepth)
            return await LoadAsync(request, cancellationToken);

        var generation = await cache.NamespaceAsync(CatalogCacheKeys.ProductLists, cancellationToken);
        var key = $"{generation}:{request.CategoryId?.ToString("N") ?? "all"}:{request.Skip}:{request.Take}";
        return await cache.GetOrLoadAsync(
                   key,
                   async token => await LoadAsync(request, token),
                   CatalogCachePolicies.ProductList,
                   cancellationToken)
               ?? await LoadAsync(request, cancellationToken);
    }

    private Task<PagedResult<ProductDto>> LoadAsync(ListProductsQuery request, CancellationToken cancellationToken) =>
        reads.ListProductsAsync(
            request.Skip,
            request.Take,
            publicOnly: true,
            request.Search,
            lifecycleStatus: null,
            request.CategoryId,
            cancellationToken);
}

internal sealed class ListAdminProductsHandler(ICatalogReadService reads)
    : IRequestHandler<ListAdminProductsQuery, PagedResult<ProductDto>>
{
    public Task<PagedResult<ProductDto>> Handle(ListAdminProductsQuery request, CancellationToken cancellationToken) =>
        reads.ListProductsAsync(
            request.Skip,
            request.Take,
            publicOnly: false,
            request.Search,
            request.LifecycleStatus,
            request.CategoryId,
            cancellationToken);
}
