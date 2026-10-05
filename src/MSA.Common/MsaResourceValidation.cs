using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Validation;

namespace MSA.Common;

/// <summary>
/// 资源服务（含网关）校验 access token 时，把 iss claim 校验和拉取 JWKS 的网络地址拆开：
/// iss 必须等于认证中心对外的公网地址（跟令牌里签发的一致，浏览器/其它客户端认的也是这个），
/// 但验签只需要在 Docker 网络内部发生，没必要每次都经入口代理绕一圈出去再回来。
/// 两个键都是必填：appsettings.json 本地默认值和 Docker 的环境变量都要写，漏配直接启动失败，
/// 不悄悄退回成另一个地址——那样配置漏了也看不出来，等于白改。
/// </summary>
public static class MsaResourceValidation
{
    public static OpenIddictValidationBuilder SetMsaIssuer(this OpenIddictValidationBuilder builder, IConfiguration configuration)
    {
        var issuer = RequireUri(configuration, "Authentication:Issuer");
        builder.SetIssuer(issuer);

        // 不能用 SetConfiguration 手写静态配置：那样 OpenIddict 不会去拉 JWKS，所有令牌验签都失败。
        // 改为从内部地址拉 discovery，再把文档里的公网 JWKS 地址换成内部地址。
        var internalBase = new Uri(RequireUri(configuration, "Authentication:InternalAddress").AbsoluteUri.TrimEnd('/') + "/");
        builder.Configure(options =>
            options.ConfigurationEndpoint = new Uri(internalBase, ".well-known/openid-configuration"));
        builder.AddEventHandler<OpenIddictValidationEvents.HandleConfigurationResponseContext>(handler => handler
            .UseInlineHandler(context =>
            {
                context.Configuration.JsonWebKeySetUri = new Uri(internalBase, ".well-known/jwks");
                return default;
            })
            .SetOrder(int.MaxValue)
            .SetType(OpenIddictValidationHandlerType.Custom));

        return builder;
    }

    private static Uri RequireUri(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"{key} must be an absolute HTTP or HTTPS URI.");
        }

        return uri;
    }
}
