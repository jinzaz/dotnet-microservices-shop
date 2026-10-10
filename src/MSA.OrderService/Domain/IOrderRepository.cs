namespace MSA.OrderService.Domain;

public interface IOrderRepository
{
    void Add(Order order);
    Task<Order?> GetWithFulfillmentHistoryAsync(Guid orderId, CancellationToken cancellationToken);
    Task<bool> HasOpenAfterSalesAsync(Guid orderId, CancellationToken cancellationToken);
}
