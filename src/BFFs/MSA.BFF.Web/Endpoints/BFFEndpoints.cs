using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MSA.BFF.Web.Endpoints;

internal static class BFFEndpoints
{
    internal static IEndpointRouteBuilder MapBFFEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var bffGroup = endpoints.MapGroup("/bff")
            .WithTags("bff");


        bffGroup.MapGet("/csrf", GetCSRFAsync)
            .AllowAnonymous()
            .WithName("GetCSRF")
            .WithSummary("Retrieve the CSRF token for the current session.")
            .Produces<CSRFDto>()
            .ProducesProblem(StatusCodes.Status204NoContent);

        bffGroup.MapMethods("/login", [HttpMethods.Get, HttpMethods.Post], Login)
            .AllowAnonymous()
            .WithName("Login")
            .WithSummary("Initiate the login process");

        bffGroup.MapMethods("/logout", [HttpMethods.Get, HttpMethods.Post], Logout)
            .AllowAnonymous()
            .WithName("Logout")
            .WithSummary("Initiate the logout process");


        bffGroup.MapMethods("/callback/login", [HttpMethods.Get, HttpMethods.Post], LoginCallback)
            .AllowAnonymous()
            .WithName("LoginCallback")
            .WithSummary("Initiate the login-callback");

        bffGroup.MapMethods("/callback/logout", [HttpMethods.Get, HttpMethods.Post], LogoutCallback)
            .AllowAnonymous()
            .WithName("LogoutCallback")
            .WithSummary("Initiate the logout-callback");



        return endpoints;
    }

    private static async Task<Results<NoContent, Ok<CSRFDto>>> GetCSRFAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);

        if(tokens?.RequestToken is null)
            return TypedResults.NoContent();


        return TypedResults.Ok(new CSRFDto
        {
            Token = tokens.RequestToken
        });

    }

    private static async Task<ActionResult> Login(string returnUrl)
    {
    }

    private static async Task<ActionResult> Logout(string returnUrl)
    {
    }

    private static async Task<ActionResult> LoginCallback()
    {

    }

    private static async Task<ActionResult> LogoutCallback()
    {

    }
}


public sealed record CSRFDto
{
    public required string Token { get; set; }
}
