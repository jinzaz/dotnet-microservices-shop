using System.Globalization;
using System.Security.Claims;
using MSA.BFF.BM.Authorization;
using MSA.Grpc;
using MSA.Grpc.Contracts.Inventory;
using MSA.Grpc.Contracts.Order;
using MSA.Grpc.Contracts.Payment;

namespace MSA.BFF.BM.Endpoints;

internal sealed record OrderOverviewPayment(
    Guid PaymentId,
    string Status,
    decimal Amount,
    string Currency,
    string Channel,
    DateTimeOffset? ExpiresOnUtc);

internal sealed record OrderOverviewReservationLine(string Sku, int Quantity, Guid? OfferId);

internal sealed record OrderOverviewReservation(
    Guid ReservationId,
    string Status,
    DateTimeOffset? ExpiresOnUtc,
    IReadOnlyList<OrderOverviewReservationLine> Lines);

/// <param name="Unavailable">这次没问到的下游；页面据此显示「暂不可用」而不是「没有」。</param>
internal sealed record OrderOverviewResponse(
    Guid OrderId,
    string OrderNumber,
    string UserId,
    decimal Amount,
    string Currency,
    string Status,
    string FulfillmentStatus,
    string PaymentMethod,
    string? CarrierName,
    string? TrackingNumber,
    OrderOverviewPayment? Payment,
    OrderOverviewReservation? Reservation,
    IReadOnlyList<string> Unavailable);

/// <summary>
/// 后台订单详情的一次取数：订单、支付、库存预占三个服务并行问一遍，页面不用串三次 REST。
/// 权限仍由 PBAC 把关（看订单要 order.fulfillment.query，支付那一段另外要 payment.transactions.query），
/// 对下游用的是 BFF 自己的机器身份，所以这里必须先判权限再取数。
/// </summary>
internal static class OrderOverviewEndpoints
{
    private const string OrderPermission = "order.fulfillment.query";
    private const string PaymentPermission = "payment.transactions.query";

    internal static IEndpointRouteBuilder MapOrderOverviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/bff/orders/{orderId:guid}/overview", GetOverviewAsync)
            .RequireAuthorization(PbacDefaults.PermissionPolicy(OrderPermission))
            .WithTags("BM Order Overview")
            .WithName("GetOrderOverview")
            .WithSummary("Read an order with its payment and inventory reservation in one round trip.")
            .Produces<OrderOverviewResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetOverviewAsync(
        Guid orderId,
        ClaimsPrincipal principal,
        PbacAuthorizer authorizer,
        OrderQuery.OrderQueryClient orders,
        PaymentQuery.PaymentQueryClient payments,
        InventoryAvailability.InventoryAvailabilityClient inventory,
        ILogger<OrderOverviewResponse> logger,
        CancellationToken cancellationToken)
    {
        var canReadPayment = await authorizer.IsAllowedAsync(principal, PaymentPermission, cancellationToken);

        // 三个下游同时问：整页的耗时是最慢的那一个，而不是三个之和。
        var orderTask = Ask(logger, "order", () => orders
            .GetOrderSnapshotAsync(new GetOrderSnapshotRequest { OrderId = orderId.ToString() }, cancellationToken: cancellationToken)
            .ResponseAsync);
        var paymentTask = canReadPayment
            ? Ask(logger, "payment", () => payments
                .GetOrderPaymentAsync(new GetOrderPaymentRequest { OrderId = orderId.ToString() }, cancellationToken: cancellationToken)
                .ResponseAsync)
            : Task.FromResult<GetOrderPaymentResponse?>(null);
        var reservationTask = Ask(logger, "inventory", () => inventory
            .GetOrderReservationAsync(new GetOrderReservationRequest { OrderId = orderId.ToString() }, cancellationToken: cancellationToken)
            .ResponseAsync);

        await Task.WhenAll(orderTask, paymentTask, reservationTask);
        var order = orderTask.Result;
        if (order is null)
            return Results.Problem("Ordering is unavailable right now.", statusCode: StatusCodes.Status503ServiceUnavailable);
        if (!order.Found)
            return Results.NotFound();

        var payment = paymentTask.Result;
        var reservation = reservationTask.Result;
        var unavailable = new List<string>();
        if (canReadPayment && payment is null)
            unavailable.Add("payment");
        if (reservation is null)
            unavailable.Add("inventory");

        return Results.Ok(new OrderOverviewResponse(
            orderId,
            order.OrderNumber,
            order.UserId,
            ParseAmount(order.Amount),
            order.Currency,
            order.Status,
            order.FulfillmentStatus,
            order.PaymentMethod,
            Text(order.CarrierName),
            Text(order.TrackingNumber),
            payment is { Found: true }
                ? new OrderOverviewPayment(
                    Guid.Parse(payment.PaymentId),
                    payment.Status,
                    ParseAmount(payment.Amount),
                    payment.Currency,
                    payment.Channel,
                    ParseInstant(payment.ExpiresOnUtc))
                : null,
            reservation is { Found: true }
                ? new OrderOverviewReservation(
                    Guid.Parse(reservation.ReservationId),
                    reservation.Status,
                    ParseInstant(reservation.ExpiresOnUtc),
                    reservation.Lines
                        .Select(line => new OrderOverviewReservationLine(
                            line.Sku,
                            line.Quantity,
                            Guid.TryParse(line.OfferId, out var offerId) ? offerId : null))
                        .ToArray())
                : null,
            unavailable));
    }

    /// <summary>某个下游问不到就返回 null：一个服务抖动不该让整页打不开。</summary>
    private static async Task<TResponse?> Ask<TResponse>(
        ILogger logger,
        string service,
        Func<Task<TResponse>> call)
        where TResponse : class
    {
        try
        {
            return await call();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Order overview could not read {Service}.", service);
            return null;
        }
    }

    private static decimal ParseAmount(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : 0m;

    private static DateTimeOffset? ParseInstant(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, out var instant) ? instant : null;

    private static string? Text(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
