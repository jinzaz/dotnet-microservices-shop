using Microsoft.EntityFrameworkCore;
using MSA.BFF.Common;
using MSA.BFF.Web;
using MSA.Grpc;
using MSA.Grpc.Contracts.Inventory;

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
// 秒杀余量推流：BFF 用自己的机器身份（scope inventory）订阅库存服务的 gRPC 流，再以 SSE 转给浏览器。
builder.Services.AddMsaServiceAuthentication(builder.Configuration.GetSection(ServiceAuthenticationOptions.SectionName));
builder.Services.AddMsaGrpcClient<InventoryAvailability.InventoryAvailabilityClient>(builder.Configuration.GetSection("GrpcClients:Inventory"));

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
app.MapFlashSaleStreamEndpoints();
app.MapReverseProxy();

app.Run();
