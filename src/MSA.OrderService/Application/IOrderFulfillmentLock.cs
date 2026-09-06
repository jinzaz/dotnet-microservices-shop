namespace MSA.OrderService.Application;

public interface IOrderFulfillmentLock
{
    Task AcquireAsync(Guid orderId, CancellationToken cancellationToken);
}
