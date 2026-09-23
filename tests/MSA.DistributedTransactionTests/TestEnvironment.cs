using Microsoft.Extensions.Configuration;

namespace MSA.DistributedTransactionTests;

/// <summary>
/// 测试基础设施的连接信息。默认读取 testsettings.json，可用环境变量覆盖：
/// MSA_TEST_POSTGRES、MSA_TEST_RABBITMQ__HostName 等（双下划线表示层级）。
/// 没有配置或连不上时，依赖它们的测试会标记为 Inconclusive 而不是失败，本地无基础设施也能跑其余用例。
/// </summary>
internal static class TestEnvironment
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddJsonFile("testsettings.json", optional: true)
        .AddEnvironmentVariables("MSA_TEST_")
        .Build();

    /// <summary>共享测试库里的独立 schema，避免和别的用途混在一起。</summary>
    public const string Schema = "dtx";

    /// <summary>队列、交换机统一用这个前缀，和生产拓扑区分开。</summary>
    public static string QueuePrefix => Configuration["RabbitMQ:QueuePrefix"] ?? "test";

    public static string? PostgresConnectionString => Nullable(Configuration["Postgres"]);

    public static bool UsePostgres => PostgresConnectionString is not null;

    public static (string HostName, int Port, string UserName, string Password, string VirtualHost)? RabbitMq
    {
        get
        {
            var host = Nullable(Configuration["RabbitMQ:HostName"]);
            return host is null
                ? null
                : (host,
                    int.TryParse(Configuration["RabbitMQ:Port"], out var port) ? port : 5672,
                    Configuration["RabbitMQ:UserName"] ?? "guest",
                    Configuration["RabbitMQ:Password"] ?? "guest",
                    Configuration["RabbitMQ:VirtualHost"] ?? "/");
        }
    }

    private static string? Nullable(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
