using Microsoft.Extensions.DependencyInjection.Extensions;
using MSA.Certificates;

namespace MSA.BFF.Common.BodyEncryption;

public static class BodyEncryptionServiceCollectionExtensions
{
    /// <summary>密钥来自证书库（要先调 AddMsaCertificates）；没有证书库时退回环境变量证书 / 开发环境临时密钥。</summary>
    public static IServiceCollection AddBffBodyEncryption(this IServiceCollection services)
    {
        services.TryAddSingleton(CertificateStore.Disabled);
        services.AddSingleton<BodyEncryptionKeyRing>();
        services.AddTransient<BodyEncryptionMiddleware>();
        return services;
    }

    /// <summary>要在 app.MapReverseProxy() 之前调用：中间件要在 YARP 转发前解密、在 YARP 写完响应后加密。</summary>
    public static WebApplication UseBffBodyEncryption(this WebApplication app)
    {
        app.UseMiddleware<BodyEncryptionMiddleware>();
        return app;
    }

    /// <summary>
    /// 浏览器拉服务端当前公钥（未压缩点格式，65 字节原始二进制），X-Msa-Kid 响应头是它的标识，后续请求带回来。
    /// 不允许缓存：密钥轮换后浏览器要能拿到新的。
    /// </summary>
    public static WebApplication MapBodyEncryptionPublicKeyEndpoint(this WebApplication app)
    {
        app.MapGet("/bff/security/public-key", (BodyEncryptionKeyRing keyRing, HttpResponse response) =>
            {
                var key = keyRing.Current;
                response.Headers[BodyEncryptionMiddleware.KeyIdHeader] = key.Kid;
                response.Headers.CacheControl = "no-store";
                return Results.Bytes(key.PublicKeyRaw, "application/octet-stream");
            })
            .WithMetadata(new SkipBodyEncryptionAttribute())
            .AllowAnonymous();
        return app;
    }
}
