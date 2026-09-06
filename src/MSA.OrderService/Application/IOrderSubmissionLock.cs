namespace MSA.OrderService.Application;

public interface IOrderSubmissionLock
{
    Task AcquireAsync(IEnumerable<string> resourceKeys, CancellationToken cancellationToken);
}

public static class OrderSubmissionLockKeys
{
    public static string Idempotency(string userId, string idempotencyKey) =>
        $"order-idempotency:{userId}:{idempotencyKey}";

    public static string FlashSaleBuyer(Guid campaignId, string userId) =>
        $"flash-sale-buyer:{campaignId:N}:{userId}";

    public static string CouponUser(string codeDigest, string userId) =>
        $"coupon-user:{codeDigest}:{userId}";

    public static string BasketCheckout(
        string userId,
        Guid basketId,
        long basketVersion) =>
        $"basket-checkout:{userId}:{basketId:N}:{basketVersion}";
}
