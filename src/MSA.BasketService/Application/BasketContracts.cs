namespace MSA.BasketService.Application;

public sealed record BasketMutationResult(Guid BasketId, long Version);

public sealed record BasketMoneyTotalDto(string Currency, decimal Amount);

public sealed record BasketItemDto(
    Guid ProductId,
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    string Currency,
    decimal LineAmount,
    bool IsAvailable,
    long ProductRevision,
    decimal UnitPriceAtSelection,
    string CurrencyAtSelection,
    long ProductRevisionAtSelection,
    bool PriceChangedSinceSelection,
    DateTimeOffset AddedOnUtc,
    DateTimeOffset UpdatedOnUtc);

public sealed record BasketDto(
    Guid? BasketId,
    long Version,
    IReadOnlyList<BasketItemDto> Items,
    IReadOnlyList<string> CouponCodes,
    IReadOnlyList<BasketMoneyTotalDto> EstimatedTotals,
    bool HasUnavailableItems,
    bool HasPriceChanges,
    DateTimeOffset? UpdatedOnUtc)
{
    public const string PricingNoticeText = "Basket totals are estimates; Ordering validates products, coupons, and prices again when the order is submitted.";
    public string PricingNotice => PricingNoticeText;

    public static BasketDto Empty() =>
        new(null, 0, [], [], [], false, false, null);
}
