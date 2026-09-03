using MSA.Infrastrucute.Entities;
namespace MSA.OrderService.Domain;
public sealed class Order : AggregateRoot<Guid>
{
    private Order() { }
    public Order(Guid id, string productId, int quantity, decimal amount, string currency)
    {
        if (id == Guid.Empty) throw new ArgumentException("Order id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(productId)) throw new ArgumentException("Product is required.", nameof(productId));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3) throw new ArgumentException("Currency must be an ISO 4217 code.", nameof(currency));
        Id = id; ProductId = productId.Trim(); Quantity = quantity; Amount = amount; Currency = currency.ToUpperInvariant(); CreatedOnUtc = DateTimeOffset.UtcNow;
    }
    public string ProductId { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset CreatedOnUtc { get; private set; }
}
