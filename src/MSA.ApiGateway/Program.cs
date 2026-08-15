var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

//builder.Services.AddControllers();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services
    .AddOpenIddict()
    .AddValidation(options =>
    {
        // OpenIddict Server 地址

        options.SetIssuer(
            "https://auth.example.com/");

        // Access Token 的 Audience

        options.AddAudiences("api");

        // 获取 OpenIddict Server 的 metadata / keys

        options.UseSystemNetHttp();

        // ASP.NET Core Authentication integration

        options.UseAspNetCore();
    });

var app = builder.Build();

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapReverseProxy();

app.Run();
