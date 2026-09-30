using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MSA.Grpc;

public static class GrpcClientRegistration
{
    /// <summary>
    /// 注册服务自身的机器身份（client_credentials）。所有 gRPC 客户端共用它换取令牌。
    /// </summary>
    public static IServiceCollection AddMsaServiceAuthentication(
        this IServiceCollection services,
        IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        services.AddOptions<ServiceAuthenticationOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddTimeProvider();
        services.AddSingleton<IServiceTokenProvider, ClientCredentialsTokenProvider>();
        services.AddHttpClient(ClientCredentialsTokenProvider.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                var options = provider.GetRequiredService<IOptions<ServiceAuthenticationOptions>>().Value;
                return CreateHandler(options.AllowUntrustedCertificates);
            });
        return services;
    }

    /// <summary>
    /// 注册一个带鉴权、截止时间与重试的 gRPC 客户端。
    /// <typeparamref name="TClient"/> 是 protobuf 生成的客户端类型，调用方直接注入它。
    /// </summary>
    public static IHttpClientBuilder AddMsaGrpcClient<TClient>(
        this IServiceCollection services,
        IConfiguration section)
        where TClient : ClientBase<TClient>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        var name = typeof(TClient).DeclaringType?.Name ?? typeof(TClient).Name;
        services.AddOptions<GrpcClientOptions>(name)
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = section.Get<GrpcClientOptions>()
            ?? throw new InvalidOperationException($"gRPC client '{name}' is not configured.");

        return services
            .AddGrpcClient<TClient>(name, client =>
            {
                client.Address = new Uri(options.Address);
                // 默认截止时间统一在拦截器里补，未显式指定 deadline 的调用不会无限等待。
                client.Interceptors.Add(new DeadlineInterceptor(options.Deadline));
            })
            .ConfigureChannel(channel =>
            {
                // 只读调用可以安全重试：瞬时网络抖动与被重启的实例由此自动恢复。
                channel.ServiceConfig = new ServiceConfig
                {
                    MethodConfigs =
                    {
                        new MethodConfig
                        {
                            Names = { MethodName.Default },
                            RetryPolicy = new RetryPolicy
                            {
                                MaxAttempts = Math.Clamp(options.MaxAttempts, 1, 5),
                                InitialBackoff = options.InitialBackoff,
                                MaxBackoff = options.MaxBackoff,
                                BackoffMultiplier = 2,
                                RetryableStatusCodes = { StatusCode.Unavailable, StatusCode.DeadlineExceeded }
                            }
                        }
                    }
                };
            })
            .ConfigurePrimaryHttpMessageHandler(() => CreateHandler(options.AllowUntrustedCertificates))
            .AddHttpMessageHandler(provider => new ServiceTokenHandler(
                provider.GetRequiredService<IServiceTokenProvider>(),
                options.Scopes));
    }

    private static HttpClientHandler CreateHandler(bool allowUntrustedCertificates)
    {
        var handler = new HttpClientHandler();
        if (allowUntrustedCertificates)
        {
            // 仅用于本地自签证书；生产环境保持默认校验。
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        return handler;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(TimeProvider)))
            services.AddSingleton(TimeProvider.System);
    }
}

/// <summary>给没有显式设置 deadline 的调用补上默认截止时间。</summary>
internal sealed class DeadlineInterceptor(TimeSpan deadline) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation) =>
        continuation(request, context.Options.Deadline is null
            ? new ClientInterceptorContext<TRequest, TResponse>(
                context.Method,
                context.Host,
                context.Options.WithDeadline(DateTime.UtcNow.Add(deadline)))
            : context);
}
