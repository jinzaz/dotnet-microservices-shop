using Microsoft.AspNetCore.Http.HttpResults;
using MSA.PaymentCore;
using MSA.PaymentService.Application;
using MSA.PaymentService.Domain;

namespace MSA.PaymentService.Endpoints;

internal static class PaymentAdminEndpoints
{
    internal static IEndpointRouteBuilder MapPaymentAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var read = endpoints.MapGroup("/payment/admin")
            .RequireAuthorization("payment.admin.read");

        read.MapGet("/orders/{orderId:guid}", async Task<Results<Ok<PaymentDto>, NotFound>> (
            Guid orderId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var payment = await sender.Send(
                new GetPaymentByOrderQuery(orderId, string.Empty, true), cancellationToken);
            return payment is null ? TypedResults.NotFound() : TypedResults.Ok(payment);
        })
        .WithName("GetAdminPaymentByOrder");

        read.MapGet("/payments", async (
            ISender sender,
            CancellationToken cancellationToken,
            PaymentStatus? status = null,
            PaymentChannel? channel = null,
            Guid? orderId = null,
            string? userId = null,
            int skip = 0,
            int take = 50) => TypedResults.Ok(await sender.Send(
                new ListPaymentsQuery(status, channel, orderId, userId, skip, take),
                cancellationToken)))
            .WithName("ListAdminPayments")
            .WithSummary("List payment transactions with admin filters")
            .Produces<PaymentPage>(StatusCodes.Status200OK);

        return endpoints;
    }
}
