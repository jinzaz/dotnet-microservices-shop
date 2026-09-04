using MSA.Infrastrucute.Entities;
namespace MSA.CatalogService.Domain;
public sealed class Category : AggregateRoot<Guid>
{
    private Category() { }
    public Category(Guid id, string name) { Id = id; Rename(name); }
    public string Name { get; private set; } = string.Empty;
    public void Rename(string name) { ArgumentException.ThrowIfNullOrWhiteSpace(name); Name = name.Trim(); }
}
public sealed class Product : AggregateRoot<Guid>
{
    private Product() { }
    public Product(Guid id, Guid categoryId, string sku, string name, string description, decimal price, string currency)
    { if (price < 0) throw new ArgumentOutOfRangeException(nameof(price)); Id = id; CategoryId = categoryId; Sku = Required(sku); Name = Required(name); Description = description.Trim(); Price = price; Currency = currency.ToUpperInvariant(); IsActive = true; UpdatedOnUtc = DateTimeOffset.UtcNow; }
    public Guid CategoryId { get; private set; } public string Sku { get; private set; } = string.Empty; public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty; public decimal Price { get; private set; } public string Currency { get; private set; } = "CNY"; public bool IsActive { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; } public long Version { get; private set; }
    public void Update(string name, string description, decimal price, bool active) { if (price < 0) throw new ArgumentOutOfRangeException(nameof(price)); Name = Required(name); Description = description.Trim(); Price = price; IsActive = active; UpdatedOnUtc = DateTimeOffset.UtcNow; Version++; }
    private static string Required(string value) { ArgumentException.ThrowIfNullOrWhiteSpace(value); return value.Trim(); }
}
