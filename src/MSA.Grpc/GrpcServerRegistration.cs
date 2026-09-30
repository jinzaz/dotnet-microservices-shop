using FluentValidation;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MSA.Grpc;

public static class GrpcServerRegistration
{
    /// <summary>
    /// 注册 gRPC 服务端，并统一接上异常映射拦截器。
    /// 业务代码照常抛领域异常，客户端收到的是稳定的 gRPC 状态码。
    /// </summary>
    public static IServiceCollection AddMsaGrpcServer(this IServiceCollection services, bool detailedErrors = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = detailedErrors;
            options.Interceptors.Add<MsaGrpcExceptionInterceptor>();
        });
        return services;
    }

    /// <summary>
    /// 挂载一个 gRPC 服务并套用授权策略。策略名就是服务现有的 API 策略（例如 "catalog-api"），
    /// 因此服务间调用与前台调用共用同一套 scope / resource，不需要第二套权限模型。
    /// </summary>
    public static GrpcServiceEndpointConventionBuilder MapMsaGrpcService<TService>(
        this IEndpointRouteBuilder endpoints,
        string authorizationPolicy)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationPolicy);
        var builder = endpoints.MapGrpcService<TService>();
        builder.RequireAuthorization(authorizationPolicy);
        return builder;
    }
}

/// <summary>
/// 领域异常 → gRPC 状态码的统一映射。没有它，客户端只能看到 Unknown，
/// 也就无法区分「参数错」「没找到」和「真的挂了」这三种完全不同的处理方式。
/// </summary>
internal sealed class MsaGrpcExceptionInterceptor(ILogger<MsaGrpcExceptionInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (RpcException)
        {
            // 业务已经明确表达了状态码，原样抛给调用方。
            throw;
        }
        catch (Exception exception)
        {
            var status = Translate(exception);
            if (status.StatusCode is StatusCode.Internal or StatusCode.Unknown)
                logger.LogError(exception, "gRPC call {Method} failed.", context.Method);
            else
                logger.LogWarning("gRPC call {Method} rejected: {Detail}", context.Method, status.Detail);
            throw new RpcException(status);
        }
    }

    internal static Status Translate(Exception exception) => exception switch
    {
        ValidationException or ArgumentException =>
            new Status(StatusCode.InvalidArgument, exception.Message),
        KeyNotFoundException =>
            new Status(StatusCode.NotFound, exception.Message),
        UnauthorizedAccessException =>
            new Status(StatusCode.PermissionDenied, "The caller is not allowed to perform this operation."),
        OperationCanceledException =>
            new Status(StatusCode.Cancelled, "The call was cancelled."),
        TimeoutException =>
            new Status(StatusCode.DeadlineExceeded, "The call timed out."),
        InvalidOperationException =>
            new Status(StatusCode.FailedPrecondition, exception.Message),
        _ => new Status(StatusCode.Internal, "The service failed to handle the call.")
    };
}
