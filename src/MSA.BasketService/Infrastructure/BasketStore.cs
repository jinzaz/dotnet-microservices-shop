using Microsoft.EntityFrameworkCore;
using MSA.BasketService.Application;
using MSA.BasketService.Application.Abstractions;
using MSA.BasketService.Domain;
using MSA.Grpc.Contracts.Catalog;

namespace MSA.BasketService.Infrastructure;

public sealed class BasketStore(
    BasketDbContext db,
    ICatalogPriceAuthority priceAuthority,
    TimeProvider clock) :
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
        // 结算页看到的价格要和下单时一致：投影是最终一致的，改价与下架可能还没同步过来。
        // 问不到就用投影 —— 购物车本来就是预览，不该因为下游抖动而打不开。
        var confirmation = await priceAuthority.ConfirmAsync(
            basket.Items.Select(value => value.Sku).Distinct(StringComparer.Ordinal).ToArray(),
            cancellationToken);
        var items = basket.Items
            .OrderBy(value => value.AddedOnUtc)
            .ThenBy(value => value.Sku, StringComparer.Ordinal)
            .Select(item => MapItem(item, snapshots.GetValueOrDefault(item.VariantId), confirmation, now))
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

    /// <param name="confirmation">
    /// 目录此刻的权威判定；null 表示这次问不到，此时退回投影。
    /// 确认到了但这个 SKU 不在其中，说明目录已经没有它了 —— 按不可售处理。
    /// </param>
    private static BasketItemDto MapItem(
        BasketItem item,
        BasketProductSnapshot? current,
        PriceConfirmation? confirmation,
        DateTimeOffset now)
    {
        var authority = confirmation?.Items.GetValueOrDefault(item.Sku);
        var isAvailable = confirmation is not null
            ? authority is { IsActive: true }
            : current is not null && current.IsAvailable && current.EffectiveOnUtc <= now;
        var productName = authority?.ProductName ?? current?.ProductName ?? item.ProductNameAtSelection;
        var variantName = authority?.VariantName ?? current?.VariantName ?? item.VariantNameAtSelection;
        var attributesJson = authority?.AttributesJson ?? current?.AttributesJson ?? item.VariantAttributesJsonAtSelection;
        var unitPrice = authority?.Price ?? current?.Price ?? item.UnitPriceAtSelection;
        var currency = authority?.Currency ?? current?.Currency ?? item.CurrencyAtSelection;
        var revision = authority?.Revision ?? current?.SourceRevision ?? item.ProductRevisionAtSelection;
        // 完全问不到这个商品（既没有权威判定也没有投影）也算「和选购时不一样」，提示用户重新确认。
        var priceChanged = (authority is null && current is null)
            || unitPrice != item.UnitPriceAtSelection
            || !string.Equals(currency, item.CurrencyAtSelection, StringComparison.Ordinal);

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
