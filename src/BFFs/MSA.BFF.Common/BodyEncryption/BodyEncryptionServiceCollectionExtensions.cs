namespace MSA.BFF.Common.BodyEncryption;

public static class BodyEncryptionServiceCollectionExtensions
{
    public static IServiceCollection AddBffBodyEncryption(this IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddSingleton(_ => new BodyEncryptionServerKey(BodyEncryptionCertificateEnvironment.Load(environment)));
        services.AddTransient<BodyEncryptionMiddleware>();
        return services;
    }

    /// <summary>要在 app.MapReverseProxy() 之前调用：中间件要在 YARP 转发前解密、在 YARP 写完响应后加密。</summary>
    public static WebApplication UseBffBodyEncryption(this WebApplication app)
    {
        app.UseMiddleware<BodyEncryptionMiddleware>();
        return app;
    }

    /// <summary>浏览器启动时拉一次服务端公钥，用来加密后续请求。未压缩点格式，65 字节原始二进制。</summary>
    public static WebApplication MapBodyEncryptionPublicKeyEndpoint(this WebApplication app)
    {
        app.MapGet("/bff/security/public-key", (BodyEncryptionServerKey key) =>
                Results.Bytes(key.PublicKeyRaw, "application/octet-stream"))
            .WithMetadata(new SkipBodyEncryptionAttribute())
            .AllowAnonymous();
        return app;
    }
}
