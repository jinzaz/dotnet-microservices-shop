using MSA.Common;
using MSA.Observability;
using Microsoft.EntityFrameworkCore;
using MSA.BFF.Common;
using MSA.BFF.Common.BodyEncryption;
using MSA.Certificates;
using MSA.BFF.Web;
using MSA.Grpc;
using MSA.Grpc.Contracts.Inventory;

var builder = WebApplication.CreateBuilder(args);
builder.AddMsaServiceDefaults();

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
// 证书库：OIDC state token 证书、请求体加密密钥的来源；DataProtection 密钥环（会话票据）也存进去，发版不再全员掉线。
builder.Services.AddMsaCertificates(builder.Configuration);
builder.Services.AddBffBodyEncryption();
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

app.UseMsaServiceDefaults();
app.UseBffBodyEncryption();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseAuthentication();
app.UseAuthorization();
app.UseBffAntiforgery();

app.MapBodyEncryptionPublicKeyEndpoint();
app.MapBffEndpoints();
app.MapFlashSaleStreamEndpoints();
app.MapReverseProxy();

app.Run();
