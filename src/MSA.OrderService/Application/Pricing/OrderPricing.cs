using MSA.Redis;
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
        CouponSelection coupons,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <summary>
/// Coupons requested for an order: secret codes typed by the buyer, plus coupons issued to the buyer's
/// wallet, which are referenced by id because their plaintext code is never stored.
/// </summary>
public sealed record CouponSelection(IReadOnlyList<string> Codes, IReadOnlyList<Guid> WalletCouponIds)
{
    public const int MaximumCoupons = 5;
    public static readonly CouponSelection None = new([], []);

    public int Count => Codes.Count + WalletCouponIds.Count;

    public static CouponSelection From(IReadOnlyList<string>? codes, IReadOnlyList<Guid>? walletCouponIds) =>
        new(codes ?? [], walletCouponIds ?? []);
}

public enum CouponRejectionReason
{
    Unknown,
    Duplicate,
    NotAvailable,
    SamePromotion,
    PromotionInactive,
    TargetMismatch,
    ConditionsNotMet,
    ExclusiveGroup,
    NoDiscount,
    NotApplied,
    UsageLimitReached,
    QuotaExhausted,
    Conflict
}

/// <summary>A requested coupon cannot be applied; still an <see cref="InvalidOperationException"/> so the API maps it to 409.</summary>
public sealed class CouponRejectedException(CouponRejectionReason reason, string message, Guid? couponId = null)
    : InvalidOperationException(message)
{
    public CouponRejectionReason Reason { get; } = reason;
    public Guid? CouponId { get; } = couponId;
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

/// <summary>An automatic promotion whose targets match but whose threshold is not reached yet.</summary>
public sealed record PromotionOpportunity(
    Guid PromotionId,
    string PromotionName,
    decimal? MissingAmount,
    int? MissingQuantity,
    string BenefitsJson);

public sealed record OrderPricingResult(
    decimal Subtotal,
    decimal DiscountTotal,
    IReadOnlyList<OrderDiscount> Discounts,
    IReadOnlyList<AppliedCoupon> Coupons,
    string SnapshotHash)
{
    public IReadOnlyList<PromotionOpportunity> Opportunities { get; init; } = [];

    /// <summary>每个计价行分摊到的优惠，与传入的 items 一一对应，合计等于 <see cref="DiscountTotal"/>。</summary>
    public IReadOnlyList<decimal> LineDiscounts { get; init; } = [];
}

public sealed class OrderPricingService(
    OrderDbContext db,
    IEnumerable<IPromotionRuleEvaluator> ruleEvaluators,
    IEnumerable<IPromotionBenefitCalculator> benefitCalculators,
    ICacheStore? cache = null) : IOrderPricingService
{
    /// <summary>
    /// 生效中的促销是所有报价与下单共享的一小份热数据（一次报价会按候选券逐个试算，每次都要读），
    /// 整份缓存，按时间与券在内存里筛。促销投影一落库，提交后失效。
    /// 这是成交依据，不开一级缓存：旧值可见的时间只有提交到失效之间的毫秒级。
    /// </summary>
    internal const string ActivePromotionsKey = "promotions:active";
    private static readonly CachePolicy ActivePromotionsPolicy = new(TimeSpan.FromMinutes(5));

    private readonly IReadOnlyDictionary<string, IPromotionRuleEvaluator> _rules =
        ruleEvaluators.ToDictionary(item => item.RuleType, StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, IPromotionBenefitCalculator> _benefits =
        benefitCalculators.ToDictionary(item => item.BenefitType, StringComparer.OrdinalIgnoreCase);

    public async Task<OrderPricingResult> CalculateAsync(
        string userId,
        IReadOnlyList<PricingItem> items,
        CouponSelection couponSelection,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            throw new ArgumentException("Pricing requires at least one item.", nameof(items));

        var subtotal = Round(items.Sum(item => item.Product.Price * item.Quantity));
        if (subtotal <= 0)
            throw new InvalidOperationException("A payable order must have a positive subtotal.");

        var coupons = await ResolveCouponsAsync(userId, couponSelection, cancellationToken);
        var unavailable = coupons.FirstOrDefault(coupon => !coupon.IsAvailableFor(userId, now));
        if (unavailable is not null)
            throw new CouponRejectedException(CouponRejectionReason.NotAvailable, "One or more coupons are not active for this buyer.", unavailable.Id);
        if (coupons.Select(coupon => coupon.PromotionId).Distinct().Count() != coupons.Length)
            throw new CouponRejectedException(CouponRejectionReason.SamePromotion, "Only one coupon from the same promotion can be applied to an order.");

        var couponByPromotion = coupons.ToDictionary(coupon => coupon.PromotionId);
        var couponPromotionIds = couponByPromotion.Keys.ToArray();
        var promotions = (await LoadActivePromotionsAsync(now, cancellationToken))
            .Where(promotion =>
                promotion.StartsOnUtc <= now
                && promotion.EndsOnUtc > now
                && (promotion.TriggerType == "Automatic" || couponPromotionIds.Contains(promotion.Id)))
            .OrderByDescending(promotion => promotion.Priority)
            .ThenBy(promotion => promotion.Id)
            .ToArray();

        var loadedCouponPromotionIds = promotions
            .Where(promotion => string.Equals(promotion.TriggerType, "Coupon", StringComparison.OrdinalIgnoreCase))
            .Select(promotion => promotion.Id)
            .ToHashSet();
        var inactive = coupons.FirstOrDefault(coupon => !loadedCouponPromotionIds.Contains(coupon.PromotionId));
        if (inactive is not null)
            throw new CouponRejectedException(CouponRejectionReason.PromotionInactive, "A coupon's promotion is not currently active.", inactive.Id);

        var discounts = new List<OrderDiscount>();
        var appliedCoupons = new List<AppliedCoupon>();
        var opportunities = new List<PromotionOpportunity>();
        var claimedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remaining = subtotal;
        // 每行优惠后还剩多少。门槛和权益都按适用行的剩余金额算，并把优惠分摊回这些行：
        // 同一件商品被多个活动命中时，后面的活动只能在前面减剩的金额上继续减，单行永远不会被减成负数。
        var lineRemaining = items.Select(item => Round(item.Product.Price * item.Quantity)).ToArray();
        var lineDiscounts = new decimal[items.Count];

        // 促销按优先级稳定计算；每步最多抵扣到剩余 0.01，确保订单始终保留正的应付金额。
        foreach (var promotion in promotions)
        {
            var eligibleLines = EligibleLines(promotion.TargetsJson, items);
            if (eligibleLines.Length == 0)
            {
                EnsureRequestedCouponCanApply(promotion, couponByPromotion, CouponRejectionReason.TargetMismatch, "Coupon target does not match this order.");
                continue;
            }

            var promotionContext = new PromotionEvaluationContext(
                eligibleLines.Select(index => items[index]).ToArray(),
                subtotal,
                Round(eligibleLines.Sum(index => lineRemaining[index])));

            if (!RulesMatch(promotion.RulesJson, promotionContext))
            {
                EnsureRequestedCouponCanApply(promotion, couponByPromotion, CouponRejectionReason.ConditionsNotMet, "Coupon conditions are not satisfied.");
                if (string.Equals(promotion.TriggerType, "Automatic", StringComparison.OrdinalIgnoreCase)
                    && Shortfall(promotion.RulesJson, promotionContext) is { } shortfall)
                {
                    opportunities.Add(new PromotionOpportunity(
                        promotion.Id, promotion.Name, shortfall.Amount, shortfall.Quantity, promotion.BenefitsJson));
                }
                continue;
            }

            if (!string.IsNullOrWhiteSpace(promotion.ExclusiveGroup)
                && claimedGroups.Contains(promotion.ExclusiveGroup))
            {
                EnsureRequestedCouponCanApply(promotion, couponByPromotion, CouponRejectionReason.ExclusiveGroup, "Another promotion in the same exclusive group already applies.");
                continue;
            }

            if (CalculateBenefits(promotion.BenefitsJson, promotionContext, remaining) is not { } calculated)
            {
                // 不认识的权益（例如目录先于订单发布了新类型）只让这一个活动不生效，不拖垮所有下单。
                EnsureRequestedCouponCanApply(promotion, couponByPromotion, CouponRejectionReason.NoDiscount, "Coupon benefit is not supported yet.");
                continue;
            }
            var amount = Round(Math.Min(calculated, Math.Max(0, remaining - 0.01m)));
            if (amount <= 0)
            {
                EnsureRequestedCouponCanApply(promotion, couponByPromotion, CouponRejectionReason.NoDiscount, "Coupon has no payable discount for this order.");
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
            Allocate(amount, eligibleLines, lineRemaining, lineDiscounts);
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

        var skipped = coupons.FirstOrDefault(coupon => appliedCoupons.All(applied => applied.CouponId != coupon.Id));
        if (skipped is not null)
            throw new CouponRejectedException(CouponRejectionReason.NotApplied, "Not every requested coupon could be applied.", skipped.Id);

        var discountTotal = Round(discounts.Sum(discount => discount.Amount));
        // 快照摘要随订单固化，支付服务据此校验金额来自同一版商品和优惠定义。
        var snapshotHash = PricingSnapshotHash.Create(items, discounts, subtotal, discountTotal);
        return new OrderPricingResult(subtotal, discountTotal, discounts, appliedCoupons, snapshotHash)
        {
            Opportunities = opportunities,
            LineDiscounts = lineDiscounts
        };
    }

    private async Task<PromotionProjection[]> LoadActivePromotionsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        Task<PromotionProjection[]> Load(CancellationToken token) => db.PromotionProjections
            .AsNoTracking()
            .Where(promotion => promotion.Status == "Active" && promotion.EndsOnUtc > now)
            .ToArrayAsync(token);

        return cache is null
            ? await Load(cancellationToken)
            : await cache.GetOrLoadAsync<PromotionProjection[]>(
                  ActivePromotionsKey, async token => await Load(token), ActivePromotionsPolicy, cancellationToken) ?? [];
    }

    private async Task<CouponProjection[]> ResolveCouponsAsync(
        string userId,
        CouponSelection selection,
        CancellationToken cancellationToken)
    {
        if (selection.Count == 0)
            return [];
        if (selection.Count > CouponSelection.MaximumCoupons)
            throw new CouponRejectedException(CouponRejectionReason.Conflict, $"At most {CouponSelection.MaximumCoupons} coupons can be applied.");

        var normalizedCodes = selection.Codes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(CouponCode.Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var walletIds = selection.WalletCouponIds.Distinct().ToArray();
        if (normalizedCodes.Length != selection.Codes.Count || walletIds.Length != selection.WalletCouponIds.Count)
            throw new CouponRejectedException(CouponRejectionReason.Duplicate, "Coupon codes must be non-empty and unique.");

        var digests = normalizedCodes.Select(CouponCode.Digest).ToArray();
        var byCode = digests.Length == 0
            ? []
            : await db.CouponProjections
                .AsNoTracking()
                .Where(coupon => digests.Contains(coupon.CodeDigest))
                .ToArrayAsync(cancellationToken);
        if (byCode.Length != digests.Length)
            throw new CouponRejectedException(CouponRejectionReason.Unknown, "One or more coupon codes are unknown.");

        // Wallet coupons are only reachable by the buyer they were issued to; a foreign id reads as unknown.
        var byWallet = walletIds.Length == 0
            ? []
            : await db.CouponProjections
                .AsNoTracking()
                .Where(coupon => walletIds.Contains(coupon.Id) && coupon.AssignedUserId == userId)
                .ToArrayAsync(cancellationToken);
        if (byWallet.Length != walletIds.Length)
            throw new CouponRejectedException(CouponRejectionReason.Unknown, "One or more wallet coupons do not belong to this buyer.");

        var all = byCode.Concat(byWallet).ToArray();
        if (all.Select(coupon => coupon.Id).Distinct().Count() != all.Length)
            throw new CouponRejectedException(CouponRejectionReason.Duplicate, "The same coupon was requested twice.");
        return all;
    }

    private static (decimal? Amount, int? Quantity)? Shortfall(string rulesJson, PromotionEvaluationContext context)
    {
        var rules = JsonSerializer.Deserialize<IReadOnlyList<PromotionRuleContract>>(rulesJson) ?? [];
        decimal? amount = null;
        int? quantity = null;
        foreach (var rule in rules)
        {
            if (rule.SchemaVersion == 1 && string.Equals(rule.RuleType, "MinimumOrderAmount", StringComparison.OrdinalIgnoreCase))
            {
                var missing = PromotionJson.RequiredDecimal(rule.ConfigurationJson, "amount") - context.EligibleSubtotal;
                if (missing > 0)
                    amount = Round(missing);
            }
            else if (rule.SchemaVersion == 1 && string.Equals(rule.RuleType, "MinimumItemQuantity", StringComparison.OrdinalIgnoreCase))
            {
                var missing = PromotionJson.RequiredInt32(rule.ConfigurationJson, "quantity") - context.TotalQuantity;
                if (missing > 0)
                    quantity = missing;
            }
            else
            {
                // Rules the storefront cannot express as "buy a little more" produce no hint.
                return null;
            }
        }
        return amount is null && quantity is null ? null : (amount, quantity);
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

    /// <summary>
    /// 一个活动内的多条权益依次在前一条减剩的金额上计算，合计不超过适用行的剩余金额。
    /// 含有本服务不认识的权益时返回 null，由调用方让整个活动不生效。
    /// </summary>
    private decimal? CalculateBenefits(
        string benefitsJson,
        PromotionEvaluationContext context,
        decimal remaining)
    {
        var benefits = JsonSerializer.Deserialize<IReadOnlyList<PromotionBenefitContract>>(benefitsJson) ?? [];
        var cap = Math.Min(remaining, context.EligibleSubtotal);
        var discount = 0m;
        foreach (var benefit in benefits)
        {
            if (!_benefits.TryGetValue(benefit.BenefitType, out var calculator)
                || calculator.SchemaVersion != benefit.SchemaVersion)
            {
                return null;
            }

            var benefitAmount = calculator.Calculate(benefit.ConfigurationJson, context, cap - discount);
            discount = Round(discount + Math.Clamp(benefitAmount, 0, cap - discount));
        }
        return discount;
    }

    /// <summary>活动适用的计价行下标；没有目标表示全部适用，排除项优先于包含项。</summary>
    private static int[] EligibleLines(string targetsJson, IReadOnlyList<PricingItem> items)
    {
        var targets = JsonSerializer.Deserialize<IReadOnlyList<PromotionTargetContract>>(targetsJson) ?? [];
        var included = targets.Where(target => !target.IsExcluded).ToArray();
        var excluded = targets.Where(target => target.IsExcluded).ToArray();
        return Enumerable.Range(0, items.Count)
            .Where(index =>
                (included.Length == 0 || included.Any(target => TargetMatches(target, items[index])))
                && !excluded.Any(target => TargetMatches(target, items[index])))
            .ToArray();
    }

    /// <summary>
    /// 把一笔优惠按适用行剩余金额的比例分摊回去：先向下取整到分，零头再依次补给仍有余额的行。
    /// 比例份额不会超过该行余额，适用行余额之和又不小于优惠金额，所以每一分都能分完且没有行变负数。
    /// </summary>
    private static void Allocate(decimal amount, int[] lines, decimal[] lineRemaining, decimal[] lineDiscounts)
    {
        var basis = lines.Sum(index => lineRemaining[index]);
        var shares = lines
            .Select(index => Math.Floor(amount * lineRemaining[index] / basis * 100m) / 100m)
            .ToArray();
        var leftover = amount - shares.Sum();
        for (var i = 0; i < lines.Length && leftover > 0; i++)
        {
            var extra = Math.Min(leftover, lineRemaining[lines[i]] - shares[i]);
            shares[i] += extra;
            leftover -= extra;
        }
        if (leftover != 0)
            throw new InvalidOperationException("Discount exceeds the remaining amount of its eligible lines.");

        for (var i = 0; i < lines.Length; i++)
        {
            lineRemaining[lines[i]] -= shares[i];
            lineDiscounts[lines[i]] += shares[i];
        }
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
        CouponRejectionReason reason,
        string message)
    {
        if (coupons.TryGetValue(promotion.Id, out var coupon))
            throw new CouponRejectedException(reason, message, coupon.Id);
    }

    private static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <param name="Items">活动适用的计价行。</param>
/// <param name="Subtotal">整单原价小计，仅供参考；门槛与权益都不应基于它计算。</param>
/// <param name="EligibleSubtotal">适用行扣除前序活动优惠后的剩余金额。</param>
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

    // 只看适用商品优惠后的金额：品类满减不能靠其他商品凑单，同一笔金额也不能被多个满减重复用作门槛。
    public bool IsSatisfied(string configurationJson, PromotionEvaluationContext context) =>
        context.EligibleSubtotal >= PromotionJson.RequiredDecimal(configurationJson, "amount");
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
        IEnumerable<(string Sku, int Quantity, Guid? OfferId)> items,
        IEnumerable<string> couponCodes,
        ShippingAddressSnapshot shippingAddress,
        Guid? sourceBasketId,
        long? sourceBasketVersion,
        IEnumerable<Guid>? walletCouponIds = null)
    {
        ArgumentNullException.ThrowIfNull(shippingAddress);
        if (sourceBasketId.HasValue != sourceBasketVersion.HasValue)
            throw new ArgumentException("Basket id and version must be provided together.");
        if (sourceBasketId == Guid.Empty || sourceBasketVersion is < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceBasketId), "Basket source values are invalid.");
        var canonicalItems = items
            .Select(item => (Sku: item.Sku.Trim().ToUpperInvariant(), item.Quantity, item.OfferId))
            .OrderBy(item => item.Sku, StringComparer.Ordinal)
            .Select(item => new
            {
                item.Sku,
                Quantity = item.Quantity.ToString(CultureInfo.InvariantCulture),
                OfferId = item.OfferId?.ToString("N")
            })
            .ToArray();
        var canonicalCoupons = couponCodes
            .Select(CouponCode.Digest)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var canonical = JsonSerializer.Serialize(new
        {
            UserId = userId.Trim(),
            PaymentMethod = paymentMethod.Trim().ToUpperInvariant(),
            Items = canonicalItems,
            Coupons = canonicalCoupons,
            Address = new
            {
                shippingAddress.SourceAddressId,
                shippingAddress.RecipientName,
                shippingAddress.PhoneNumber,
                shippingAddress.CountryCode,
                shippingAddress.Province,
                shippingAddress.City,
                shippingAddress.District,
                shippingAddress.StreetLine1,
                shippingAddress.StreetLine2,
                shippingAddress.PostalCode
            }
        });
        var canonicalWalletCoupons = (walletCouponIds ?? [])
            .Select(id => id.ToString("N"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        // Appended only when present so fingerprints of code-only requests match earlier releases.
        if (canonicalWalletCoupons.Length > 0)
            canonical = string.Join('|', canonical, "wallet", string.Join(',', canonicalWalletCoupons));
        if (sourceBasketId.HasValue)
        {
            canonical = string.Join(
                '|',
                canonical,
                "basket",
                sourceBasketId.Value.ToString("N"),
                sourceBasketVersion!.Value.ToString(CultureInfo.InvariantCulture));
        }
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
                item.Product.VariantId.ToString("N"),
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
