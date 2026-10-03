using MSA.Common;
using MSA.Observability;
using Microsoft.EntityFrameworkCore;
using MSA.Grpc;
using MSA.Infrastrucute;
using MSA.Infrastrucute.Extensions;
using MSA.UserService.Application;
using MSA.UserService.Application.Addresses;
using MSA.UserService.Domain;
using MSA.UserService.Grpc;
using MSA.UserService.Infrastructure;
using MSA.UserService.Middleware;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
// 基础设施地址、OTLP 与 gRPC 下游地址、客户端密钥来自环境变量或仓库根目录的 msa.env，见 MsaEnvironment。
builder.Configuration.AddMsaEnvironment(builder.Environment.ContentRootPath);
// 链路、指标、日志统一走 OTLP，配置见 MSA.Observability。
builder.AddMsaObservability();
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
// 服务间实时调用：收货地址的权威读取走 gRPC，策略沿用现有的 user-api（scope user / resource user-api）。
builder.Services.AddMsaGrpcServer(builder.Environment.IsDevelopment());
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<UserApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services.AddOpenIddict().AddValidation(options =>
{
    options.SetMsaIssuer(builder.Configuration);
    options.AddAudiences("user-api", "user.admin.write", "user.admin.read");
    options.UseSystemNetHttp();
    options.UseAspNetCore();
});
builder.Services.AddMsaApiAuthorization(
    "user-api",
    "user.admin.read",
    "user.admin.write");

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
app.MapMsaGrpcService<UserAddressGrpcService>("user-api");
app.Run();
