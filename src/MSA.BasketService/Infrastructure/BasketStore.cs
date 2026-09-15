using Microsoft.EntityFrameworkCore;
using MSA.BasketService.Application;
using MSA.BasketService.Application.Abstractions;
using MSA.BasketService.Domain;

namespace MSA.BasketService.Infrastructure;

public sealed class BasketStore(BasketDbContext db, TimeProvider clock) :
    IBasketRepository,
    IBasketProductCatalog,
    IBasketReadService
{
    public Task<Basket?> FindByUserAsync(string userId, CancellationToken cancellationToken) =>
        db.Baskets
            .Include(value => value.Items)
            .Include(value => value.Coupons)
            .SingleOrDefaultAsync(value => value.UserId == userId.Trim(), cancellationToken);

    public void Add(Basket basket) => db.Baskets.Add(basket);

    public async Task<ProductSelection?> FindBySkuAsync(
        string sku,
        CancellationToken cancellationToken)
    {
        var normalizedSku = Basket.NormalizeSku(sku);
        var snapshot = await db.ProductSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Sku == normalizedSku, cancellationToken);
        if (snapshot is null)
            return null;

        var selection = snapshot.ToSelection();
        return selection with
        {
            IsAvailable = selection.IsAvailable && snapshot.EffectiveOnUtc <= clock.GetUtcNow()
        };
    }

    public async Task<BasketDto> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var basket = await db.Baskets
            .AsNoTracking()
            .Include(value => value.Items)
            .Include(value => value.Coupons)
            .SingleOrDefaultAsync(value => value.UserId == userId.Trim(), cancellationToken);
        if (basket is null)
            return BasketDto.Empty();

        var variantIds = basket.Items.Select(value => value.VariantId).Distinct().ToArray();
        var snapshots = await db.ProductSnapshots
            .AsNoTracking()
            .Where(value => variantIds.Contains(value.VariantId))
            .ToDictionaryAsync(value => value.VariantId, cancellationToken);
        var now = clock.GetUtcNow();
        var items = basket.Items
            .OrderBy(value => value.AddedOnUtc)
            .ThenBy(value => value.Sku, StringComparer.Ordinal)
            .Select(item => MapItem(item, snapshots.GetValueOrDefault(item.VariantId), now))
            .ToArray();
        var totals = items
            .Where(item => item.IsAvailable)
            .GroupBy(item => item.Currency, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new BasketMoneyTotalDto(
                group.Key,
                decimal.Round(group.Sum(item => item.LineAmount), 2, MidpointRounding.AwayFromZero)))
            .ToArray();

        return new BasketDto(
            basket.Id,
            basket.Version,
            items,
            basket.Coupons
                .OrderBy(value => value.AddedOnUtc)
                .ThenBy(value => value.Code, StringComparer.Ordinal)
                .Select(value => value.Code)
                .ToArray(),
            totals,
            items.Any(item => !item.IsAvailable),
            items.Any(item => item.PriceChangedSinceSelection),
            basket.UpdatedOnUtc);
    }

    private static BasketItemDto MapItem(
        BasketItem item,
        BasketProductSnapshot? current,
        DateTimeOffset now)
    {
        var isAvailable = current is not null
            && current.IsAvailable
            && current.EffectiveOnUtc <= now;
        var productName = current?.ProductName ?? item.ProductNameAtSelection;
        var variantName = current?.VariantName ?? item.VariantNameAtSelection;
        var attributesJson = current?.AttributesJson ?? item.VariantAttributesJsonAtSelection;
        var unitPrice = current?.Price ?? item.UnitPriceAtSelection;
        var currency = current?.Currency ?? item.CurrencyAtSelection;
        var revision = current?.SourceRevision ?? item.ProductRevisionAtSelection;
        var priceChanged = current is null
            || current.Price != item.UnitPriceAtSelection
            || !string.Equals(current.Currency, item.CurrencyAtSelection, StringComparison.Ordinal);

        return new BasketItemDto(
            item.ProductId,
            item.VariantId,
            current?.Sku ?? item.Sku,
            productName,
            variantName,
            attributesJson,
            item.Quantity,
            unitPrice,
            currency,
            decimal.Round(unitPrice * item.Quantity, 2, MidpointRounding.AwayFromZero),
            isAvailable,
            revision,
            item.UnitPriceAtSelection,
            item.CurrencyAtSelection,
            item.ProductRevisionAtSelection,
            priceChanged,
            item.AddedOnUtc,
            item.UpdatedOnUtc);
    }
}
