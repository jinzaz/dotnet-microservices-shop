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

    private static bool? _postgresReachable;

    /// <summary>
    /// 配置了 Postgres 并且真的连得上时才用它。探测一次即可：服务器临时不可达（断网、环境没起）时
    /// 自动回退到 SQLite，整套用例仍然能跑，只是少了 SKIP LOCKED 这类 PostgreSQL 专有语义。
    /// </summary>
    public static bool UsePostgres
    {
        get
        {
            if (PostgresConnectionString is null)
                return false;
            if (_postgresReachable is { } reachable)
                return reachable;

            try
            {
                using var connection = new Npgsql.NpgsqlConnection(
                    new Npgsql.NpgsqlConnectionStringBuilder(PostgresConnectionString) { Timeout = 5 }.ConnectionString);
                connection.Open();
                _postgresReachable = true;
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[tests] Postgres 不可用，本次回退到 SQLite：{exception.Message}");
                _postgresReachable = false;
            }

            return _postgresReachable.Value;
        }
    }

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

    private static bool? _rabbitReachable;

    /// <summary>broker 不可达时把用例标记为 Inconclusive，而不是让整套测试变红。</summary>
    public static bool RabbitMqReachable
    {
        get
        {
            if (RabbitMq is not { } broker)
                return false;
            if (_rabbitReachable is { } reachable)
                return reachable;
            try
            {
                using var probe = new System.Net.Sockets.TcpClient();
                _rabbitReachable = probe.ConnectAsync(broker.HostName, broker.Port).Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                _rabbitReachable = false;
            }
            return _rabbitReachable.Value;
        }
    }

    /// <summary>测试专用的逻辑库（15），每个用例再用随机键前缀隔开，用完即删。</summary>
    public static string? RedisConnectionString => Nullable(Configuration["Redis"]);

    private static bool? _redisReachable;

    public static bool RedisReachable
    {
        get
        {
            if (RedisConnectionString is null)
                return false;
            if (_redisReachable is { } reachable)
                return reachable;
            try
            {
                var options = StackExchange.Redis.ConfigurationOptions.Parse(RedisConnectionString);
                options.ConnectTimeout = 5000;
                using var probe = StackExchange.Redis.ConnectionMultiplexer.Connect(options);
                _redisReachable = probe.IsConnected;
            }
            catch (Exception)
            {
                _redisReachable = false;
            }
            return _redisReachable.Value;
        }
    }

    private static string? Nullable(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
