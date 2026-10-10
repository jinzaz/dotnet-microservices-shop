using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSA.OrderService.Application.Pricing;
using MSA.OrderService.Domain;

namespace MSA.OrderService.Middleware;

internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, detail) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed", "One or more request values are invalid."),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized", "The access token does not identify a user."),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found", "The requested order resource was not found."),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request", "The request violates an order rule."),
            CouponRejectedException => (StatusCodes.Status409Conflict, "Coupon not applicable", exception.Message),
            AfterSalesRuleException => (StatusCodes.Status409Conflict, "After-sales rule", exception.Message),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Order conflict", "The request conflicts with the current order or campaign state."),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Order changed", "The order changed concurrently; retry with the same idempotency key."),
            DbUpdateException => (StatusCodes.Status409Conflict, "Order constraint conflict", "The request conflicts with an order uniqueness constraint."),
            _ => (0, string.Empty, string.Empty)
        };
        if (statusCode == 0)
            return false;

        logger.LogWarning(exception, "Handled order API exception with status {StatusCode}.", statusCode);
        httpContext.Response.StatusCode = statusCode;
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };
        // Clients localize coupon failures from the stable reason code rather than the English detail.
        if (exception is CouponRejectedException rejected)
        {
            problem.Extensions["couponReason"] = rejected.Reason.ToString();
            problem.Extensions["couponId"] = rejected.CouponId;
        }
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
