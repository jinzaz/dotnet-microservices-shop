using Microsoft.AspNetCore.Antiforgery;

namespace MSA.BFF.Web.Middleware;

public class AnttiforgeryValidationMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE")
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

                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Invalid CSRF token."
                });

                return;
            }
        }

        await next(context);
    }
}
