using MSA.Common;
using MSA.Observability;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MSA.BFF.BM;
using MSA.BFF.BM.Authorization;
using MSA.BFF.BM.Endpoints;
using MSA.BFF.BM.ObjectStorage;
using MSA.BFF.Common;
using MSA.BFF.Common.BodyEncryption;
using MSA.Certificates;
using MSA.Grpc;
using MSA.Grpc.Contracts.Inventory;
using MSA.Grpc.Contracts.Order;
using MSA.Grpc.Contracts.Payment;

var builder = WebApplication.CreateBuilder(args);
builder.AddMsaServiceDefaults();

var postgresql = builder.Configuration.GetConnectionString("Postgresql")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");
var redis = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.");

builder.Services.AddDbContext<BffBmDbContext>(options =>
{
    options.UseNpgsql(postgresql, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "bff_bm"));
    options.UseOpenIddict();
});
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddSingleton(TimeProvider.System);
// 证书库：OIDC state token 证书、请求体加密密钥的来源；DataProtection 密钥环（会话票据）也存进去，发版不再全员掉线。
builder.Services.AddMsaCertificates(builder.Configuration);
builder.Services.AddBffBodyEncryption();
builder.Services.AddBff(redis, "MSABm");
builder.Services.AddBffOpenIddictClient<BffBmDbContext>(
    builder.Configuration,
    builder.Environment,
    typeof(Program).Assembly);
builder.Services.AddBffReverseProxy(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddOptions<PbacOptions>()
    .Bind(builder.Configuration.GetSection(PbacOptions.SectionName))
    .Validate(options => options.BootstrapAdminUserIds.All(value =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Length <= 200 &&
            value == value.Trim()),
        "Pbac bootstrap user IDs must contain 1 to 200 characters without surrounding whitespace.")
    .ValidateOnStart();
builder.Services.AddScoped<PbacAuthorizer>();
builder.Services.AddScoped<BmDirectoryStore>();
builder.Services.AddScoped<PbacPolicyStore>();
builder.Services.AddOptions<ObjectStorageOptions>()
    .Bind(builder.Configuration.GetSection(ObjectStorageOptions.SectionName))
    .Validate(ObjectStorageOptions.IsValid,
        "ObjectStorage must define a valid endpoint, distinct public/private buckets, and credentials.")
    .ValidateOnStart();
builder.Services.AddSingleton<S3ObjectStorage>();
// 后台详情页的一次取数：并行问订单、支付、库存，走 BFF 自己的机器身份（scope order / payment / inventory）。
builder.Services.AddMsaServiceAuthentication(builder.Configuration.GetSection(ServiceAuthenticationOptions.SectionName));
builder.Services.AddMsaGrpcClient<OrderQuery.OrderQueryClient>(builder.Configuration.GetSection("GrpcClients:Order"));
builder.Services.AddMsaGrpcClient<PaymentQuery.PaymentQueryClient>(builder.Configuration.GetSection("GrpcClients:Payment"));
builder.Services.AddMsaGrpcClient<InventoryAvailability.InventoryAvailabilityClient>(builder.Configuration.GetSection("GrpcClients:Inventory"));
builder.Services.AddScoped<IAuthorizationHandler, PbacProxyAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, PbacPermissionAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PbacDefaults.ProxyPolicy, policy =>
        policy.RequireAuthenticatedUser().AddRequirements(new PbacProxyRequirement()));
    foreach (var action in Enum.GetValues<PbacAction>())
    {
        options.AddPolicy(PbacDefaults.ManagementPolicy(action), policy =>
            policy.RequireAuthenticatedUser().AddRequirements(
                new PbacPermissionRequirement(
                    PbacDefaults.ManagementPermission(action),
                    AllowBootstrapAdministrator: true)));
    }
    foreach (var permission in PbacSeedData.Permissions)
    {
        options.AddPolicy(PbacDefaults.PermissionPolicy(permission.Code), policy =>
            policy.RequireAuthenticatedUser().AddRequirements(
                new PbacPermissionRequirement(permission.Code)));
    }
});

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<BffBmDbContext>().Database.MigrateAsync();
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
app.MapPbacEndpoints();
app.MapObjectStorageEndpoints();
app.MapOrderOverviewEndpoints();
app.MapReverseProxy().RequireAuthorization(PbacDefaults.ProxyPolicy);

app.Run();
