using Microsoft.AspNetCore.Http.HttpResults;
using MSA.PaymentService.Application;
using System.Security.Claims;

namespace MSA.PaymentService.Endpoints;

internal static class PaymentApiEndpoints
{
    internal static IEndpointRouteBuilder MapPaymentApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/payment/orders/{orderId:guid}", async Task<Results<Ok<PaymentDto>, NotFound>> (
            Guid orderId,
            ClaimsPrincipal user,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var subject = user.FindFirstValue("sub") ?? string.Empty;
            var payment = await sender.Send(
                new GetPaymentByOrderQuery(orderId, subject, false), cancellationToken);
            return payment is null ? TypedResults.NotFound() : TypedResults.Ok(payment);
        })
        .RequireAuthorization("payment-api")
        .WithName("GetPaymentByOrder");

        return endpoints;
    }
}
