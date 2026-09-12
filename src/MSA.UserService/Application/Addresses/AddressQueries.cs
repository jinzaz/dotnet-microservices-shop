using MediatR;
using MSA.Infrastrucute.Mediator;

namespace MSA.UserService.Application.Addresses;

public sealed record ListUserAddressesQuery(string UserId) : IQuery<IReadOnlyList<UserAddressResponse>>;

public sealed record GetUserAddressQuery(string UserId, Guid AddressId) : IQuery<UserAddressResponse?>;

internal sealed class ListUserAddressesQueryHandler(IUserReadStore readStore)
    : IRequestHandler<ListUserAddressesQuery, IReadOnlyList<UserAddressResponse>>
{
    public Task<IReadOnlyList<UserAddressResponse>> Handle(
        ListUserAddressesQuery request,
        CancellationToken cancellationToken) =>
        readStore.ListAddressesAsync(request.UserId, cancellationToken);
}

internal sealed class GetUserAddressQueryHandler(IUserReadStore readStore)
    : IRequestHandler<GetUserAddressQuery, UserAddressResponse?>
{
    public Task<UserAddressResponse?> Handle(
        GetUserAddressQuery request,
        CancellationToken cancellationToken) =>
        readStore.GetAddressAsync(request.UserId, request.AddressId, cancellationToken);
}
