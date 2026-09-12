using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSA.UserService.Application;
using MSA.UserService.Application.Points;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MSA.UserService.Controllers;

[Authorize]
[ApiController]
public sealed class LoyaltyPointsController(ISender sender) : ControllerBase
{
    [HttpGet("user/me/points")]
    [ProducesResponseType<LoyaltyPointAccountResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LoyaltyPointAccountResponse>> GetAccount(CancellationToken cancellationToken)
    {
        var response = await sender.Send(new GetLoyaltyPointAccountQuery(CurrentUserId()), cancellationToken);
        return Ok(response);
    }

    [HttpGet("user/me/points/entries")]
    [ProducesResponseType<IReadOnlyList<LoyaltyPointEntryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LoyaltyPointEntryResponse>>> ListEntries(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var response = await sender.Send(
            new ListLoyaltyPointEntriesQuery(CurrentUserId(), Math.Max(skip, 0), Math.Clamp(take, 1, 100)),
            cancellationToken);
        return Ok(response);
    }

    [Authorize(Roles = "admin")]
    [HttpGet("user/admin/points/{userId}")]
    [ProducesResponseType<LoyaltyPointAccountResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LoyaltyPointAccountResponse>> GetUserAccount(
        string userId,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(
            new GetLoyaltyPointAccountQuery(userId),
            cancellationToken);
        return Ok(response);
    }

    [Authorize(Roles = "admin")]
    [HttpGet("user/admin/points/{userId}/entries")]
    [ProducesResponseType<IReadOnlyList<LoyaltyPointEntryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LoyaltyPointEntryResponse>>> ListUserEntries(
        string userId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var response = await sender.Send(
            new ListLoyaltyPointEntriesQuery(userId, Math.Max(skip, 0), Math.Clamp(take, 1, 100)),
            cancellationToken);
        return Ok(response);
    }

    [Authorize(Roles = "admin")]
    [HttpGet("user/admin/points/{userId}/entries/{entryId:guid}")]
    [ProducesResponseType<LoyaltyPointEntryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoyaltyPointEntryResponse>> GetEntry(
        string userId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(
            new GetLoyaltyPointEntryQuery(userId, entryId),
            cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    [Authorize(Roles = "admin")]
    [HttpPost("user/admin/points/{userId}/adjustments")]
    [ProducesResponseType<LoyaltyPointEntryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<LoyaltyPointEntryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LoyaltyPointEntryResponse>> Adjust(
        string userId,
        AdjustLoyaltyPointsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new AdjustLoyaltyPointsCommand(
                userId,
                request.Delta,
                request.BusinessKey,
                request.Source,
                request.Reason,
                CurrentUserId()),
            cancellationToken);
        if (result.IsReplay)
            return Ok(result.Entry);
        return Created($"/api/user/admin/points/{userId}/entries/{result.Entry.Id}", result.Entry);
    }

    private string CurrentUserId() =>
        User.FindFirstValue("sub")
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("The authenticated subject is unavailable.");
}

/// <summary>Payload for an administrator to grant or deduct loyalty points.</summary>
public sealed record AdjustLoyaltyPointsRequest
{
    [Range(-1_000_000_000L, 1_000_000_000L)] public long Delta { get; init; }
    [Required, StringLength(128)] public required string BusinessKey { get; init; }
    [Required, StringLength(64)] public required string Source { get; init; }
    [Required, StringLength(500)] public required string Reason { get; init; }
}
