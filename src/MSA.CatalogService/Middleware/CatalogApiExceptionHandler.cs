using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSA.CatalogService.Domain;

namespace MSA.CatalogService.Middleware;

internal sealed class CatalogApiExceptionHandler(ILogger<CatalogApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, detail) = exception switch
        {
            ValidationException =>
                (StatusCodes.Status400BadRequest, "Validation failed", "One or more request values are invalid."),
            KeyNotFoundException =>
                (StatusCodes.Status404NotFound, "Catalog resource not found", exception.Message),
            ArgumentException =>
                (StatusCodes.Status400BadRequest, "Invalid catalog request", exception.Message),
            DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict, "Catalog data changed", "The resource changed concurrently; reload it and retry."),
            CouponClaimRejectedException =>
                (StatusCodes.Status409Conflict, "Coupon claim rejected", exception.Message),
            DbUpdateException =>
                (StatusCodes.Status409Conflict, "Catalog constraint conflict", "The operation conflicts with catalog uniqueness or integrity constraints."),
            InvalidOperationException =>
                (StatusCodes.Status409Conflict, "Catalog state conflict", exception.Message),
            _ => (0, string.Empty, string.Empty)
        };

        if (statusCode == 0)
            return false;

        logger.LogWarning(exception, "Handled catalog API exception with status {StatusCode}.", statusCode);
        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
                Instance = httpContext.Request.Path,
                // Storefront buttons switch on the reason code rather than parsing the English message.
                Extensions = exception is CouponClaimRejectedException rejected
                    ? new Dictionary<string, object?> { ["reason"] = rejected.Reason.ToString() }
                    : new Dictionary<string, object?>()
            },
            cancellationToken);
        return true;
    }
}
