using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MSA.IdentityService.Application;
using MSA.IdentityService.Application.Addresses;
using MSA.IdentityService.Domain;
using MSA.IdentityService.Infrastructure;
using MSA.IdentityService.Middleware;
using MSA.Infrastrucute.Mediator.Behaviors;
using OpenTelemetry.Resources;
using Quartz;
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


builder.Services.AddIdentityDefault();

builder.Services.AddQuartz(options =>
{
    options.UseSimpleTypeLoader();
    options.UseInMemoryStore();
});

builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
});

builder.Services.AddOpenIddictServer(builder.Configuration, builder.Environment);


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
