using MediatR;
using MSA.UserService.Domain;
using MSA.Infrastrucute.Mediator;

namespace MSA.UserService.Application.Addresses;

public sealed record CreateUserAddressCommand(
    string UserId,
    AddressDetails Details,
    bool MakeDefault) : ICommand<UserAddressResponse>;

public sealed record UpdateUserAddressCommand(
    string UserId,
    Guid AddressId,
    AddressDetails Details) : ICommand<UserAddressResponse>;

public sealed record SetDefaultUserAddressCommand(
    string UserId,
    Guid AddressId) : ICommand<UserAddressResponse>;

public sealed record DeleteUserAddressCommand(
    string UserId,
    Guid AddressId) : ICommand<Unit>;

internal sealed class CreateUserAddressCommandHandler(
    IUserAddressRepository addresses,
    IUserBusinessLock userLock,
    TimeProvider clock) : IRequestHandler<CreateUserAddressCommand, UserAddressResponse>
{
    public async Task<UserAddressResponse> Handle(
        CreateUserAddressCommand request,
        CancellationToken cancellationToken)
    {
        await userLock.AcquireAsync(request.UserId, "addresses", cancellationToken);
        var activeCount = await addresses.CountActiveAsync(request.UserId, cancellationToken);
        if (activeCount >= UserAddress.MaximumActiveAddressesPerUser)
            throw new UserBusinessException($"A user can have at most {UserAddress.MaximumActiveAddressesPerUser} active addresses.");

        var now = clock.GetUtcNow();
        var makeDefault = request.MakeDefault || activeCount == 0;
        if (makeDefault)
            await addresses.ClearDefaultAsync(request.UserId, now, cancellationToken);

        var address = UserAddress.Create(Guid.NewGuid(), request.UserId, request.Details, makeDefault, now);
        addresses.Add(address);
        return UserResponseMapper.ToResponse(address);
    }
}

internal sealed class UpdateUserAddressCommandHandler(
    IUserAddressRepository addresses,
    IUserBusinessLock userLock,
    TimeProvider clock) : IRequestHandler<UpdateUserAddressCommand, UserAddressResponse>
{
    public async Task<UserAddressResponse> Handle(
        UpdateUserAddressCommand request,
        CancellationToken cancellationToken)
    {
        await userLock.AcquireAsync(request.UserId, "addresses", cancellationToken);
        var address = await addresses.GetAsync(request.UserId, request.AddressId, cancellationToken)
            ?? throw new UserResourceNotFoundException("The address does not exist.");
        address.Update(request.Details, clock.GetUtcNow());
        return UserResponseMapper.ToResponse(address);
    }
}

internal sealed class SetDefaultUserAddressCommandHandler(
    IUserAddressRepository addresses,
    IUserBusinessLock userLock,
    TimeProvider clock) : IRequestHandler<SetDefaultUserAddressCommand, UserAddressResponse>
{
    public async Task<UserAddressResponse> Handle(
        SetDefaultUserAddressCommand request,
        CancellationToken cancellationToken)
    {
        await userLock.AcquireAsync(request.UserId, "addresses", cancellationToken);
        var now = clock.GetUtcNow();
        await addresses.ClearDefaultAsync(request.UserId, now, cancellationToken);
        var address = await addresses.GetAsync(request.UserId, request.AddressId, cancellationToken)
            ?? throw new UserResourceNotFoundException("The address does not exist.");
        address.MakeDefault(now);
        return UserResponseMapper.ToResponse(address);
    }
}

internal sealed class DeleteUserAddressCommandHandler(
    IUserAddressRepository addresses,
    IUserBusinessLock userLock,
    TimeProvider clock) : IRequestHandler<DeleteUserAddressCommand, Unit>
{
    public async Task<Unit> Handle(
        DeleteUserAddressCommand request,
        CancellationToken cancellationToken)
    {
        await userLock.AcquireAsync(request.UserId, "addresses", cancellationToken);
        var address = await addresses.GetAsync(request.UserId, request.AddressId, cancellationToken)
            ?? throw new UserResourceNotFoundException("The address does not exist.");

        var now = clock.GetUtcNow();
        address.Remove(now);
        return Unit.Value;
    }
}
