// 请求使用YARP直接透传到 YARP网关,去除Cookie和Authorization头部,手动添加OpenIddict Token; Cookie不直接存储 Token相关信息,通过 Session id存入Redis,通过Session id获取Token信息;添加antiforgery防止CSRF攻击
using Microsoft.EntityFrameworkCore;
using MSA.BFF.Web;
using MSA.BFF.Web.Extensions;
using MSA.BFF.Web.Middleware;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddDbContext<BffWebDbContext>(
        options =>
        {
            options.UseNpgsql(
                builder.Configuration
                    .GetConnectionString(
                        "BffOpenIddictDb"));

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
builder.Services.AddBffServicesDefault(builder.Configuration.GetConnectionString("Redis"));
builder.Services.AddYarp(builder.Configuration.GetSection("ReverseProxy"));



var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.UseMiddleware<AnttiforgeryValidationMiddleware>();

app.MapReverseProxy();

app.Run();
