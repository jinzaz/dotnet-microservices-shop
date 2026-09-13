using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MSA.BFF.Common.Middleware;

internal sealed class AntiforgeryValidationMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<IAntiforgeryMetadata>();
        var method = context.Request.Method;
        if (!HttpMethods.IsGet(method) &&
            !HttpMethods.IsHead(method) &&
            !HttpMethods.IsOptions(method) &&
            !HttpMethods.IsTrace(method) &&
            metadata?.RequiresValidation is not false)
        {
            try
            {
                await context.RequestServices
                    .GetRequiredService<IAntiforgery>()
                    .ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Title = "Invalid CSRF token.",
                        Instance = context.Request.Path
                    },
                    context.RequestAborted);
                return;
            }
        }

        await next(context);
    }
}
