using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSA.UserService.Application;
using MSA.UserService.Application.Addresses;
using MSA.UserService.Domain;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MSA.UserService.Controllers;

[Authorize]
[ApiController]
[Route("api/user/me/addresses")]
public sealed class UserAddressesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UserAddressResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserAddressResponse>>> List(CancellationToken cancellationToken)
    {
        var addresses = await sender.Send(new ListUserAddressesQuery(CurrentUserId()), cancellationToken);
        return Ok(addresses);
    }

    [HttpGet("{addressId:guid}")]
    [ProducesResponseType<UserAddressResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserAddressResponse>> Get(Guid addressId, CancellationToken cancellationToken)
    {
        var address = await sender.Send(new GetUserAddressQuery(CurrentUserId(), addressId), cancellationToken);
        return address is null ? NotFound() : Ok(address);
    }

    [HttpPost]
    [ProducesResponseType<UserAddressResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserAddressResponse>> Create(
        CreateUserAddressRequest request,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(
            new CreateUserAddressCommand(CurrentUserId(), request.ToDetails(), request.MakeDefault),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { addressId = response.Id }, response);
    }

    [HttpPut("{addressId:guid}")]
    [ProducesResponseType<UserAddressResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserAddressResponse>> Update(
        Guid addressId,
        UpdateUserAddressRequest request,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(
            new UpdateUserAddressCommand(CurrentUserId(), addressId, request.ToDetails()),
            cancellationToken);
        return Ok(response);
    }

    [HttpPut("{addressId:guid}/default")]
    [ProducesResponseType<UserAddressResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserAddressResponse>> SetDefault(
        Guid addressId,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(
            new SetDefaultUserAddressCommand(CurrentUserId(), addressId),
            cancellationToken);
        return Ok(response);
    }

    [HttpDelete("{addressId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid addressId, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteUserAddressCommand(CurrentUserId(), addressId), cancellationToken);
        return NoContent();
    }

    private string CurrentUserId() =>
        User.FindFirstValue("sub")
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("The authenticated subject is unavailable.");
}

/// <summary>Payload for creating a delivery address.</summary>
public sealed record CreateUserAddressRequest
{
    [Required, StringLength(50)] public required string Label { get; init; }
    [Required, StringLength(100)] public required string RecipientName { get; init; }
    [Required, StringLength(32, MinimumLength = 6)] public required string PhoneNumber { get; init; }
    [Required, StringLength(2, MinimumLength = 2)] public required string CountryCode { get; init; }
    [Required, StringLength(100)] public required string Province { get; init; }
    [Required, StringLength(100)] public required string City { get; init; }
    [Required, StringLength(100)] public required string District { get; init; }
    [Required, StringLength(300)] public required string Street { get; init; }
    [StringLength(20)] public string? PostalCode { get; init; }
    public bool MakeDefault { get; init; }

    internal AddressDetails ToDetails() => new(
        Label,
        RecipientName,
        PhoneNumber,
        CountryCode,
        Province,
        City,
        District,
        Street,
        PostalCode);
}

/// <summary>Payload for replacing the contact and location fields of a delivery address.</summary>
public sealed record UpdateUserAddressRequest
{
    [Required, StringLength(50)] public required string Label { get; init; }
    [Required, StringLength(100)] public required string RecipientName { get; init; }
    [Required, StringLength(32, MinimumLength = 6)] public required string PhoneNumber { get; init; }
    [Required, StringLength(2, MinimumLength = 2)] public required string CountryCode { get; init; }
    [Required, StringLength(100)] public required string Province { get; init; }
    [Required, StringLength(100)] public required string City { get; init; }
    [Required, StringLength(100)] public required string District { get; init; }
    [Required, StringLength(300)] public required string Street { get; init; }
    [StringLength(20)] public string? PostalCode { get; init; }

    internal AddressDetails ToDetails() => new(
        Label,
        RecipientName,
        PhoneNumber,
        CountryCode,
        Province,
        City,
        District,
        Street,
        PostalCode);
}
