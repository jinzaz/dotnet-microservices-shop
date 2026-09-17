using FluentValidation;
using MediatR;
using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Domain;
using MSA.Infrastrucute.Mediator;

namespace MSA.CatalogService.Application;

public sealed record CreateProductVariantCommand(
    Guid ProductId,
    string Sku,
    string DisplayName,
    string AttributesJson,
    string? Barcode,
    decimal Price,
    string Currency,
    bool IsActive,
    string Actor) : ICommand<Guid>;

public sealed record UpdateProductVariantCommand(
    Guid ProductId,
    Guid VariantId,
    string DisplayName,
    string AttributesJson,
    string? Barcode,
    decimal Price,
    string Currency,
    bool IsActive,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record DeleteProductVariantCommand(
    Guid ProductId,
    Guid VariantId,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record AddProductMediaCommand(
    Guid ProductId,
    Guid? VariantId,
    ProductMediaType MediaType,
    string Url,
    string? ThumbnailUrl,
    string? AltText,
    int SortOrder,
    long? ExpectedVersion,
    string Actor) : ICommand<Guid>;

public sealed record UpdateProductMediaCommand(
    Guid ProductId,
    Guid MediaId,
    Guid? VariantId,
    ProductMediaType MediaType,
    string Url,
    string? ThumbnailUrl,
    string? AltText,
    int SortOrder,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record DeleteProductMediaCommand(
    Guid ProductId,
    Guid MediaId,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed class CreateProductVariantValidator : AbstractValidator<CreateProductVariantCommand>
{
    public CreateProductVariantValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.Sku).NotEmpty().MaximumLength(80);
        RuleFor(command => command.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(command => command.AttributesJson).Must(CatalogValidation.IsJsonObjectOrEmpty)
            .WithMessage("AttributesJson must be a JSON object.");
        RuleFor(command => command.Barcode).MaximumLength(80);
        RuleFor(command => command.Price).InclusiveBetween(0, CatalogValidation.MaximumPrice);
        RuleFor(command => command.Currency).Matches(CatalogValidation.CurrencyPattern);
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateProductVariantValidator : AbstractValidator<UpdateProductVariantCommand>
{
    public UpdateProductVariantValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.VariantId).NotEmpty();
        RuleFor(command => command.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(command => command.AttributesJson).Must(CatalogValidation.IsJsonObjectOrEmpty)
            .WithMessage("AttributesJson must be a JSON object.");
        RuleFor(command => command.Barcode).MaximumLength(80);
        RuleFor(command => command.Price).InclusiveBetween(0, CatalogValidation.MaximumPrice);
        RuleFor(command => command.Currency).Matches(CatalogValidation.CurrencyPattern);
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
    }
}

public sealed class ProductMediaCommandValidator :
    AbstractValidator<AddProductMediaCommand>
{
    public ProductMediaCommandValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.VariantId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("VariantId cannot be an empty GUID.");
        RuleFor(command => command.MediaType).IsInEnum();
        RuleFor(command => command.Url).NotEmpty().MaximumLength(2_000);
        RuleFor(command => command.ThumbnailUrl).MaximumLength(2_000);
        RuleFor(command => command.AltText).MaximumLength(300);
        RuleFor(command => command.SortOrder).InclusiveBetween(0, 10_000);
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateProductMediaValidator : AbstractValidator<UpdateProductMediaCommand>
{
    public UpdateProductMediaValidator()
    {
        RuleFor(command => command.MediaId).NotEmpty();
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.VariantId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("VariantId cannot be an empty GUID.");
        RuleFor(command => command.MediaType).IsInEnum();
        RuleFor(command => command.Url).NotEmpty().MaximumLength(2_000);
        RuleFor(command => command.ThumbnailUrl).MaximumLength(2_000);
        RuleFor(command => command.AltText).MaximumLength(300);
        RuleFor(command => command.SortOrder).InclusiveBetween(0, 10_000);
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
    }
}

internal sealed class CreateProductVariantHandler(
    IProductRepository products,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<CreateProductVariantCommand, Guid>
{
    public async Task<Guid> Handle(CreateProductVariantCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetRequiredAsync(request.ProductId, cancellationToken);
        var normalizedSku = Product.NormalizeSku(request.Sku);
        if (await products.SkuExistsAsync(normalizedSku, null, cancellationToken))
            throw new InvalidOperationException($"SKU '{request.Sku}' already exists.");

        var now = clock.GetUtcNow();
        var variant = product.AddVariant(
            Guid.NewGuid(),
            request.Sku,
            request.DisplayName,
            request.AttributesJson,
            request.Barcode,
            request.Price,
            request.Currency,
            request.IsActive,
            request.Actor,
            now);
        snapshots.Publish(product);
        audit.Record("Product", product.Id, "VariantAdded", request.Actor, now, new { variant.Id, variant.Sku });
        return variant.Id;
    }
}

internal sealed class UpdateProductVariantHandler(
    IProductRepository products,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<UpdateProductVariantCommand, bool>
{
    public async Task<bool> Handle(UpdateProductVariantCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetRequiredAsync(request.ProductId, cancellationToken);
        product.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        product.UpdateVariant(
            request.VariantId,
            request.DisplayName,
            request.AttributesJson,
            request.Barcode,
            request.Price,
            request.Currency,
            request.IsActive,
            request.Actor,
            now);
        snapshots.Publish(product);
        audit.Record("Product", product.Id, "VariantUpdated", request.Actor, now, new { request.VariantId });
        return true;
    }
}

internal sealed class DeleteProductVariantHandler(
    IProductRepository products,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<DeleteProductVariantCommand, bool>
{
    public async Task<bool> Handle(DeleteProductVariantCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetRequiredAsync(request.ProductId, cancellationToken);
        product.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        var removed = product.RemoveVariant(request.VariantId, request.Actor, now);
        snapshots.PublishUnavailable(product, removed);
        snapshots.Publish(product);
        audit.Record("Product", product.Id, "VariantRemoved", request.Actor, now, new { request.VariantId });
        return true;
    }
}

internal sealed class AddProductMediaHandler(
    IProductRepository products,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<AddProductMediaCommand, Guid>
{
    public async Task<Guid> Handle(AddProductMediaCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetRequiredAsync(request.ProductId, cancellationToken);
        product.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        var media = product.AddMedia(
            Guid.NewGuid(), request.VariantId, request.MediaType, request.Url,
            request.ThumbnailUrl, request.AltText, request.SortOrder, request.Actor, now);
        snapshots.Publish(product);
        audit.Record("Product", product.Id, "MediaAdded", request.Actor, now, new { media.Id });
        return media.Id;
    }
}

internal sealed class UpdateProductMediaHandler(
    IProductRepository products,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<UpdateProductMediaCommand, bool>
{
    public async Task<bool> Handle(UpdateProductMediaCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetRequiredAsync(request.ProductId, cancellationToken);
        product.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        product.UpdateMedia(
            request.MediaId, request.VariantId, request.MediaType, request.Url,
            request.ThumbnailUrl, request.AltText, request.SortOrder, request.Actor, now);
        snapshots.Publish(product);
        audit.Record("Product", product.Id, "MediaUpdated", request.Actor, now, new { request.MediaId });
        return true;
    }
}

internal sealed class DeleteProductMediaHandler(
    IProductRepository products,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<DeleteProductMediaCommand, bool>
{
    public async Task<bool> Handle(DeleteProductMediaCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetRequiredAsync(request.ProductId, cancellationToken);
        product.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        product.RemoveMedia(request.MediaId, request.Actor, now);
        snapshots.Publish(product);
        audit.Record("Product", product.Id, "MediaRemoved", request.Actor, now, new { request.MediaId });
        return true;
    }
}
