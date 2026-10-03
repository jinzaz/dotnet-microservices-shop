using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.WebEncoders;
using MSA.Observability;

namespace MSA.Common;

/// <summary>
/// 11 个服务（资源服务、网关、认证中心、两个 BFF）启动时都一样的那部分：读 msa.env/环境变量、
/// 接 OTLP、统一异常/状态码响应、健康检查、中文不被转成 \uXXXX。各服务自己的部分
/// （OpenIddict scope、数据库、消息总线等）仍然留在各自的 Program.cs 里，不往这塞。
/// </summary>
public static class MsaServiceDefaults
{
    public static WebApplicationBuilder AddMsaServiceDefaults(this WebApplicationBuilder builder)
    {
        // 基础设施地址、OTLP 与 gRPC 下游地址、客户端密钥来自环境变量或仓库根目录的 msa.env，见 MsaEnvironment。
        builder.Configuration.AddMsaEnvironment(builder.Environment.ContentRootPath);
        builder.AddMsaObservability();

        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();

        // HtmlEncoder/JavaScriptEncoder 默认只放行 Basic Latin，中文等非 ASCII 字符会被转成
        // \uXXXX（JSON）或 &#xXXXX;（Razor 视图）。放宽到全部 Unicode 不影响 XSS 防护——
        // <、>、&、'、" 这些真正危险的字符始终会被转义，不受允许范围设置影响。
        builder.Services.Configure<WebEncoderOptions>(options =>
            options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
        builder.Services.Configure<JsonOptions>(options =>
            options.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All));
        builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
            options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All));

        return builder;
    }

    /// <summary>服务自己有 UseForwardedHeaders() 时，要调在它后面——HTTPS 重定向开关得看纠正过的 scheme。</summary>
    public static WebApplication UseMsaServiceDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        // TLS 终止在 nginx，容器只开 http://+:8080，默认不需要本地重定向；
        // 真要在应用层也做 HTTPS 重定向时，配置 Hosting:UseHttpsRedirection=true 打开。
        if (app.Configuration.GetValue("Hosting:UseHttpsRedirection", false))
            app.UseHttpsRedirection();

        app.MapHealthChecks("/health");

        return app;
    }
}
