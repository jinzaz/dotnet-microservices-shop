using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MSA.CertificateService.Acme;
using MSA.CertificateService.Application;
using MSA.CertificateService.Endpoints;
using MSA.CertificateService.Infrastructure;
using MSA.Common;
using MSA.Infrastrucute.Extensions;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddMsaServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("Postgresql");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");

builder.Services.AddDbContext<CertificateDbContext>(options => options.UseNpgsql(connectionString, CertificateDbContext.ConfigureNpgsql));
builder.Services.AddSingleton(TimeProvider.System);
// 全部使用方的 KEK，缺一个就起不来。
builder.Services.AddSingleton(CertificateKekRing.FromEnvironment());
builder.Services.AddScoped<CertificateRegistry>();
builder.Services.AddScoped<TlsDeployer>();
builder.Services.AddOptions<TlsDeploymentOptions>().Bind(builder.Configuration.GetSection(TlsDeploymentOptions.SectionName));

builder.Services.AddScoped<AcmeIssuer>();
builder.Services.AddScoped<AcmeSubscriptions>();
builder.Services.AddSingleton<DnsPropagationChecker>();
builder.Services.AddSingleton<IDnsChallengeProvider, AliyunDnsProvider>();
builder.Services.AddSingleton<IDnsChallengeProvider, CloudflareDnsProvider>();
builder.Services.AddOptions<AcmeOptions>()
    .Bind(builder.Configuration.GetSection(AcmeOptions.SectionName))
    .Validate(options =>
            Uri.TryCreate(options.ProductionDirectory, UriKind.Absolute, out _) &&
            Uri.TryCreate(options.StagingDirectory, UriKind.Absolute, out _) &&
            Uri.TryCreate(options.DohResolver, UriKind.Absolute, out _),
        "Acme directories and DohResolver must be absolute URLs.")
    .ValidateOnStart();
var userAgent = $"MSA.CertificateService/{typeof(Program).Assembly.GetName().Version}";
builder.Services.AddHttpClient(AcmeOptions.HttpClientName, client => client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent));
builder.Services.AddHttpClient(DnsPropagationChecker.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient(AliyunDnsProvider.HttpClientName, client => client.BaseAddress = new Uri("https://alidns.aliyuncs.com/"));
builder.Services.AddHttpClient(CloudflareDnsProvider.HttpClientName, client => client.BaseAddress = new Uri("https://api.cloudflare.com/client/v4/"));

builder.Services.AddSingleton<CertificateRenewalTrigger>();
builder.Services.AddSingleton<CertificateRenewalStatus>();
builder.Services.AddOptions<CertificateRenewalOptions>()
    .Bind(builder.Configuration.GetSection(CertificateRenewalOptions.SectionName))
    .Validate(options => options.Interval >= TimeSpan.FromMinutes(1), "Renewal:Interval must be at least one minute.")
    .ValidateOnStart();
builder.Services.AddHostedService<CertificateRenewalService>();

builder.Services.AddOpenIddict().AddValidation(options =>
{
    options.SetMsaIssuer(builder.Configuration);
    options.AddAudiences("certificate-api");
    options.UseSystemNetHttp();
    options.UseAspNetCore();
});
builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services.AddMsaApiAuthorization("certificate-api", CertificateAdminEndpoints.ReadPolicy, CertificateAdminEndpoints.WritePolicy);
builder.Services.AddExceptionHandler<CertificateApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CertificateDbContext>().Database.MigrateAsync();
}

app.UseMsaServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapCertificateAdminEndpoints();
app.Run();
