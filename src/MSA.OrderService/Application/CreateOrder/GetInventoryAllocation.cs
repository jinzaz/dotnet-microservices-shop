using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Infrastrucute.Mediator;
using MSA.OrderService.Infrastructure;

namespace MSA.OrderService.Application.CreateOrder;

public sealed record InventoryAllocationSelection(string Sku, Guid OfferId);

public sealed record GetInventoryAllocationQuery(Guid AllocationId)
    : IQuery<InventoryAllocationSelection?>;

internal sealed class GetInventoryAllocationQueryHandler(OrderDbContext db)
    : IRequestHandler<GetInventoryAllocationQuery, InventoryAllocationSelection?>
{
    public Task<InventoryAllocationSelection?> Handle(
        GetInventoryAllocationQuery request,
        CancellationToken cancellationToken) =>
        db.InventoryAllocationProjections
            .AsNoTracking()
            .Where(value => value.AllocationId == request.AllocationId)
            .Select(value => new InventoryAllocationSelection(value.Sku, value.OfferId))
            .SingleOrDefaultAsync(cancellationToken);
}
