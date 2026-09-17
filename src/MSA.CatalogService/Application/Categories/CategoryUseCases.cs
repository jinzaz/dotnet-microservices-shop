using FluentValidation;
using MediatR;
using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Domain;
using MSA.Infrastrucute.Mediator;

namespace MSA.CatalogService.Application;

public sealed record CreateCategoryAdminCommand(string Code, string Name, string Actor) : ICommand<Guid>;

public sealed record RenameCategoryCommand(
    Guid CategoryId,
    string Name,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record ActivateCategoryCommand(
    Guid CategoryId,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record ArchiveCategoryCommand(
    Guid CategoryId,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record ListCategoriesQuery(int Skip, int Take, string? Status)
    : IQuery<PagedResult<CategoryDto>>;

public sealed class CreateCategoryAdminValidator : AbstractValidator<CreateCategoryAdminCommand>
{
    public CreateCategoryAdminValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(60);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(120);
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
    }
}

internal sealed class CreateCategoryAdminHandler(
    ICategoryRepository categories,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<CreateCategoryAdminCommand, Guid>
{
    public async Task<Guid> Handle(CreateCategoryAdminCommand request, CancellationToken cancellationToken)
    {
        var normalizedCode = Category.NormalizeCode(request.Code);
        if (await categories.CodeExistsAsync(normalizedCode, null, cancellationToken))
            throw new InvalidOperationException($"Category code '{request.Code}' already exists.");

        var id = Guid.NewGuid();
        var now = clock.GetUtcNow();
        categories.Add(new Category(id, request.Code, request.Name, request.Actor, now));
        audit.Record("Category", id, "Created", request.Actor, now, new { request.Code, request.Name });
        return id;
    }
}

internal sealed class RenameCategoryHandler(
    ICategoryRepository categories,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<RenameCategoryCommand, bool>
{
    public async Task<bool> Handle(RenameCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await categories.GetRequiredAsync(request.CategoryId, cancellationToken);
        category.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        category.Rename(request.Name, request.Actor, now);
        audit.Record("Category", category.Id, "Renamed", request.Actor, now, new { request.Name });
        return true;
    }
}

internal sealed class ActivateCategoryHandler(
    ICategoryRepository categories,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<ActivateCategoryCommand, bool>
{
    public async Task<bool> Handle(ActivateCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await categories.GetRequiredAsync(request.CategoryId, cancellationToken);
        category.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        category.Activate(request.Actor, now);
        audit.Record("Category", category.Id, "Activated", request.Actor, now);
        return true;
    }
}

internal sealed class ArchiveCategoryHandler(
    ICategoryRepository categories,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<ArchiveCategoryCommand, bool>
{
    public async Task<bool> Handle(ArchiveCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await categories.GetRequiredAsync(request.CategoryId, cancellationToken);
        category.EnsureVersion(request.ExpectedVersion);
        if (await categories.HasPublishedProductsAsync(category.Id, cancellationToken))
            throw new InvalidOperationException("A category containing published products cannot be archived.");

        var now = clock.GetUtcNow();
        category.Archive(request.Actor, now);
        audit.Record("Category", category.Id, "Archived", request.Actor, now);
        return true;
    }
}

internal sealed class ListCategoriesHandler(ICatalogReadService reads)
    : IRequestHandler<ListCategoriesQuery, PagedResult<CategoryDto>>
{
    public Task<PagedResult<CategoryDto>> Handle(ListCategoriesQuery request, CancellationToken cancellationToken) =>
        reads.ListCategoriesAsync(request.Skip, request.Take, request.Status, cancellationToken);
}
