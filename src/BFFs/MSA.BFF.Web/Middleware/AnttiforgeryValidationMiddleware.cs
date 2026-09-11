using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MSA.BFF.Web.Middleware;

public sealed class AnttiforgeryValidationMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var antiforgeryMetadata = context.GetEndpoint()?.Metadata.GetMetadata<IAntiforgeryMetadata>();
        var method = context.Request.Method;
        if (!HttpMethods.IsGet(method) &&
            !HttpMethods.IsHead(method) &&
            !HttpMethods.IsOptions(method) &&
            !HttpMethods.IsTrace(method) &&
            antiforgeryMetadata?.RequiresValidation is not false)
        {
            var antiforgery =
                context.RequestServices
                    .GetRequiredService<IAntiforgery>();

            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode =
                    StatusCodes.Status400BadRequest;
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
