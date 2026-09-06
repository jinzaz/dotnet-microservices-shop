using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MSA.IdentityService.Application;
using MSA.IdentityService.Application.Addresses;
using MSA.IdentityService.Domain;
using MSA.IdentityService.Infrastructure;
using MSA.IdentityService.Middleware;
using MSA.Infrastrucute.Mediator.Behaviors;
using OpenIddict.Abstractions;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<IdentityApiExceptionHandler>();
builder.Services.AddProblemDetails();

var connection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(connection);
    options.UseOpenIddict();
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 10;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.User.RequireUniqueEmail = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.SignIn.RequireConfirmedEmail = false;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<ApplicationDbContext>())
    .AddServer(options =>
    {
        var issuer = builder.Configuration["Authentication:Issuer"];
        if (!string.IsNullOrWhiteSpace(issuer))
            options.SetIssuer(new Uri(issuer));
        options.SetAuthorizationEndpointUris("/connect/authorize");
        options.SetTokenEndpointUris("/connect/token");
        options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
        options.AllowPasswordFlow();
        options.AllowRefreshTokenFlow();
        options.AllowClientCredentialsFlow();
        options.DisableAccessTokenEncryption();
        options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.OfflineAccess,
            "api",
            "catalog.write",
            "inventory.write",
            "notifications.read.all");
        if (builder.Environment.IsDevelopment())
        {
            options.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
        }
        else
        {
            var path = builder.Configuration["Certificates:Path"];
            var password = builder.Configuration["Certificates:Password"];
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("A production OpenIddict certificate is required.");
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password);
            options.AddEncryptionCertificate(certificate).AddSigningCertificate(certificate);
        }

        options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

builder.Services.AddMediatR(configuration =>
    configuration.RegisterServicesFromAssemblyContaining<CreateUserAddressCommand>());
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdentityTransactionBehavior<,>));
builder.Services.AddScoped<IdentityCustomerStore>();
builder.Services.AddScoped<IUserAddressRepository>(services => services.GetRequiredService<IdentityCustomerStore>());
builder.Services.AddScoped<ILoyaltyPointRepository>(services => services.GetRequiredService<IdentityCustomerStore>());
builder.Services.AddScoped<IIdentityCustomerReadStore>(services => services.GetRequiredService<IdentityCustomerStore>());
builder.Services.AddScoped<IUserBusinessLock, PostgresUserBusinessLock>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddHostedService<IdentitySeedHostedService>();
builder.Services.AddAuthorization();
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(builder.Environment.ApplicationName))
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddOtlpExporter());
builder.Services.AddHealthChecks();

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapControllers();
app.Run();
