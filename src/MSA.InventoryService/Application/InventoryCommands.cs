using FluentValidation; using MediatR; using Microsoft.EntityFrameworkCore; using Microsoft.Extensions.Caching.Distributed; using MSA.Infrastrucute.Mediator; using MSA.InventoryService.Domain; using MSA.InventoryService.Infrastructure; using System.Text.Json;
namespace MSA.InventoryService.Application;
public sealed record AddStockCommand(string Sku,int Quantity):ICommand<Guid>;
public sealed class AddStockValidator:AbstractValidator<AddStockCommand>{public AddStockValidator(){RuleFor(x=>x.Sku).NotEmpty().MaximumLength(80);RuleFor(x=>x.Quantity).GreaterThan(0);}}
internal sealed class AddStockHandler(InventoryDbContext db,IDistributedCache cache):IRequestHandler<AddStockCommand,Guid>{public async Task<Guid> Handle(AddStockCommand r,CancellationToken ct){var item=await db.StockItems.SingleOrDefaultAsync(x=>x.Sku==r.Sku,ct);if(item is null){item=new StockItem(Guid.NewGuid(),r.Sku,r.Quantity);db.Add(item);}else item.Increase(r.Quantity);await cache.RemoveAsync($"inventory:availability:{r.Sku}",ct);return item.Id;}}
public sealed record GetInventoryAvailabilityQuery(string Sku):IQuery<InventoryAvailabilityDto?>;
public sealed record InventoryAvailabilityDto(string Sku,int OnHand,int Reserved,int Available,long Version);
internal sealed class GetInventoryAvailabilityHandler(InventoryDbContext db,IDistributedCache cache):IRequestHandler<GetInventoryAvailabilityQuery,InventoryAvailabilityDto?>
{
 public async Task<InventoryAvailabilityDto?> Handle(GetInventoryAvailabilityQuery r,CancellationToken ct){var key=$"inventory:availability:{r.Sku}";var cached=await cache.GetStringAsync(key,ct);if(cached is not null)return JsonSerializer.Deserialize<InventoryAvailabilityDto>(cached);var value=await db.StockItems.AsNoTracking().Where(x=>x.Sku==r.Sku).Select(x=>new InventoryAvailabilityDto(x.Sku,x.OnHand,x.Reserved,x.OnHand-x.Reserved,x.Version)).SingleOrDefaultAsync(ct);if(value is not null)await cache.SetStringAsync(key,JsonSerializer.Serialize(value),new DistributedCacheEntryOptions{AbsoluteExpirationRelativeToNow=TimeSpan.FromSeconds(15)},ct);return value;}
}
