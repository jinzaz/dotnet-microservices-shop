namespace MSA.OrderService.Domain;

public interface IOrderRepository
{
    void Add(Order order);
}
