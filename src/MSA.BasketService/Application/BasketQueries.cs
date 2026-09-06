using MediatR;
using MSA.BasketService.Application.Abstractions;
using MSA.Infrastrucute.Mediator;

namespace MSA.BasketService.Application;

public sealed record GetBasketQuery(string UserId) : IQuery<BasketDto>;

internal sealed class GetBasketQueryHandler(IBasketReadService baskets)
    : IRequestHandler<GetBasketQuery, BasketDto>
{
    public Task<BasketDto> Handle(GetBasketQuery request, CancellationToken cancellationToken) =>
        baskets.GetAsync(request.UserId, cancellationToken);
}
