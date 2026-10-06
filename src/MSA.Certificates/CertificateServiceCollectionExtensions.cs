using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace MSA.Certificates;

public static class CertificateServiceCollectionExtensions
{
    /// <summary>证书库连接串名：ConnectionStrings:Certificates（appsettings 里写库名，地址与口令由 MsaEnvironment 合并）。</summary>
    public const string ConnectionStringName = "Certificates";

    /// <summary>
    /// 注册证书库读取（<see cref="CertificateStore"/>）与 DataProtection 密钥环持久化。
    /// 没配 ConnectionStrings:Certificates 时只注册一个未启用的证书库：各集成点退回环境变量 / 开发证书，DataProtection 保持默认。
    /// 配了就要求 Certificates:Consumer 与对应的 MSA_CERT_KEK_*，缺了启动失败。
    /// </summary>
    public static IServiceCollection AddMsaCertificates(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.TryAddSingleton(CertificateStore.Disabled);
            return services;
        }

        var consumer = configuration[$"{CertificateStoreOptions.SectionName}:{nameof(CertificateStoreOptions.Consumer)}"];
        if (!CertificateConsumers.IsKnown(consumer) || consumer == CertificateConsumers.Edge)
        {
            throw new InvalidOperationException(
                "Certificates:Consumer must be one of identity, bff-web or bff-bm when ConnectionStrings:Certificates is set.");
        }

        var kek = CertificateKek.FromEnvironment(consumer!);
        services.AddOptions<CertificateStoreOptions>()
            .Bind(configuration.GetSection(CertificateStoreOptions.SectionName))
            .Validate(options => options.PollInterval >= TimeSpan.FromSeconds(5), "Certificates:PollInterval must be at least 5 seconds.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(kek);
        services.AddSingleton(_ => new CertificateDatabase(NpgsqlDataSource.Create(connectionString)));
        // AddSingleton 而不是 TryAdd：UseMsaCertificates() 先注册的未启用占位要被这里覆盖。
        services.AddSingleton(provider => new CertificateStore(consumer!, isEnabled: true, provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<CertificateStoreLoader>();
        services.AddHostedService<CertificateStoreRefresher>();

        // 应用名按使用方区分：两个 BFF 同库存密钥环，各管各的。
        services.AddDataProtection().SetApplicationName($"msa-{consumer}");
        services.AddOptions<KeyManagementOptions>()
            .Configure<CertificateDatabase, CertificateKek>((options, database, key) =>
            {
                options.XmlRepository = new CertificateDbXmlRepository(database.DataSource, key.Consumer);
                options.XmlEncryptor = new KekXmlEncryptor(key);
            });

        return services;
    }
}
