using System.Collections;
using System.Data.Common;
using Microsoft.Extensions.Configuration;

namespace MSA.Common;

/// <summary>
/// 基础设施地址、口令与客户端密钥统一来自环境变量，本地与 Docker 用同一套变量名：
/// 本地开发由仓库根目录的 msa.env 提供（从内容根目录逐级向上查找，写进进程环境变量），Docker 由 compose 注入。
/// 已经设置的进程环境变量优先于 msa.env。
/// <para>
/// 各服务 appsettings.json 只保留属于自己的部分（库名、Redis 库号、队列名、ClientId 等），
/// 地址与口令要和这些部分拼在一起，所以合并进对应的配置键，业务代码照旧读 ConnectionStrings:Postgresql、RabbitMQ:HostName：
/// </para>
/// <list type="bullet">
/// <item>INFRA_POSTGRES_HOST / PORT / USER / PASSWORD → ConnectionStrings:Postgresql、DefaultConnection</item>
/// <item>INFRA_REDIS_HOST / PORT / PASSWORD → ConnectionStrings:Redis</item>
/// <item>INFRA_RABBITMQ_HOST / PORT / USER / PASSWORD / VHOST → RabbitMQ:*</item>
/// <item>INFRA_OSS_ENDPOINT / ACCESS_KEY / SECRET_KEY → ObjectStorage:*</item>
/// <item>MSA_SERVICE_AUTHORITY → ServiceAuthentication:Authority</item>
/// <item>MSA_OTLP_ENDPOINT → OpenTelemetry:OtlpEndpoint</item>
/// <item>MSA_GRPC_&lt;名称大写&gt;_ADDRESS → GrpcClients:&lt;名称&gt;:Address（按被调用的服务命名，例如 MSA_GRPC_CATALOG_ADDRESS）</item>
/// </list>
/// 只合并服务自己声明过的部分：没有 Redis 连接串的服务不会被塞进 Redis 配置。
/// 客户端密钥不进配置：用到的地方按 ClientId 直接调用 <see cref="ClientSecret"/> 读环境变量。
/// </summary>
public static class MsaEnvironment
{
    public const string FileName = "msa.env";

    public static IConfigurationBuilder AddMsaEnvironment(this IConfigurationBuilder builder, string? contentRoot = null)
    {
        var env = Read(contentRoot ?? Directory.GetCurrentDirectory());
        var current = builder as IConfiguration ?? builder.Build();
        var values = new Dictionary<string, string?>();

        foreach (var name in new[] { "Postgresql", "DefaultConnection" })
        {
            if (current.GetConnectionString(name) is { } connectionString)
                values[$"ConnectionStrings:{name}"] = Postgres(name, connectionString, env);
        }

        if (current.GetConnectionString("Redis") is { } redis)
            values["ConnectionStrings:Redis"] = Redis(redis, env);

        if (current.GetSection("RabbitMQ").Exists())
        {
            Map(values, env, "INFRA_RABBITMQ_HOST", "RabbitMQ:HostName");
            Map(values, env, "INFRA_RABBITMQ_PORT", "RabbitMQ:Port");
            Map(values, env, "INFRA_RABBITMQ_USER", "RabbitMQ:UserName");
            Map(values, env, "INFRA_RABBITMQ_PASSWORD", "RabbitMQ:Password");
            Map(values, env, "INFRA_RABBITMQ_VHOST", "RabbitMQ:VirtualHost");
        }

        if (current.GetSection("ObjectStorage").Exists())
        {
            Map(values, env, "INFRA_OSS_ENDPOINT", "ObjectStorage:Endpoint");
            Map(values, env, "INFRA_OSS_ACCESS_KEY", "ObjectStorage:AccessKey");
            Map(values, env, "INFRA_OSS_SECRET_KEY", "ObjectStorage:SecretKey");
        }

        if (current.GetSection("ServiceAuthentication").Exists())
            Map(values, env, "MSA_SERVICE_AUTHORITY", "ServiceAuthentication:Authority");

        Map(values, env, "MSA_OTLP_ENDPOINT", "OpenTelemetry:OtlpEndpoint");

        foreach (var client in current.GetSection("GrpcClients").GetChildren())
            Map(values, env, $"MSA_GRPC_{client.Key.ToUpperInvariant()}_ADDRESS", $"{client.Path}:Address");

        return builder.AddInMemoryCollection(values);
    }

    /// <summary>环境变量 MSA_CLIENT_SECRET_&lt;ClientId 大写&gt;：认证中心注册客户端与各服务取令牌读的是同一个变量。没有设置时返回 null。</summary>
    public static string? ClientSecret(string clientId) =>
        string.IsNullOrWhiteSpace(clientId)
            ? null
            : Environment.GetEnvironmentVariable(ClientSecretVariable(clientId)) is { Length: > 0 } secret ? secret : null;

    public static string ClientSecretVariable(string clientId) => $"MSA_CLIENT_SECRET_{clientId.ToUpperInvariant()}";

    private static string Postgres(string name, string configured, IReadOnlyDictionary<string, string> env)
    {
        var connection = new DbConnectionStringBuilder { ConnectionString = configured };
        Set(connection, "Host", env, "INFRA_POSTGRES_HOST");
        Set(connection, "Port", env, "INFRA_POSTGRES_PORT");
        Set(connection, "Username", env, "INFRA_POSTGRES_USER");
        Set(connection, "Password", env, "INFRA_POSTGRES_PASSWORD");
        if (!connection.ContainsKey("Host"))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{name} has no host. Set INFRA_POSTGRES_HOST (and PORT / USER / PASSWORD) " +
                $"in the environment or in {FileName} at the repository root.");
        }

        return connection.ConnectionString;
    }

    // appsettings 里只写 defaultDatabase=N 等选项；地址与口令来自环境变量。口令不能含逗号（StackExchange.Redis 的分隔符）。
    private static string Redis(string configured, IReadOnlyDictionary<string, string> env)
    {
        var options = configured
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (env.TryGetValue("INFRA_REDIS_HOST", out var host))
        {
            options.RemoveAll(option => !option.Contains('='));
            options.Insert(0, $"{host}:{env.GetValueOrDefault("INFRA_REDIS_PORT", "6379")}");
        }
        if (env.TryGetValue("INFRA_REDIS_PASSWORD", out var password))
        {
            options.RemoveAll(option => option.StartsWith("password=", StringComparison.OrdinalIgnoreCase));
            options.Add($"password={password}");
        }
        if (!options.Any(option => !option.Contains('=')))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:Redis has no endpoint. Set INFRA_REDIS_HOST (and PORT / PASSWORD) " +
                $"in the environment or in {FileName} at the repository root.");
        }

        return string.Join(',', options);
    }

    private static void Set(DbConnectionStringBuilder connection, string key, IReadOnlyDictionary<string, string> env, string variable)
    {
        if (env.TryGetValue(variable, out var value))
            connection[key] = value;
    }

    private static void Map(Dictionary<string, string?> values, IReadOnlyDictionary<string, string> env, string variable, string key)
    {
        if (env.TryGetValue(variable, out var value))
            values[key] = value;
    }

    /// <summary>把 msa.env 写进进程环境变量（已经设置的不覆盖），再返回全部非空的环境变量。</summary>
    internal static IReadOnlyDictionary<string, string> Read(string startDirectory)
    {
        if (Find(startDirectory) is { } file)
        {
            foreach (var line in File.ReadAllLines(file))
            {
                var text = line.Trim();
                var separator = text.IndexOf('=');
                if (text.Length is 0 || text[0] == '#' || separator <= 0)
                    continue;

                var name = text[..separator].Trim();
                var value = text[(separator + 1)..].Trim();
                if (value.Length >= 2 && value[0] is '\'' or '"' && value[^1] == value[0])
                    value = value[1..^1];
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
                    Environment.SetEnvironmentVariable(name, value);
            }
        }

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            if (variable.Value is string { Length: > 0 } value)
                env[(string)variable.Key] = value;
        }

        return env;
    }

    private static string? Find(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, FileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
