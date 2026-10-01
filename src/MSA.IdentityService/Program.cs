using MSA.Observability;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MSA.Infrastrucute.Extensions;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var connection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(connection);
    options.UseOpenIddict();
});


builder.Services.AddIdentityDefault(builder.Configuration, builder.Environment);
builder.Services.AddIdentityRateLimiting();
builder.Services.AddSmsLogin(builder.Configuration, builder.Environment);

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


builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddHostedService<IdentitySeedHostedService>();
builder.Services.AddMsaApiAuthorization(
    IdentityServiceConstants.Resources.Auth,
    IdentityServiceConstants.Scopes.AuthAdminRead,
    IdentityServiceConstants.Scopes.AuthAdminWrite);
// 链路、指标、日志统一走 OTLP，配置见 MSA.Observability。
builder.AddMsaObservability();
builder.Services.AddHealthChecks();

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapControllers();
app.Run();
