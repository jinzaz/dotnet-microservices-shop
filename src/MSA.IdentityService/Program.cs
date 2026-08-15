using OpenIddict.Abstractions;

using OpenTelemetry.Trace;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection");


builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(connectionString);

    options.UseOpenIddict();
});

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;

        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services
    .AddOpenIddict()

    // OpenIddict Core
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<ApplicationDbContext>();
    })
    // OpenIddict Server
    .AddServer(options =>
    {
        // OAuth2 / OIDC endpoints

        options.SetAuthorizationEndpointUris(
            "/connect/authorize");

        options.SetTokenEndpointUris(
            "/connect/token");

        // Authorization Code Flow

        options.AllowAuthorizationCodeFlow();

        // PKCE

        options.RequireProofKeyForCodeExchange();

        // Scopes

        options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            "api");

        // Development certificates
        // Production 应该换成正式证书

        options.AddDevelopmentEncryptionCertificate()
               .AddDevelopmentSigningCertificate();

        // ASP.NET Core integration

        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough();
    });


var app = builder.Build();

// OpenTelemetry tracing
builder.Services.AddOpenTelemetryTracing(otelBuilder =>
{
    var svcName = builder.Environment.ApplicationName ?? "MSA.IdentityService";
    otelBuilder.SetResourceBuilder(OpenTelemetry.Resources.ResourceBuilder.CreateDefault().AddService(svcName))
        .AddAspNetCoreInstrumentation();
    try { otelBuilder.AddHttpClientInstrumentation(); } catch { }
    var otlp = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
    var enableConsole = bool.TryParse(builder.Configuration["OpenTelemetry:EnableConsoleExporter"], out var b) && b;
    if (!string.IsNullOrEmpty(otlp)) otelBuilder.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
    if (enableConsole) otelBuilder.AddConsoleExporter();
});

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
