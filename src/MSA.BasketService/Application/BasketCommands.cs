using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using MSA.BasketService.Application.Abstractions;
using MSA.BasketService.Domain;
using MSA.Infrastrucute.Mediator;

namespace MSA.BasketService.Application;

public sealed record AddBasketItemCommand(
    string UserId,
    string Sku,
    int Quantity,
    long? ExpectedVersion) : ICommand<BasketMutationResult>;

public sealed record SetBasketItemQuantityCommand(
    string UserId,
    string Sku,
    int Quantity,
    long? ExpectedVersion) : ICommand<BasketMutationResult>;

public sealed record RemoveBasketItemCommand(
    string UserId,
    string Sku,
    long? ExpectedVersion) : ICommand<BasketMutationResult>;

public sealed record ClearBasketCommand(
    string UserId,
    long? ExpectedVersion) : ICommand<BasketMutationResult>;

public sealed record ReplaceBasketCouponsCommand(
    string UserId,
    IReadOnlyList<string> CouponCodes,
    long? ExpectedVersion) : ICommand<BasketMutationResult>;

internal abstract class BasketCommandHandler(
    IBasketRepository baskets,
    IBasketMutationLock mutationLock,
    TimeProvider clock)
{
    protected async Task<Basket?> LockAndFindAsync(string userId, CancellationToken cancellationToken)
    {
        await mutationLock.AcquireAsync(userId, cancellationToken);
        return await baskets.FindByUserAsync(userId, cancellationToken);
    }

    protected Basket Create(string userId, long? expectedVersion)
    {
        if (expectedVersion.HasValue && expectedVersion.Value != 0)
            throw new BasketVersionConflictException(expectedVersion.Value, 0);

        var basket = new Basket(Guid.NewGuid(), userId, clock.GetUtcNow());
        baskets.Add(basket);
        return basket;
    }

    protected DateTimeOffset UtcNow => clock.GetUtcNow();
}

internal sealed class AddBasketItemCommandHandler(
    IBasketRepository baskets,
    IBasketProductCatalog products,
    IBasketMutationLock mutationLock,
    IOptions<BasketOptions> options,
    TimeProvider clock)
    : BasketCommandHandler(baskets, mutationLock, clock),
      IRequestHandler<AddBasketItemCommand, BasketMutationResult>
{
    public async Task<BasketMutationResult> Handle(
        AddBasketItemCommand request,
        CancellationToken cancellationToken)
    {
        var basket = await LockAndFindAsync(request.UserId, cancellationToken);
        var isNew = basket is null;
        basket ??= Create(request.UserId, request.ExpectedVersion);
        var product = await products.FindBySkuAsync(request.Sku, cancellationToken)
            ?? throw new ProductUnavailableException(request.Sku);

        basket.AddOrIncreaseItem(
            product,
            request.Quantity,
            options.Value.MaximumDistinctItems,
            options.Value.MaximumLineQuantity,
            isNew ? null : request.ExpectedVersion,
            UtcNow);
        return new BasketMutationResult(basket.Id, basket.Version);
    }
}

internal sealed class SetBasketItemQuantityCommandHandler(
    IBasketRepository baskets,
    IBasketProductCatalog products,
    IBasketMutationLock mutationLock,
    IOptions<BasketOptions> options,
    TimeProvider clock)
    : BasketCommandHandler(baskets, mutationLock, clock),
      IRequestHandler<SetBasketItemQuantityCommand, BasketMutationResult>
{
    public async Task<BasketMutationResult> Handle(
        SetBasketItemQuantityCommand request,
        CancellationToken cancellationToken)
    {
        var basket = await LockAndFindAsync(request.UserId, cancellationToken)
            ?? throw new KeyNotFoundException("Basket was not found.");
        var product = await products.FindBySkuAsync(request.Sku, cancellationToken);
        basket.SetItemQuantity(
            request.Sku,
            request.Quantity,
            product,
            options.Value.MaximumLineQuantity,
            request.ExpectedVersion,
            UtcNow);
        return new BasketMutationResult(basket.Id, basket.Version);
    }
}

internal sealed class RemoveBasketItemCommandHandler(
    IBasketRepository baskets,
    IBasketMutationLock mutationLock,
    TimeProvider clock)
    : BasketCommandHandler(baskets, mutationLock, clock),
      IRequestHandler<RemoveBasketItemCommand, BasketMutationResult>
{
    public async Task<BasketMutationResult> Handle(
        RemoveBasketItemCommand request,
        CancellationToken cancellationToken)
    {
        var basket = await LockAndFindAsync(request.UserId, cancellationToken);
        if (basket is null)
        {
            if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != 0)
                throw new BasketVersionConflictException(request.ExpectedVersion.Value, 0);
            return new BasketMutationResult(Guid.Empty, 0);
        }

        basket.RemoveItem(request.Sku, request.ExpectedVersion, UtcNow);
        return new BasketMutationResult(basket.Id, basket.Version);
    }
}

internal sealed class ClearBasketCommandHandler(
    IBasketRepository baskets,
    IBasketMutationLock mutationLock,
    TimeProvider clock)
    : BasketCommandHandler(baskets, mutationLock, clock),
      IRequestHandler<ClearBasketCommand, BasketMutationResult>
{
    public async Task<BasketMutationResult> Handle(
        ClearBasketCommand request,
        CancellationToken cancellationToken)
    {
        var basket = await LockAndFindAsync(request.UserId, cancellationToken);
        if (basket is null)
        {
            if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != 0)
                throw new BasketVersionConflictException(request.ExpectedVersion.Value, 0);
            return new BasketMutationResult(Guid.Empty, 0);
        }

        basket.Clear(request.ExpectedVersion, UtcNow);
        return new BasketMutationResult(basket.Id, basket.Version);
    }
}

internal sealed class ReplaceBasketCouponsCommandHandler(
    IBasketRepository baskets,
    IBasketMutationLock mutationLock,
    IOptions<BasketOptions> options,
    TimeProvider clock)
    : BasketCommandHandler(baskets, mutationLock, clock),
      IRequestHandler<ReplaceBasketCouponsCommand, BasketMutationResult>
{
    public async Task<BasketMutationResult> Handle(
        ReplaceBasketCouponsCommand request,
        CancellationToken cancellationToken)
    {
        var basket = await LockAndFindAsync(request.UserId, cancellationToken);
        if (basket is null && request.CouponCodes.Count == 0)
        {
            if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != 0)
                throw new BasketVersionConflictException(request.ExpectedVersion.Value, 0);
            return new BasketMutationResult(Guid.Empty, 0);
        }

        var isNew = basket is null;
        basket ??= Create(request.UserId, request.ExpectedVersion);
        basket.ReplaceCouponCodes(
            request.CouponCodes,
            options.Value.MaximumCouponCodes,
            isNew ? null : request.ExpectedVersion,
            UtcNow);
        return new BasketMutationResult(basket.Id, basket.Version);
    }
}

public sealed class AddBasketItemCommandValidator : AbstractValidator<AddBasketItemCommand>
{
    public AddBasketItemCommandValidator(IOptions<BasketOptions> options)
    {
        RuleFor(value => value.UserId).NotEmpty().MaximumLength(200);
        RuleFor(value => value.Sku).NotEmpty().MaximumLength(80);
        RuleFor(value => value.Quantity).InclusiveBetween(1, options.Value.MaximumLineQuantity);
        RuleFor(value => value.ExpectedVersion).GreaterThanOrEqualTo(0).When(value => value.ExpectedVersion.HasValue);
    }
}

public sealed class SetBasketItemQuantityCommandValidator : AbstractValidator<SetBasketItemQuantityCommand>
{
    public SetBasketItemQuantityCommandValidator(IOptions<BasketOptions> options)
    {
        RuleFor(value => value.UserId).NotEmpty().MaximumLength(200);
        RuleFor(value => value.Sku).NotEmpty().MaximumLength(80);
        RuleFor(value => value.Quantity).InclusiveBetween(1, options.Value.MaximumLineQuantity);
        RuleFor(value => value.ExpectedVersion).GreaterThanOrEqualTo(0).When(value => value.ExpectedVersion.HasValue);
    }
}

public sealed class RemoveBasketItemCommandValidator : AbstractValidator<RemoveBasketItemCommand>
{
    public RemoveBasketItemCommandValidator()
    {
        RuleFor(value => value.UserId).NotEmpty().MaximumLength(200);
        RuleFor(value => value.Sku).NotEmpty().MaximumLength(80);
        RuleFor(value => value.ExpectedVersion).GreaterThanOrEqualTo(0).When(value => value.ExpectedVersion.HasValue);
    }
}

public sealed class ClearBasketCommandValidator : AbstractValidator<ClearBasketCommand>
{
    public ClearBasketCommandValidator()
    {
        RuleFor(value => value.UserId).NotEmpty().MaximumLength(200);
        RuleFor(value => value.ExpectedVersion).GreaterThanOrEqualTo(0).When(value => value.ExpectedVersion.HasValue);
    }
}

public sealed class ReplaceBasketCouponsCommandValidator : AbstractValidator<ReplaceBasketCouponsCommand>
{
    public ReplaceBasketCouponsCommandValidator(IOptions<BasketOptions> options)
    {
        RuleFor(value => value.UserId).NotEmpty().MaximumLength(200);
        RuleFor(value => value.CouponCodes)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(codes => codes.Count <= options.Value.MaximumCouponCodes)
            .WithMessage($"At most {options.Value.MaximumCouponCodes} coupon codes are allowed.");
        RuleForEach(value => value.CouponCodes).NotEmpty().MaximumLength(80);
        RuleFor(value => value.ExpectedVersion).GreaterThanOrEqualTo(0).When(value => value.ExpectedVersion.HasValue);
    }
}
