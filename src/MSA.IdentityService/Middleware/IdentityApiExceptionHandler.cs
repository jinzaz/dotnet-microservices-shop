using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSA.IdentityService.Domain;
using Npgsql;

namespace MSA.IdentityService.Middleware;

public sealed class IdentityApiExceptionHandler(ILogger<IdentityApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            IdentityResourceNotFoundException =>
                (StatusCodes.Status404NotFound, "Resource not found", "The requested identity resource was not found."),
            IdentityIdempotencyConflictException =>
                (StatusCodes.Status409Conflict, "Idempotency conflict", "The business key was already used for another operation."),
            IdentityBusinessException =>
                (StatusCodes.Status409Conflict, "Business rule conflict", "The operation conflicts with the current resource state."),
            DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict, "Concurrency conflict", "The resource was changed by another request. Retry with fresh data."),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                (StatusCodes.Status409Conflict, "Uniqueness conflict", "The operation conflicts with an existing resource."),
            ArgumentException =>
                (StatusCodes.Status400BadRequest, "Invalid request", "One or more request values are invalid."),
            UnauthorizedAccessException =>
                (StatusCodes.Status401Unauthorized, "Unauthorized", "An authenticated user subject is required."),
            _ => (0, string.Empty, string.Empty)
        };

        if (status == 0)
            return false;

        logger.LogWarning(exception, "Handled Identity API exception with status {StatusCode}", status);
        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        }, cancellationToken);
        return true;
    }
}
