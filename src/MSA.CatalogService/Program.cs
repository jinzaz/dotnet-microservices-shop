using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MSA.CatalogService.Application;
using MSA.CatalogService.Infrastructure;
using MSA.EventBus.DependencyInjection;
using MSA.EventBusOutbox.Extensions;
using MSA.Infrastrucute;
using MSA.Infrastrucute.Extensions;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
var builder = WebApplication.CreateBuilder(args); var pg = builder.Configuration.GetConnectionString("Postgresql"); if (string.IsNullOrWhiteSpace(pg)) throw new InvalidOperationException("ConnectionStrings:Postgresql is required."); var redis = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(pg)); builder.Services.AddScoped<MSADbContext>(s => s.GetRequiredService<CatalogDbContext>()); builder.Services.AddMediatR(c => c.RegisterServicesFromAssemblyContaining<CreateProductCommand>()); builder.Services.AddScoped<IValidator<CreateProductCommand>, CreateProductValidator>(); builder.Services.AddScoped<IValidator<UpdateProductCommand>, UpdateProductValidator>(); builder.Services.AddScoped<IValidator<CreateCategoryCommand>, CreateCategoryValidator>(); builder.Services.AddMsaApplicationPipeline(); builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redis);
builder.Services.AddRabbitMQEventBus(o => { o.HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost"; o.UserName = builder.Configuration["RabbitMQ:User"] ?? "guest"; o.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest"; o.QueueName = "msa.catalog"; }); builder.Services.AddOutbox<CatalogDbContext>(); builder.Services.AddHealthChecks(); builder.Services.AddProblemDetails();
builder.Services.AddOpenIddict().AddValidation(o => { o.SetIssuer(builder.Configuration["Authentication:Issuer"] ?? "https://localhost:10002/"); o.AddAudiences("api"); o.UseSystemNetHttp(); o.UseAspNetCore(); }); builder.Services.AddAuthorization(o => o.AddPolicy("catalog.write", p => p.RequireAssertion(c => c.User.Claims.Where(x => x.Type == "scope").SelectMany(x => x.Value.Split(' ')).Contains("catalog.write"))));
builder.Services.AddOpenTelemetry().WithTracing(t => t.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(builder.Environment.ApplicationName)).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporter());
var app = builder.Build(); if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup")) { await using var scope = app.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(); }
app.UseExceptionHandler(); app.UseAuthentication(); app.UseAuthorization(); app.MapHealthChecks("/health");
app.MapGet("/api/products", async (int skip, int take, ISender sender, CancellationToken ct) => Results.Ok(await sender.Send(new ListProductsQuery(skip, take), ct))).RequireAuthorization();
app.MapGet("/api/products/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) => { var p = await sender.Send(new GetProductQuery(id), ct); return p is null ? Results.NotFound() : Results.Ok(p); }).RequireAuthorization();
app.MapPost("/api/categories", async (CreateCategoryRequest r, ISender sender, CancellationToken ct) => { var id = await sender.Send(new CreateCategoryCommand(r.Name), ct); return Results.Created($"/api/categories/{id}", new { id }); }).RequireAuthorization("catalog.write");
app.MapPost("/api/products", async (CreateProductRequest r, ISender sender, CancellationToken ct) => { var id = await sender.Send(new CreateProductCommand(r.CategoryId, r.Sku, r.Name, r.Description, r.Price, r.Currency), ct); return Results.Created($"/api/products/{id}", new { id }); }).RequireAuthorization("catalog.write"); app.MapPut("/api/products/{id:guid}", async (Guid id, UpdateProductRequest r, ISender sender, CancellationToken ct) => { await sender.Send(new UpdateProductCommand(id, r.Name, r.Description, r.Price, r.IsActive), ct); return Results.NoContent(); }).RequireAuthorization("catalog.write"); app.Run();
/// <summary>Creates a product in the catalog.</summary>
public sealed record CreateProductRequest(Guid CategoryId, string Sku, string Name, string Description, decimal Price, string Currency = "CNY");
public sealed record UpdateProductRequest(string Name, string Description, decimal Price, bool IsActive);
public sealed record CreateCategoryRequest(string Name);
