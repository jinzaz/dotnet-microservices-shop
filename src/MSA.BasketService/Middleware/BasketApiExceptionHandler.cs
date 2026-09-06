using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSA.BasketService.Domain;

namespace MSA.BasketService.Middleware;

internal sealed class BasketApiExceptionHandler(ILogger<BasketApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, detail) = exception switch
        {
            ValidationException =>
                (StatusCodes.Status400BadRequest, "Validation failed", "One or more basket request values are invalid."),
            UnauthorizedAccessException =>
                (StatusCodes.Status401Unauthorized, "Unauthorized", "The access token does not identify a user."),
            BasketVersionConflictException conflict =>
                (StatusCodes.Status412PreconditionFailed, "Basket version mismatch", conflict.Message),
            KeyNotFoundException =>
                (StatusCodes.Status404NotFound, "Basket item not found", "The requested basket item was not found."),
            ProductUnavailableException unavailable =>
                (StatusCodes.Status409Conflict, "Product unavailable", unavailable.Message),
            BasketRuleException rule =>
                (StatusCodes.Status409Conflict, "Basket rule conflict", rule.Message),
            ArgumentException =>
                (StatusCodes.Status400BadRequest, "Invalid request", "The request violates a basket input rule."),
            DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict, "Basket changed", "The basket changed concurrently; reload it and retry."),
            DbUpdateException =>
                (StatusCodes.Status409Conflict, "Basket constraint conflict", "The request conflicts with the current basket state."),
            _ => (0, string.Empty, string.Empty)
        };
        if (statusCode == 0)
            return false;

        logger.LogWarning(exception, "Handled basket API exception with status {StatusCode}.", statusCode);
        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
                Instance = httpContext.Request.Path
            },
            cancellationToken);
        return true;
    }
}
