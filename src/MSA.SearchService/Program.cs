using MSA.Common;
using MSA.Contracts;
using MSA.EventBus.DependencyInjection;
using MSA.EventBusRabbitMQ;
using MSA.Grpc;
using MSA.Grpc.Contracts.Catalog;
using MSA.Infrastrucute.Extensions;
using MSA.Observability;
using MSA.SearchService;
using OpenIddict.Validation.AspNetCore;

// 商品搜索：Elasticsearch + IK 分词的索引与查询。目录是商品数据的权威，这里的索引只是它的投影——
// 增量靠目录 Outbox 发出的 CatalogSearchInvalidated 消息 + gRPC 回读最新快照，全量靠 gRPC 分页导出。
// 本服务没有自己的数据库；ES 不可用时照常启动，搜索返回 503，前台回退到目录服务的数据库搜索。
var builder = WebApplication.CreateBuilder(args);
builder.AddMsaServiceDefaults();

builder.Services.AddOptions<ElasticsearchOptions>()
    .Bind(builder.Configuration.GetSection(ElasticsearchOptions.SectionName))
    .Validate(options => Uri.TryCreate(options.Url, UriKind.Absolute, out _),
        "Elasticsearch:Url must be an absolute URL (set INFRA_ELASTICSEARCH_URL in msa.env or the environment).")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Alias), "Elasticsearch:Alias is required.")
    // 证书路径写错要在启动时就失败，而不是等到第一次请求 ES 才在连接池里抛出、被熔断吞成 503。
    .Validate(options => string.IsNullOrWhiteSpace(options.CaCertificatePath) || File.Exists(options.CaCertificatePath),
        "Elasticsearch:CaCertificatePath (INFRA_ELASTICSEARCH_CA_CERT) points to a file that does not exist.")
    .Validate(options => options.ExportPageSize is > 0 and <= 1_000, "Elasticsearch:ExportPageSize must be 1-1000.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
ElasticsearchHttp.Configure(builder.Services);
builder.Services.AddSingleton<ElasticsearchHttp>();
builder.Services.AddSingleton<BuildingAliasCache>();
builder.Services.AddTransient<ProductIndexWriter>();
builder.Services.AddSingleton<IndexRebuilder>();
builder.Services.AddHostedService<SearchIndexBootstrapper>();
builder.Services.AddSingleton<SearchCircuitBreaker>();
builder.Services.AddSingleton<ProductSearchService>();

// 用自己的机器身份（scope catalog）经 gRPC 回读目录。
builder.Services.AddMsaServiceAuthentication(builder.Configuration.GetSection(ServiceAuthenticationOptions.SectionName));
builder.Services.AddMsaGrpcClient<CatalogSearchFeed.CatalogSearchFeedClient>(builder.Configuration.GetSection("GrpcClients:Catalog"));

builder.Services.AddRabbitMQEventBus(builder.Configuration.GetSection(RabbitMQOptions.SectionName));
builder.Services.AddIntegrationEventHandler<CatalogSearchInvalidatedIntegrationEvent, CatalogSearchInvalidatedHandler>();

builder.Services.AddOpenIddict().AddValidation(options =>
{
    options.SetMsaIssuer(builder.Configuration);
    options.AddAudiences("search-api");
    options.UseSystemNetHttp();
    options.UseAspNetCore();
});
builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services.AddMsaApiAuthorization("search-api", "search.admin.read", "search.admin.write");

var app = builder.Build();
app.UseMsaServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapSearchEndpoints();
app.Run();
