using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MSA.OrderService.Application.Pricing;

public interface IOrderPricingService
{
    Task<OrderPricingResult> CalculateAsync(
        string userId,
        IReadOnlyList<PricingItem> items,
        IReadOnlyList<string> couponCodes,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface ICouponRedemptionStore
{
    Task ReserveAsync(
        Guid orderId,
        string userId,
        IReadOnlyList<AppliedCoupon> coupons,
        DateTimeOffset expiresOnUtc,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task ConsumeAsync(Guid orderId, DateTimeOffset now, CancellationToken cancellationToken);
    Task ReleaseAsync(Guid orderId, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record PricingItem(
    OrderProductProjection Product,
    int Quantity);

public sealed record AppliedCoupon(
    Guid CouponId,
    string MaskedCode,
    int PerUserLimit,
    bool HasTotalLimit,
    decimal DiscountAmount);

public sealed record OrderPricingResult(
    decimal Subtotal,
    decimal DiscountTotal,
    IReadOnlyList<OrderDiscount> Discounts,
    IReadOnlyList<AppliedCoupon> Coupons,
    string SnapshotHash);

public sealed class OrderPricingService(
    OrderDbContext db,
    IEnumerable<IPromotionRuleEvaluator> ruleEvaluators,
    IEnumerable<IPromotionBenefitCalculator> benefitCalculators) : IOrderPricingService
{
    private readonly IReadOnlyDictionary<string, IPromotionRuleEvaluator> _rules =
        ruleEvaluators.ToDictionary(item => item.RuleType, StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, IPromotionBenefitCalculator> _benefits =
        benefitCalculators.ToDictionary(item => item.BenefitType, StringComparer.OrdinalIgnoreCase);

    public async Task<OrderPricingResult> CalculateAsync(
        string userId,
        IReadOnlyList<PricingItem> items,
        IReadOnlyList<string> couponCodes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            throw new ArgumentException("Pricing requires at least one item.", nameof(items));

        var subtotal = Round(items.Sum(item => item.Product.Price * item.Quantity));
        if (subtotal <= 0)
            throw new InvalidOperationException("A payable order must have a positive subtotal.");

        var normalizedCodes = couponCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(CouponCode.Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedCodes.Length != couponCodes.Count)
            throw new InvalidOperationException("Coupon codes must be non-empty and unique.");

        var digests = normalizedCodes.Select(CouponCode.Digest).ToArray();
        var coupons = digests.Length == 0
            ? []
            : await db.CouponProjections
                .AsNoTracking()
                .Where(coupon => digests.Contains(coupon.CodeDigest))
                .ToArrayAsync(cancellationToken);
        if (coupons.Length != digests.Length)
            throw new InvalidOperationException("One or more coupon codes are unknown.");
        if (coupons.Any(coupon => !coupon.IsAvailableFor(userId, now)))
            throw new InvalidOperationException("One or more coupons are not active for this buyer.");
        if (coupons.Select(coupon => coupon.PromotionId).Distinct().Count() != coupons.Length)
            throw new InvalidOperationException("Only one coupon from the same promotion can be applied to an order.");

        var couponByPromotion = coupons.ToDictionary(coupon => coupon.PromotionId);
        var couponPromotionIds = couponByPromotion.Keys.ToArray();
        var promotions = await db.PromotionProjections
            .AsNoTracking()
            .Where(promotion =>
                promotion.StartsOnUtc <= now
                && promotion.EndsOnUtc > now
                && promotion.Status == "Active"
                && (promotion.TriggerType == "Automatic" || couponPromotionIds.Contains(promotion.Id)))
            .OrderByDescending(promotion => promotion.Priority)
            .ThenBy(promotion => promotion.Id)
            .ToArrayAsync(cancellationToken);

        var loadedCouponPromotionIds = promotions
            .Where(promotion => string.Equals(promotion.TriggerType, "Coupon", StringComparison.OrdinalIgnoreCase))
            .Select(promotion => promotion.Id)
            .ToHashSet();
        if (couponPromotionIds.Any(id => !loadedCouponPromotionIds.Contains(id)))
            throw new InvalidOperationException("A coupon's promotion is not currently active.");

        var context = new PromotionEvaluationContext(items, subtotal, subtotal);
        var discounts = new List<OrderDiscount>();
        var appliedCoupons = new List<AppliedCoupon>();
        var claimedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remaining = subtotal;

        foreach (var promotion in promotions)
        {
            var promotionContext = ApplyTargets(promotion.TargetsJson, context);
            if (promotionContext is null)
            {
                EnsureRequestedCouponCanApply(promotion, couponByPromotion, "Coupon target does not match this order.");
                continue;
            }

            if (!RulesMatch(promotion.RulesJson, promotionContext))
            {
                EnsureRequestedCouponCanApply(promotion, couponByPromotion, "Coupon conditions are not satisfied.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(promotion.ExclusiveGroup)
                && claimedGroups.Contains(promotion.ExclusiveGroup))
            {
                EnsureRequestedCouponCanApply(promotion, couponByPromotion, "Another promotion in the same exclusive group already applies.");
                continue;
            }

            var calculated = CalculateBenefits(promotion.BenefitsJson, promotionContext, remaining);
            var amount = Round(Math.Min(calculated, Math.Max(0, remaining - 0.01m)));
            if (amount <= 0)
            {
                EnsureRequestedCouponCanApply(promotion, couponByPromotion, "Coupon has no payable discount for this order.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(promotion.ExclusiveGroup))
                claimedGroups.Add(promotion.ExclusiveGroup);

            couponByPromotion.TryGetValue(promotion.Id, out var coupon);
            var snapshot = JsonSerializer.Serialize(new
            {
                promotion.Id,
                promotion.Code,
                promotion.SourceRevision,
                promotion.DefinitionChecksum,
                promotion.RulesJson,
                promotion.BenefitsJson,
                promotion.TargetsJson,
                CouponId = coupon?.Id
            });
            discounts.Add(new OrderDiscount(
                Guid.NewGuid(),
                promotion.Id,
                promotion.SourceRevision,
                coupon?.Id,
                coupon is null ? "AutomaticPromotion" : "Coupon",
                promotion.Name,
                amount,
                snapshot));
            remaining = Round(remaining - amount);

            if (coupon is not null)
            {
                appliedCoupons.Add(new AppliedCoupon(
                    coupon.Id,
                    coupon.MaskedCode,
                    coupon.PerUserLimit,
                    coupon.TotalLimit.HasValue,
                    amount));
            }

            if (string.Equals(promotion.StackPolicy, "Exclusive", StringComparison.OrdinalIgnoreCase))
                break;
        }

        if (appliedCoupons.Count != coupons.Length)
            throw new InvalidOperationException("Not every requested coupon could be applied.");

        var discountTotal = Round(discounts.Sum(discount => discount.Amount));
        var snapshotHash = PricingSnapshotHash.Create(items, discounts, subtotal, discountTotal);
        return new OrderPricingResult(subtotal, discountTotal, discounts, appliedCoupons, snapshotHash);
    }

    private bool RulesMatch(string rulesJson, PromotionEvaluationContext context)
    {
        var rules = JsonSerializer.Deserialize<IReadOnlyList<PromotionRuleContract>>(rulesJson) ?? [];
        foreach (var rule in rules)
        {
            if (!_rules.TryGetValue(rule.RuleType, out var evaluator)
                || evaluator.SchemaVersion != rule.SchemaVersion
                || !evaluator.IsSatisfied(rule.ConfigurationJson, context))
            {
                return false;
            }
        }
        return true;
    }

    private decimal CalculateBenefits(
        string benefitsJson,
        PromotionEvaluationContext context,
        decimal remaining)
    {
        var benefits = JsonSerializer.Deserialize<IReadOnlyList<PromotionBenefitContract>>(benefitsJson) ?? [];
        var discount = 0m;
        foreach (var benefit in benefits)
        {
            if (!_benefits.TryGetValue(benefit.BenefitType, out var calculator)
                || calculator.SchemaVersion != benefit.SchemaVersion)
            {
                throw new InvalidOperationException($"Unsupported promotion benefit '{benefit.BenefitType}' v{benefit.SchemaVersion}.");
            }

            var benefitAmount = calculator.Calculate(benefit.ConfigurationJson, context, remaining - discount);
            discount = Round(discount + Math.Max(0, benefitAmount));
        }
        return discount;
    }

    private static PromotionEvaluationContext? ApplyTargets(
        string targetsJson,
        PromotionEvaluationContext context)
    {
        var targets = JsonSerializer.Deserialize<IReadOnlyList<PromotionTargetContract>>(targetsJson) ?? [];
        if (targets.Count == 0)
            return context;

        var included = targets.Where(target => !target.IsExcluded).ToArray();
        var excluded = targets.Where(target => target.IsExcluded).ToArray();
        var eligible = context.Items
            .Where(item =>
                (included.Length == 0 || included.Any(target => TargetMatches(target, item)))
                && !excluded.Any(target => TargetMatches(target, item)))
            .ToArray();
        if (eligible.Length == 0)
            return null;
        return new PromotionEvaluationContext(
            eligible,
            context.Subtotal,
            eligible.Sum(item => item.Product.Price * item.Quantity));
    }

    private static bool TargetMatches(PromotionTargetContract target, PricingItem item) =>
        target.TargetType.ToUpperInvariant() switch
        {
            "ALL" => true,
            "SKU" => string.Equals(item.Product.Sku, target.TargetKey, StringComparison.OrdinalIgnoreCase),
            "PRODUCT" => Guid.TryParse(target.TargetKey, out var productId)
                && item.Product.ProductId == productId,
            "CATEGORY" => Guid.TryParse(target.TargetKey, out var categoryId)
                && item.Product.CategoryId == categoryId,
            _ => false
        };

    private static void EnsureRequestedCouponCanApply(
        PromotionProjection promotion,
        IReadOnlyDictionary<Guid, CouponProjection> coupons,
        string reason)
    {
        if (coupons.ContainsKey(promotion.Id))
            throw new InvalidOperationException(reason);
    }

    private static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed record PromotionEvaluationContext(
    IReadOnlyList<PricingItem> Items,
    decimal Subtotal,
    decimal EligibleSubtotal)
{
    public int TotalQuantity => Items.Sum(item => item.Quantity);
}

public interface IPromotionRuleEvaluator
{
    string RuleType { get; }
    int SchemaVersion { get; }
    bool IsSatisfied(string configurationJson, PromotionEvaluationContext context);
}

public interface IPromotionBenefitCalculator
{
    string BenefitType { get; }
    int SchemaVersion { get; }
    decimal Calculate(string configurationJson, PromotionEvaluationContext context, decimal remainingAmount);
}

public sealed class MinimumOrderAmountRule : IPromotionRuleEvaluator
{
    public string RuleType => "MinimumOrderAmount";
    public int SchemaVersion => 1;

    public bool IsSatisfied(string configurationJson, PromotionEvaluationContext context) =>
        context.Subtotal >= PromotionJson.RequiredDecimal(configurationJson, "amount");
}

public sealed class MinimumItemQuantityRule : IPromotionRuleEvaluator
{
    public string RuleType => "MinimumItemQuantity";
    public int SchemaVersion => 1;

    public bool IsSatisfied(string configurationJson, PromotionEvaluationContext context) =>
        context.TotalQuantity >= PromotionJson.RequiredInt32(configurationJson, "quantity");
}

public sealed class PercentageOffBenefit : IPromotionBenefitCalculator
{
    public string BenefitType => "PercentageOff";
    public int SchemaVersion => 1;

    public decimal Calculate(string configurationJson, PromotionEvaluationContext context, decimal remainingAmount)
    {
        var percentage = PromotionJson.RequiredDecimal(configurationJson, "percentage");
        if (percentage is <= 0 or > 100)
            throw new InvalidOperationException("PercentageOff.percentage must be in (0, 100].");
        var result = Math.Min(remainingAmount, context.EligibleSubtotal) * percentage / 100m;
        var maximum = PromotionJson.OptionalDecimal(configurationJson, "maximumDiscount");
        return maximum.HasValue ? Math.Min(result, maximum.Value) : result;
    }
}

public sealed class FixedAmountOffBenefit : IPromotionBenefitCalculator
{
    public string BenefitType => "FixedAmountOff";
    public int SchemaVersion => 1;

    public decimal Calculate(string configurationJson, PromotionEvaluationContext context, decimal remainingAmount) =>
        Math.Min(
            Math.Min(remainingAmount, context.EligibleSubtotal),
            PromotionJson.RequiredDecimal(configurationJson, "amount"));
}

public static class CouponCode
{
    public static string Normalize(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return code.Trim().ToUpperInvariant();
    }

    public static string Digest(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code))));
}

public static class OrderRequestFingerprint
{
    public static string Create(
        string userId,
        string paymentMethod,
        Guid? campaignId,
        IEnumerable<(string Sku, int Quantity)> items,
        IEnumerable<string> couponCodes)
    {
        var canonicalItems = string.Join(",", items
            .Select(item => (Sku: item.Sku.Trim().ToUpperInvariant(), item.Quantity))
            .OrderBy(item => item.Sku, StringComparer.Ordinal)
            .Select(item => $"{item.Sku}:{item.Quantity.ToString(CultureInfo.InvariantCulture)}"));
        var canonicalCoupons = string.Join(",", couponCodes
            .Select(CouponCode.Digest)
            .Order(StringComparer.Ordinal));
        var canonical = string.Join('|',
            userId.Trim(),
            paymentMethod.Trim().ToUpperInvariant(),
            campaignId?.ToString("N") ?? "regular",
            canonicalItems,
            canonicalCoupons);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

internal static class PricingSnapshotHash
{
    public static string Create(
        IEnumerable<PricingItem> items,
        IEnumerable<OrderDiscount> discounts,
        decimal subtotal,
        decimal discountTotal)
    {
        var canonicalItems = string.Join(';', items
            .OrderBy(item => item.Product.Sku, StringComparer.Ordinal)
            .Select(item => string.Join(':',
                item.Product.ProductId.ToString("N"),
                item.Product.SourceRevision.ToString(CultureInfo.InvariantCulture),
                item.Product.Price.ToString("0.00", CultureInfo.InvariantCulture),
                item.Quantity.ToString(CultureInfo.InvariantCulture))));
        var canonicalDiscounts = string.Join(';', discounts
            .OrderBy(discount => discount.PromotionId)
            .Select(discount => string.Join(':',
                discount.PromotionId.ToString("N"),
                discount.PromotionRevision.ToString(CultureInfo.InvariantCulture),
                discount.CouponId?.ToString("N") ?? string.Empty,
                discount.Amount.ToString("0.00", CultureInfo.InvariantCulture))));
        var canonical = string.Join('|',
            canonicalItems,
            canonicalDiscounts,
            subtotal.ToString("0.00", CultureInfo.InvariantCulture),
            discountTotal.ToString("0.00", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

internal static class PromotionJson
{
    public static decimal RequiredDecimal(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(property, out var value)
            || !value.TryGetDecimal(out var result)
            || result <= 0)
        {
            throw new InvalidOperationException($"Promotion configuration requires a positive '{property}'.");
        }
        return result;
    }

    public static decimal? OptionalDecimal(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(property, out var value)
            && value.TryGetDecimal(out var result)
            ? result
            : null;
    }

    public static int RequiredInt32(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(property, out var value)
            || !value.TryGetInt32(out var result)
            || result <= 0)
        {
            throw new InvalidOperationException($"Promotion configuration requires a positive '{property}'.");
        }
        return result;
    }
}
