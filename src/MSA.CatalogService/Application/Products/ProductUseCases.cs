using FluentValidation;
using MediatR;
using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Domain;
using MSA.Infrastrucute.Mediator;

namespace MSA.CatalogService.Application;

// Legacy API contracts retained for rolling clients.
public sealed record CreateProductCommand(
    Guid CategoryId,
    string Sku,
    string Name,
    string Description,
    decimal Price,
    string Currency) : ICommand<Guid>;

public sealed record UpdateProductCommand(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    bool IsActive) : ICommand<bool>;

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

public sealed record ListProductsQuery(int Skip, int Take) : IQuery<IReadOnlyList<ProductDto>>;

public sealed record ListAdminProductsQuery(
    int Skip,
    int Take,
    string? Search,
    string? LifecycleStatus,
    Guid? CategoryId) : IQuery<PagedResult<ProductDto>>;

public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(command => command.CategoryId).NotEmpty();
        RuleFor(command => command.Sku).NotEmpty().MaximumLength(80);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Description).NotNull();
        RuleFor(command => command.Price).InclusiveBetween(0, 9_999_999_999_999_999.99m);
        RuleFor(command => command.Currency).Matches("^[A-Za-z]{3}$");
    }
}

public sealed class CreateDraftProductValidator : AbstractValidator<CreateDraftProductCommand>
{
    public CreateDraftProductValidator()
    {
        RuleFor(command => command.CategoryId).NotEmpty();
        RuleFor(command => command.Sku).NotEmpty().MaximumLength(80);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Description).NotNull();
        RuleFor(command => command.Price).InclusiveBetween(0, 9_999_999_999_999_999.99m);
        RuleFor(command => command.Currency).Matches("^[A-Za-z]{3}$");
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateProductValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Description).NotNull();
        RuleFor(command => command.Price).InclusiveBetween(0, 9_999_999_999_999_999.99m);
    }
}

public sealed class ChangeProductPriceValidator : AbstractValidator<ChangeProductPriceCommand>
{
    public ChangeProductPriceValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.Price).InclusiveBetween(0, 9_999_999_999_999_999.99m);
        RuleFor(command => command.Currency).Matches("^[A-Za-z]{3}$");
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
    }
}

internal sealed class CreateProductHandler(
    ICategoryRepository categories,
    IProductRepository products,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var product = await CreateAsync(
            request.CategoryId,
            request.Sku,
            request.Name,
            request.Description,
            request.Price,
            request.Currency,
            "legacy-api",
            now,
            categories,
            products,
            cancellationToken);
        product.Publish("legacy-api", now);
        products.Add(product);
        snapshots.Publish(product);
        audit.Record("Product", product.Id, "CreatedAndPublished", "legacy-api", now, new { product.Sku });
        return product.Id;
    }

    internal static async Task<Product> CreateAsync(
        Guid categoryId,
        string sku,
        string name,
        string description,
        decimal price,
        string currency,
        string actor,
        DateTimeOffset now,
        ICategoryRepository categories,
        IProductRepository products,
        CancellationToken cancellationToken)
    {
        var category = await categories.FindAsync(categoryId, cancellationToken)
            ?? throw new KeyNotFoundException("Category was not found.");
        if (category.Status != CategoryLifecycleStatus.Active)
            throw new InvalidOperationException("Products can only be assigned to an active category.");

        var normalizedSku = Product.NormalizeSku(sku);
        if (await products.SkuExistsAsync(normalizedSku, null, cancellationToken))
            throw new InvalidOperationException($"SKU '{sku}' already exists.");

        return new Product(Guid.NewGuid(), categoryId, sku, name, description, price, currency, actor, now);
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
        var now = clock.GetUtcNow();
        var product = await CreateProductHandler.CreateAsync(
            request.CategoryId,
            request.Sku,
            request.Name,
            request.Description,
            request.Price,
            request.Currency,
            request.Actor,
            now,
            categories,
            products,
            cancellationToken);
        products.Add(product);
        snapshots.Publish(product);
        audit.Record("Product", product.Id, "DraftCreated", request.Actor, now, new { product.Sku });
        return product.Id;
    }
}

internal sealed class UpdateProductHandler(
    IProductRepository products,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<UpdateProductCommand, bool>
{
    public async Task<bool> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await ProductHandlerHelpers.GetAsync(products, request.Id, cancellationToken);
        var now = clock.GetUtcNow();
        product.UpdateDetails(request.Name, request.Description, "legacy-api", now);
        product.ChangePrice(request.Price, product.Currency, "legacy-api", now);
        if (request.IsActive && !product.IsActive)
            product.Publish("legacy-api", now);
        else if (!request.IsActive && product.IsActive)
            product.Unpublish("legacy-api", now);

        snapshots.Publish(product);
        audit.Record("Product", product.Id, "LegacyUpdated", "legacy-api", now);
        return true;
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
        var product = await ProductHandlerHelpers.GetAsync(products, request.ProductId, cancellationToken);
        ProductHandlerHelpers.EnsureVersion(product.Version, request.ExpectedVersion);
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
        var product = await ProductHandlerHelpers.GetAsync(products, request.ProductId, cancellationToken);
        ProductHandlerHelpers.EnsureVersion(product.Version, request.ExpectedVersion);
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
        var product = await ProductHandlerHelpers.GetAsync(products, request.ProductId, cancellationToken);
        ProductHandlerHelpers.EnsureVersion(product.Version, request.ExpectedVersion);
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
        var product = await ProductHandlerHelpers.GetAsync(products, request.ProductId, cancellationToken);
        ProductHandlerHelpers.EnsureVersion(product.Version, request.ExpectedVersion);
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
        var product = await ProductHandlerHelpers.GetAsync(products, request.ProductId, cancellationToken);
        ProductHandlerHelpers.EnsureVersion(product.Version, request.ExpectedVersion);
        var now = clock.GetUtcNow();
        product.Archive(request.Actor, now);
        snapshots.Publish(product);
        audit.Record("Product", product.Id, "Archived", request.Actor, now, new { request.Reason });
        return true;
    }
}

internal sealed class GetProductHandler(ICatalogReadService reads)
    : IRequestHandler<GetProductQuery, ProductDto?>
{
    public Task<ProductDto?> Handle(GetProductQuery request, CancellationToken cancellationToken) =>
        reads.GetProductAsync(request.Id, publicOnly: true, cancellationToken);
}

internal sealed class GetAdminProductHandler(ICatalogReadService reads)
    : IRequestHandler<GetAdminProductQuery, ProductDto?>
{
    public Task<ProductDto?> Handle(GetAdminProductQuery request, CancellationToken cancellationToken) =>
        reads.GetProductAsync(request.Id, publicOnly: false, cancellationToken);
}

internal sealed class ListProductsHandler(ICatalogReadService reads)
    : IRequestHandler<ListProductsQuery, IReadOnlyList<ProductDto>>
{
    public async Task<IReadOnlyList<ProductDto>> Handle(ListProductsQuery request, CancellationToken cancellationToken) =>
        (await reads.ListProductsAsync(
            request.Skip,
            request.Take,
            publicOnly: true,
            search: null,
            lifecycleStatus: null,
            categoryId: null,
            cancellationToken)).Items;
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

file static class ProductHandlerHelpers
{
    public static async Task<Product> GetAsync(
        IProductRepository products,
        Guid productId,
        CancellationToken cancellationToken) =>
        await products.FindAsync(productId, cancellationToken)
        ?? throw new KeyNotFoundException("Product was not found.");

    public static void EnsureVersion(long current, long? expected)
    {
        if (expected.HasValue && expected.Value != current)
            throw new InvalidOperationException($"Product version mismatch. Expected {expected}, current {current}.");
    }
}
