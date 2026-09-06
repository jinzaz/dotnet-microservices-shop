using MSA.BasketService.Domain;

namespace MSA.BasketService.Application.Abstractions;

public interface IBasketRepository
{
    Task<Basket?> FindByUserAsync(string userId, CancellationToken cancellationToken);
    void Add(Basket basket);
}

public interface IBasketProductCatalog
{
    Task<ProductSelection?> FindBySkuAsync(string sku, CancellationToken cancellationToken);
}

public interface IBasketReadService
{
    Task<BasketDto> GetAsync(string userId, CancellationToken cancellationToken);
}

public interface IBasketMutationLock
{
    Task AcquireAsync(string userId, CancellationToken cancellationToken);
}
