using Grpc.Core;
using MediatR;
using MSA.Grpc.Contracts.User;
using MSA.UserService.Application.Addresses;

namespace MSA.UserService.Grpc;

/// <summary>
/// 传输层适配：收货地址的查询复用面向用户的那条查询，归属判定因此只有一份实现 ——
/// 查不到与不属于该用户对调用方是同一个结论（found = false）。
/// </summary>
internal sealed class UserAddressGrpcService(ISender sender) : UserAddressQuery.UserAddressQueryBase
{
    public override async Task<GetAddressResponse> GetAddress(
        GetAddressRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || !Guid.TryParse(request.AddressId, out var addressId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A user id and a valid address id are required."));

        var address = await sender.Send(
            new GetUserAddressQuery(request.UserId, addressId),
            context.CancellationToken);
        if (address is null)
            return new GetAddressResponse { Found = false };

        return new GetAddressResponse
        {
            Found = true,
            RecipientName = address.RecipientName,
            PhoneNumber = address.PhoneNumber,
            CountryCode = address.CountryCode,
            Province = address.Province,
            City = address.City,
            District = address.District,
            Street = address.Street,
            PostalCode = address.PostalCode ?? string.Empty
        };
    }
}
