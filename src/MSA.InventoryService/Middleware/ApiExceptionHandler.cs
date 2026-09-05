using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MSA.InventoryService.Middleware;

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
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found", "The requested inventory resource was not found."),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request", "The request violates an inventory rule."),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Inventory conflict", "The request conflicts with the current inventory state."),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Inventory changed", "Inventory changed concurrently; retry the request."),
            DbUpdateException => (StatusCodes.Status409Conflict, "Inventory constraint conflict", "The request conflicts with an inventory uniqueness or quantity constraint."),
            _ => (0, string.Empty, string.Empty)
        };
        if (statusCode == 0)
            return false;

        logger.LogWarning(exception, "Handled inventory API exception with status {StatusCode}.", statusCode);
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
