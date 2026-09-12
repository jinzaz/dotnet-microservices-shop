using Microsoft.EntityFrameworkCore;
using MSA.Infrastrucute;
using MSA.Infrastrucute.Extensions;
using MSA.UserService.Application;
using MSA.UserService.Application.Addresses;
using MSA.UserService.Domain;
using MSA.UserService.Infrastructure;
using MSA.UserService.Middleware;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var postgresql = builder.Configuration.GetConnectionString("Postgresql");
if (string.IsNullOrWhiteSpace(postgresql))
    throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");

builder.Services.AddDbContext<UserDbContext>(options => options.UseNpgsql(postgresql));
builder.Services.AddScoped<MSADbContext>(services => services.GetRequiredService<UserDbContext>());
builder.Services.AddMediatR(configuration =>
    configuration.RegisterServicesFromAssemblyContaining<CreateUserAddressCommand>());
builder.Services.AddMsaApplicationPipeline();
builder.Services.AddScoped<UserStore>();
builder.Services.AddScoped<IUserAddressRepository>(services => services.GetRequiredService<UserStore>());
builder.Services.AddScoped<ILoyaltyPointRepository>(services => services.GetRequiredService<UserStore>());
builder.Services.AddScoped<IUserReadStore>(services => services.GetRequiredService<UserStore>());
builder.Services.AddScoped<IUserBusinessLock, PostgresUserBusinessLock>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<UserApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services.AddOpenIddict().AddValidation(options =>
{
    options.SetIssuer(builder.Configuration["Authentication:Issuer"] ?? "https://localhost:10002/");
    options.AddAudiences("user-api", "user.admin.write", "user.admin.read");
    options.UseSystemNetHttp();
    options.UseAspNetCore();
});
builder.Services.AddAuthorization();

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<UserDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapControllers();
app.Run();
