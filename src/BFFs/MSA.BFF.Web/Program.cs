using Microsoft.EntityFrameworkCore;
using MSA.BFF.Common;
using MSA.BFF.Web;

var builder = WebApplication.CreateBuilder(args);

var postgresql = builder.Configuration.GetConnectionString("Postgresql")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");
var redis = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.");

builder.Services.AddDbContext<BffWebDbContext>(options =>
{
    options.UseNpgsql(postgresql, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "bff_web"));
    options.UseOpenIddict();
});
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddBff(redis, "MSAWeb");
builder.Services.AddBffOpenIddictClient<BffWebDbContext>(
    builder.Configuration,
    builder.Environment,
    typeof(Program).Assembly);
builder.Services.AddBffReverseProxy(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<BffWebDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseBffAntiforgery();

app.MapBffEndpoints();
app.MapReverseProxy();

app.Run();
