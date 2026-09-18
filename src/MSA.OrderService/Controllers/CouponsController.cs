using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSA.OrderService.Application.Coupons;
using System.Security.Claims;

namespace MSA.OrderService.Controllers;

[ApiController]
[Authorize(Policy = "order-api")]
[Route("order/coupons")]
public sealed class CouponsController(ISender sender) : ControllerBase
{
    /// <summary>Coupons issued to the buyer plus public coupons they have redeemed, with usage and status.</summary>
    [HttpGet("mine")]
    public Task<IReadOnlyList<WalletCouponDto>> Mine(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue("sub") ?? throw new UnauthorizedAccessException();
        return sender.Send(new GetMyCouponsQuery(userId), cancellationToken);
    }
}

[ApiController]
[Authorize(Policy = "order.admin.read")]
[Route("order/admin/coupons")]
public sealed class AdminCouponsController(ISender sender) : ControllerBase
{
    /// <summary>Reservation, consumption and remaining quota for coupons shown in the back office.</summary>
    [HttpGet("usage")]
    public async Task<ActionResult<IReadOnlyList<CouponUsageDto>>> Usage(
        [FromQuery] Guid[] couponIds,
        CancellationToken cancellationToken)
    {
        if (couponIds.Length is 0 or > 200)
            return BadRequest("Provide between 1 and 200 coupon ids.");
        return Ok(await sender.Send(new GetCouponUsageQuery(couponIds), cancellationToken));
    }
}
