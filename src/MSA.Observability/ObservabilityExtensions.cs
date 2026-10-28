using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MSA.Observability;

/// <summary>
/// 所有服务共用的一份可观测性配置：链路、指标、日志三类信号都走 OTLP，资源属性一致，
/// 换采集端或加一个数据源只改这里。
/// <para>导出端点：<c>OpenTelemetry:OtlpEndpoint</c>（gRPC，例如 <c>http://otel-collector:4317</c>）；
/// 不配置时交给标准环境变量 <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> / <c>OTEL_EXPORTER_OTLP_PROTOCOL</c>。</para>
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// 除 ASP.NET Core 与 HttpClient 之外要收集的链路源：
    /// Npgsql 自带的数据库调用、事件总线的发布/消费（RabbitMQTelemetry），以及项目自己以 <c>MSA.</c> 开头的源。
    /// gRPC 客户端调用走 HttpClient 管道，服务端走 ASP.NET Core 管道，已经被上面两个覆盖。
    /// </summary>
    private static readonly string[] TracingSources = ["Npgsql", "EventBusRabbitMQ", "MSA.*"];

    /// <summary>运行时（GC、线程池、异常）、EF Core、Npgsql 连接池这些 .NET 自带的指标。</summary>
    private static readonly string[] Meters = ["System.Runtime", "Microsoft.EntityFrameworkCore", "Npgsql", "MSA.*"];

    public static TBuilder AddMsaObservability<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var configuration = builder.Configuration;
        var serviceName = configuration["OpenTelemetry:ServiceName"] ?? builder.Environment.ApplicationName;
        var serviceVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: serviceVersion, serviceInstanceId: Environment.MachineName)
                .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    // 健康检查每几秒一次（YARP 主动探测、编排器存活检查），记下来只会淹没真正的请求。
                    options.Filter = context => !IsHealthCheck(context.Request.Path);
                    options.RecordException = true;
                })
                .AddHttpClientInstrumentation(options => options.RecordException = true)
                .AddSource(TracingSources))
            .WithMetrics(metrics => metrics
                // SDK 默认不记 exemplar；按当前链路记下 trace_id，Grafana 能从指标点跳到对应链路。
                .SetExemplarFilter(ExemplarFilterType.TraceBased)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(Meters));

        if (Uri.TryCreate(configuration["OpenTelemetry:OtlpEndpoint"], UriKind.Absolute, out var endpoint))
            telemetry.UseOtlpExporter(OtlpExportProtocol.Grpc, endpoint);
        else
            telemetry.UseOtlpExporter();

        return builder;
    }

    private static bool IsHealthCheck(PathString path) => path.StartsWithSegments("/health");
}
