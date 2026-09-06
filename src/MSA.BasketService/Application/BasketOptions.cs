using MSA.BasketService.Domain;

namespace MSA.BasketService.Application;

public sealed class BasketOptions
{
    public const string SectionName = "Basket";
    public int MaximumLineQuantity { get; set; } = 99;
    public int MaximumDistinctItems { get; set; } = 100;
    public int MaximumCouponCodes { get; set; } = BasketLimits.MaximumSupportedCouponCodes;
}
