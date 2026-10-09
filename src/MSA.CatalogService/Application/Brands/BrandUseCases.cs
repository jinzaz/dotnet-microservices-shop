using FluentValidation;
using MediatR;
using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Domain;
using MSA.Infrastrucute.Mediator;

namespace MSA.CatalogService.Application;

public sealed record CreateBrandCommand(string Code, string Name, string Actor) : ICommand<Guid>;

public sealed record RenameBrandCommand(Guid BrandId, string Name, long? ExpectedVersion, string Actor) : ICommand<bool>;

public sealed record ActivateBrandCommand(Guid BrandId, long? ExpectedVersion, string Actor) : ICommand<bool>;

public sealed record ArchiveBrandCommand(Guid BrandId, long? ExpectedVersion, string Actor) : ICommand<bool>;

public sealed record ListBrandsQuery(int Skip, int Take, string? Status, string? Search = null) : IQuery<PagedResult<BrandDto>>;

/// <summary>给商品换品牌或去掉品牌（BrandId 为 null）。</summary>
public sealed record ChangeProductBrandCommand(Guid ProductId, Guid? BrandId, long? ExpectedVersion, string Actor) : ICommand<bool>;

public sealed class CreateBrandValidator : AbstractValidator<CreateBrandCommand>
{
    public CreateBrandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(60);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(120);
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
    }
}

internal sealed class CreateBrandHandler(
    IBrandRepository brands,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<CreateBrandCommand, Guid>
{
    public async Task<Guid> Handle(CreateBrandCommand request, CancellationToken cancellationToken)
    {
        if (await brands.CodeExistsAsync(Brand.NormalizeCode(request.Code), null, cancellationToken))
            throw new InvalidOperationException($"Brand code '{request.Code}' already exists.");

        var id = Guid.NewGuid();
        var now = clock.GetUtcNow();
        brands.Add(new Brand(id, request.Code, request.Name, request.Actor, now));
        audit.Record("Brand", id, "Created", request.Actor, now, new { request.Code, request.Name });
        return id;
    }
}

internal sealed class RenameBrandHandler(
    IBrandRepository brands,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<RenameBrandCommand, bool>
{
    public async Task<bool> Handle(RenameBrandCommand request, CancellationToken cancellationToken)
    {
        var brand = await brands.GetRequiredAsync(request.BrandId, cancellationToken);
        brand.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        brand.Rename(request.Name, request.Actor, now);
        audit.Record("Brand", brand.Id, "Renamed", request.Actor, now, new { request.Name });
        return true;
    }
}

internal sealed class ActivateBrandHandler(
    IBrandRepository brands,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<ActivateBrandCommand, bool>
{
    public async Task<bool> Handle(ActivateBrandCommand request, CancellationToken cancellationToken)
    {
        var brand = await brands.GetRequiredAsync(request.BrandId, cancellationToken);
        brand.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        brand.Activate(request.Actor, now);
        audit.Record("Brand", brand.Id, "Activated", request.Actor, now);
        return true;
    }
}

internal sealed class ArchiveBrandHandler(
    IBrandRepository brands,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<ArchiveBrandCommand, bool>
{
    public async Task<bool> Handle(ArchiveBrandCommand request, CancellationToken cancellationToken)
    {
        var brand = await brands.GetRequiredAsync(request.BrandId, cancellationToken);
        brand.EnsureVersion(request.ExpectedVersion);
        // 与分类一致：还有在售商品时不能归档，先把商品下架或换品牌。
        if (await brands.HasPublishedProductsAsync(brand.Id, cancellationToken))
            throw new InvalidOperationException("A brand with published products cannot be archived.");

        var now = clock.GetUtcNow();
        brand.Archive(request.Actor, now);
        audit.Record("Brand", brand.Id, "Archived", request.Actor, now);
        return true;
    }
}

internal sealed class ListBrandsHandler(ICatalogReadService reads) : IRequestHandler<ListBrandsQuery, PagedResult<BrandDto>>
{
    public Task<PagedResult<BrandDto>> Handle(ListBrandsQuery request, CancellationToken cancellationToken) =>
        reads.ListBrandsAsync(request.Skip, request.Take, request.Status, request.Search, cancellationToken);
}

internal sealed class ChangeProductBrandHandler(
    IBrandRepository brands,
    IProductRepository products,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<ChangeProductBrandCommand, bool>
{
    public async Task<bool> Handle(ChangeProductBrandCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetRequiredAsync(request.ProductId, cancellationToken);
        product.EnsureVersion(request.ExpectedVersion);
        if (request.BrandId is { } brandId)
            await BrandRules.EnsureAssignableAsync(brands, brandId, cancellationToken);

        var now = clock.GetUtcNow();
        product.ChangeBrand(request.BrandId, request.Actor, now);
        snapshots.Publish(product);
        audit.Record("Product", product.Id, "BrandChanged", request.Actor, now, new { request.BrandId });
        return true;
    }
}

internal static class BrandRules
{
    /// <summary>只有启用的品牌能挂到商品上。</summary>
    public static async Task EnsureAssignableAsync(IBrandRepository brands, Guid brandId, CancellationToken cancellationToken)
    {
        var brand = await brands.GetRequiredAsync(brandId, cancellationToken);
        if (brand.Status != BrandLifecycleStatus.Active)
            throw new InvalidOperationException("Products can only be assigned to an active brand.");
    }
}
