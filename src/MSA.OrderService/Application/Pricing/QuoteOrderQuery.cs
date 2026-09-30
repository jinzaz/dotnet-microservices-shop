using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.Infrastrucute.Mediator;
using MSA.Grpc.Contracts.Catalog;
using MSA.OrderService.Application.Realtime;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;
using System.Text.Json;

namespace MSA.OrderService.Application.Pricing;

public sealed record QuoteItem(string Sku, int Quantity);

/// <summary>Prices a prospective order without reserving anything, so the storefront can preview discounts.</summary>
public sealed record QuoteOrderQuery(
    string UserId,
    IReadOnlyList<QuoteItem> Items,
    IReadOnlyList<string>? CouponCodes,
    IReadOnlyList<Guid>? WalletCouponIds) : IQuery<OrderQuoteDto>;

public sealed record QuoteLineDto(
    Guid ProductId,
    Guid VariantId,
    string Sku,
    string ProductName,
    string VariantName,
    int Quantity,
    decimal UnitPrice,
    decimal LineSubtotal);

public sealed record QuoteDiscountDto(
    Guid PromotionId,
    string Name,
    string SourceType,
    Guid? CouponId,
    string? MaskedCode,
    decimal Amount);

/// <param name="Key">The code as typed (normalized) or the wallet coupon id, so the client can match its input.</param>
/// <param name="Reason">A <see cref="CouponRejectionReason"/> name when the coupon was not applied.</param>
public sealed record QuoteCouponDto(
    string Key,
    Guid? CouponId,
    string? MaskedCode,
    bool Applied,
    decimal DiscountAmount,
    string? Reason);

public sealed record QuoteOpportunityDto(
    Guid PromotionId,
    string Name,
    decimal? MissingAmount,
    int? MissingQuantity,
    IReadOnlyList<PromotionBenefitContract> Benefits);

public sealed record OrderQuoteDto(
    IReadOnlyList<QuoteLineDto> Lines,
    IReadOnlyList<string> UnavailableSkus,
    string Currency,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal Total,
    IReadOnlyList<QuoteDiscountDto> Discounts,
    IReadOnlyList<QuoteCouponDto> Coupons,
    IReadOnlyList<QuoteOpportunityDto> Opportunities);

internal sealed class QuoteOrderQueryHandler(
    OrderDbContext db,
    IOrderPricingService pricing,
    ICatalogPriceAuthority priceAuthority,
    TimeProvider clock) : IRequestHandler<QuoteOrderQuery, OrderQuoteDto>
{
    private sealed record CouponRequest(string Key, string? Code, Guid? WalletId);

    public async Task<OrderQuoteDto> Handle(QuoteOrderQuery request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var quantities = request.Items
            .GroupBy(item => item.Sku.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity), StringComparer.Ordinal);
        var skus = quantities.Keys.ToArray();
        var products = await db.ProductProjections
            .AsNoTracking()
            .Where(product => skus.Contains(product.Sku) && product.IsActive && product.UpdatedOnUtc <= now)
            .ToDictionaryAsync(product => product.Sku, StringComparer.Ordinal, cancellationToken);
        // 报价是预览：能确认就用权威价格，问不到就用投影，不要因为下游抖动让用户看不到价格。
        var confirmation = await priceAuthority.ConfirmAsync(skus, cancellationToken);
        if (confirmation is not null)
        {
            foreach (var sku in products.Keys.ToArray())
            {
                if (confirmation.Items.TryGetValue(sku, out var authority) && authority.IsActive)
                    products[sku] = authority.ToPricingSnapshot(now);
                else
                    products.Remove(sku);
            }
        }

        var unavailable = skus.Where(sku => !products.ContainsKey(sku)).ToArray();
        var currency = products.Values.Select(product => product.Currency).Distinct(StringComparer.Ordinal).ToArray();
        var items = products.Values
            .OrderBy(product => product.Sku, StringComparer.Ordinal)
            .Select(product => new PricingItem(product, quantities[product.Sku]))
            .ToArray();
        var requests = (request.CouponCodes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => new CouponRequest(CouponCode.Normalize(code), CouponCode.Normalize(code), null))
            .Concat((request.WalletCouponIds ?? []).Select(id => new CouponRequest(id.ToString(), null, id)))
            .DistinctBy(item => item.Key)
            .ToArray();

        // Mixed currencies cannot be priced together; the checkout would reject them as well.
        if (items.Length == 0 || currency.Length != 1)
        {
            return new OrderQuoteDto([], unavailable, currency.FirstOrDefault() ?? "CNY", 0, 0, 0, [],
                requests.Select(item => new QuoteCouponDto(item.Key, null, null, false, 0,
                    currency.Length > 1 ? nameof(CouponRejectionReason.Conflict) : nameof(CouponRejectionReason.NotApplied))).ToArray(), []);
        }

        // Coupons are admitted one by one so each rejected coupon gets its own reason instead of failing the quote.
        var accepted = new List<CouponRequest>();
        var outcomes = new Dictionary<string, QuoteCouponDto>(StringComparer.Ordinal);
        foreach (var candidate in requests)
        {
            var attempt = accepted.Append(candidate).ToArray();
            try
            {
                var trial = await pricing.CalculateAsync(request.UserId, items, Selection(attempt), now, cancellationToken);
                var acceptedIds = await ResolveAcceptedIdsAsync(accepted, cancellationToken);
                var added = trial.Coupons.Single(coupon => !acceptedIds.Contains(coupon.CouponId));
                var usageProblem = await UsageProblemAsync(added, request.UserId, cancellationToken);
                if (usageProblem is not null)
                {
                    outcomes[candidate.Key] = new QuoteCouponDto(candidate.Key, added.CouponId, added.MaskedCode, false, 0, usageProblem.ToString());
                    continue;
                }
                accepted.Add(candidate);
            }
            catch (CouponRejectedException exception)
            {
                outcomes[candidate.Key] = new QuoteCouponDto(candidate.Key, exception.CouponId, null, false, 0, exception.Reason.ToString());
            }
        }

        var result = await pricing.CalculateAsync(request.UserId, items, Selection(accepted), now, cancellationToken);
        var couponsById = await db.CouponProjections
            .AsNoTracking()
            .Where(coupon => result.Coupons.Select(applied => applied.CouponId).Contains(coupon.Id))
            .Select(coupon => new { coupon.Id, coupon.CodeDigest })
            .ToArrayAsync(cancellationToken);
        foreach (var candidate in accepted)
        {
            var applied = result.Coupons.Single(coupon => candidate.WalletId.HasValue
                ? coupon.CouponId == candidate.WalletId.Value
                : couponsById.Any(value => value.Id == coupon.CouponId && value.CodeDigest == CouponCode.Digest(candidate.Code!)));
            outcomes[candidate.Key] = new QuoteCouponDto(candidate.Key, applied.CouponId, applied.MaskedCode, true, applied.DiscountAmount, null);
        }

        var maskedByCoupon = result.Coupons.ToDictionary(coupon => coupon.CouponId, coupon => coupon.MaskedCode);
        return new OrderQuoteDto(
            items.Select(item => new QuoteLineDto(
                item.Product.ProductId,
                item.Product.VariantId,
                item.Product.Sku,
                item.Product.ProductName,
                item.Product.VariantName,
                item.Quantity,
                item.Product.Price,
                Money.Round(item.Product.Price * item.Quantity))).ToArray(),
            unavailable,
            currency[0],
            result.Subtotal,
            result.DiscountTotal,
            Money.Round(result.Subtotal - result.DiscountTotal),
            result.Discounts.Select(discount => new QuoteDiscountDto(
                discount.PromotionId,
                discount.Description,
                discount.SourceType,
                discount.CouponId,
                discount.CouponId is Guid id && maskedByCoupon.TryGetValue(id, out var masked) ? masked : null,
                discount.Amount)).ToArray(),
            requests.Select(item => outcomes[item.Key]).ToArray(),
            result.Opportunities.Select(opportunity => new QuoteOpportunityDto(
                opportunity.PromotionId,
                opportunity.PromotionName,
                opportunity.MissingAmount,
                opportunity.MissingQuantity,
                JsonSerializer.Deserialize<IReadOnlyList<PromotionBenefitContract>>(opportunity.BenefitsJson) ?? [])).ToArray());
    }

    private static CouponSelection Selection(IEnumerable<CouponRequest> requests)
    {
        var list = requests.ToArray();
        return new CouponSelection(
            list.Where(item => item.Code is not null).Select(item => item.Code!).ToArray(),
            list.Where(item => item.WalletId.HasValue).Select(item => item.WalletId!.Value).ToArray());
    }

    private async Task<HashSet<Guid>> ResolveAcceptedIdsAsync(
        IReadOnlyList<CouponRequest> accepted,
        CancellationToken cancellationToken)
    {
        if (accepted.Count == 0)
            return [];
        var codeDigests = accepted.Where(item => item.Code is not null).Select(item => CouponCode.Digest(item.Code!)).ToArray();
        var codeIds = codeDigests.Length == 0
            ? []
            : await db.CouponProjections
                .AsNoTracking()
                .Where(coupon => codeDigests.Contains(coupon.CodeDigest))
                .Select(coupon => coupon.Id)
                .ToArrayAsync(cancellationToken);
        return codeIds.Concat(accepted.Where(item => item.WalletId.HasValue).Select(item => item.WalletId!.Value)).ToHashSet();
    }

    /// <summary>Read-only preview of the checks the redemption store enforces when the order is placed.</summary>
    private async Task<CouponRejectionReason?> UsageProblemAsync(
        AppliedCoupon coupon,
        string userId,
        CancellationToken cancellationToken)
    {
        var used = await db.CouponRedemptions.CountAsync(
            redemption => redemption.CouponId == coupon.CouponId
                && redemption.UserId == userId
                && redemption.Status != CouponRedemptionStatus.Released,
            cancellationToken);
        if (used >= coupon.PerUserLimit)
            return CouponRejectionReason.UsageLimitReached;
        if (!coupon.HasTotalLimit)
            return null;

        var remaining = await db.CouponQuotaBuckets
            .Where(bucket => bucket.CouponId == coupon.CouponId)
            .SumAsync(bucket => bucket.Capacity - bucket.ReservedCount - bucket.ConsumedCount, cancellationToken);
        return remaining > 0 ? null : CouponRejectionReason.QuotaExhausted;
    }
}
