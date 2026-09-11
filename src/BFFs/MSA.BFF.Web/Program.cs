// 请求使用YARP直接透传到 YARP网关,去除Cookie和Authorization头部,手动添加OpenIddict Token; Cookie不直接存储 Token相关信息,通过 Session id存入Redis,通过Session id获取Token信息;添加antiforgery防止CSRF攻击
using Microsoft.EntityFrameworkCore;
using MSA.BFF.Web;
using MSA.BFF.Web.Endpoints;
using MSA.BFF.Web.Extensions;
using MSA.BFF.Web.Middleware;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

var postgresqlConnectionString = builder.Configuration.GetConnectionString("Postgresql")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.");

builder.Services
    .AddDbContext<BffWebDbContext>(
        options =>
        {
            options.UseNpgsql(postgresqlConnectionString, npgsql => {
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "bff_web");
            });

            options.UseOpenIddict();
        });


builder.Services.AddQuartz(options =>
{
    options.UseSimpleTypeLoader();
    options.UseInMemoryStore();
});

builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddAuthorization();
builder.Services.AddTransient<AnttiforgeryValidationMiddleware>();
builder.Services.AddBffServicesDefault(redisConnectionString);
builder.Services.AddOpenIddictDefault(builder.Configuration, builder.Environment);
builder.Services.AddYarp(builder.Configuration.GetSection("ReverseProxy"));



var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<BffWebDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<AnttiforgeryValidationMiddleware>();

app.MapBFFEndpoints();
app.MapReverseProxy();

app.Run();
