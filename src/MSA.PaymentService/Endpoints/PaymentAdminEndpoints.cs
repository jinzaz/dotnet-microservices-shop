using Microsoft.AspNetCore.Http.HttpResults;
using MSA.PaymentCore;
using MSA.PaymentService.Application;
using MSA.PaymentService.Domain;
using System.Security.Claims;

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

        read.MapGet("/payments/summary", async Task<Results<Ok<PaymentSummaryDto>, ValidationProblem>> (
            ISender sender,
            CancellationToken cancellationToken,
            int days = 7) =>
        {
            if (days is < 1 or > 31)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(days)] = ["days must be between 1 and 31."]
                });
            }

            return TypedResults.Ok(await sender.Send(new GetPaymentSummaryQuery(days), cancellationToken));
        })
            .WithName("GetAdminPaymentSummary")
            .WithSummary("Summarize payment counts and amounts by status for the operations dashboard");

        read.MapGet("/balances/{userId}", async (
            string userId,
            ISender sender,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await sender.Send(new GetBalanceQuery(userId.Trim()), cancellationToken)))
            .WithName("GetAdminBalance");

        read.MapGet("/balances/{userId}/entries", async (
            string userId,
            ISender sender,
            CancellationToken cancellationToken,
            int skip = 0,
            int take = 50) =>
            TypedResults.Ok(await sender.Send(new ListBalanceEntriesQuery(userId.Trim(), skip, take), cancellationToken)))
            .WithName("ListAdminBalanceEntries");

        endpoints.MapPost("/payment/admin/balances/{userId}/adjustments", async Task<Results<Ok<BalanceEntryDto>, Created<BalanceEntryDto>, ValidationProblem, Conflict<string>>> (
            string userId,
            AdjustBalanceRequest request,
            ClaimsPrincipal user,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(userId)
                || request.Delta is 0 or < -1_000_000m or > 1_000_000m
                || string.IsNullOrWhiteSpace(request.BusinessKey) || request.BusinessKey.Length > 128
                || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request)] = ["调整金额须在 ±1,000,000 之间且非零；幂等键（≤128）与原因（≤500）必填。"]
                });
            }

            try
            {
                var result = await sender.Send(new AdjustBalanceCommand(
                    userId.Trim(),
                    request.Delta,
                    request.BusinessKey,
                    request.Reason,
                    user.FindFirstValue("sub") ?? throw new UnauthorizedAccessException()), cancellationToken);
                return result.IsReplay
                    ? TypedResults.Ok(result.Entry)
                    : TypedResults.Created($"/api/payment/admin/balances/{userId.Trim()}/entries", result.Entry);
            }
            catch (BalanceRuleException exception)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Delta)] = [exception.Message]
                });
            }
            catch (BalanceIdempotencyConflictException exception)
            {
                return TypedResults.Conflict(exception.Message);
            }
        })
        .RequireAuthorization("payment.admin.write")
        .WithName("AdjustAdminBalance");

        return endpoints;
    }
}

public sealed record AdjustBalanceRequest(decimal Delta, string BusinessKey, string Reason);
