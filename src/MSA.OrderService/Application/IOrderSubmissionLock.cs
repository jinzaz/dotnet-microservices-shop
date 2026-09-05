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
}
