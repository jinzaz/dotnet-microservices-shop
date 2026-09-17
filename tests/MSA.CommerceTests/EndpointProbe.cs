using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace MSA.CommerceTests;

internal sealed class EndpointProbe : IAsyncDisposable
{
    private readonly WebApplication app;

    public EndpointProbe(Type assemblyAnchor, string endpointTypeName, params string[] mapMethods)
    {
        Sender = new Mock<ISender>();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Services.AddSingleton(Sender.Object);
        app = builder.Build();

        foreach (var methodName in mapMethods)
            Map(assemblyAnchor, endpointTypeName, methodName);
    }

    public Mock<ISender> Sender { get; }
    public IReadOnlyList<RouteEndpoint> Routes => ((IEndpointRouteBuilder)app).DataSources
        .SelectMany(source => source.Endpoints)
        .OfType<RouteEndpoint>()
        .ToArray();

    public void Map(Type assemblyAnchor, string endpointTypeName, string methodName)
    {
        var endpointType = assemblyAnchor.Assembly.GetType(endpointTypeName, throwOnError: true)!;
        var method = endpointType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(endpointTypeName, methodName);
        method.Invoke(null, [app]);
    }

    public RouteEndpoint Find(string method, string route) =>
        Routes.Single(endpoint =>
            endpoint.RoutePattern.RawText?.Trim('/') == route.Trim('/')
            && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true);

    public static void AssertPolicy(RouteEndpoint endpoint, string expectedPolicy)
    {
        var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(metadata => metadata.Policy)
            .Where(policy => !string.IsNullOrEmpty(policy))
            .ToArray();
        CollectionAssert.AreEqual(new[] { expectedPolicy }, policies);
    }

    public async Task<DefaultHttpContext> InvokeAsync(
        string method,
        string route,
        string? query = null,
        object? body = null,
        IReadOnlyDictionary<string, string>? routeValues = null,
        IReadOnlyDictionary<string, string>? headers = null,
        string? userId = null)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = app.Services
        };
        context.Request.Method = method;
        context.Request.Path = "/" + route.Trim('/');
        context.Request.QueryString = new QueryString(query ?? "");
        context.Response.Body = new MemoryStream();
        if (body is not null)
        {
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(body);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
            context.Request.ContentType = "application/json";
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature());
        }
        if (routeValues is not null)
        {
            foreach (var (key, value) in routeValues)
                context.Request.RouteValues[key] = value;
        }
        if (headers is not null)
        {
            foreach (var (key, value) in headers)
                context.Request.Headers[key] = value;
        }
        if (userId is not null)
        {
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim("sub", userId)], "unit-test"));
        }

        await Find(method, route).RequestDelegate!(context);
        context.Response.Body.Position = 0;
        return context;
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();

    private sealed class RequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
