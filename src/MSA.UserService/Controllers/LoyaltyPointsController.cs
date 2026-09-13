using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSA.UserService.Application;
using MSA.UserService.Application.Points;
using System.Security.Claims;

namespace MSA.UserService.Controllers;

[Authorize(Policy = "user-api")]
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

    private string CurrentUserId() =>
        User.FindFirstValue("sub")
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("The authenticated subject is unavailable.");
}
