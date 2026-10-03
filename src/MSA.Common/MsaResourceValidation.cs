using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace MSA.Common;

/// <summary>
/// 资源服务（含网关）校验 access token 时，把 iss claim 校验和拉取 JWKS 的网络地址拆开：
/// iss 必须等于认证中心对外的公网地址（跟令牌里签发的一致，浏览器/其它客户端认的也是这个），
/// 但验签只需要在 Docker 网络内部发生，没必要每次都经入口代理绕一圈出去再回来。
/// Authentication:InternalAddress 不配置时退回 Issuer，本地开发（两者本来就是同一个 localhost 地址）行为不变。
/// </summary>
public static class MsaResourceValidation
{
    public static OpenIddictValidationBuilder SetMsaIssuer(this OpenIddictValidationBuilder builder, IConfiguration configuration)
    {
        var issuerValue = configuration["Authentication:Issuer"] ?? "https://localhost:10002/";
        if (!Uri.TryCreate(issuerValue, UriKind.Absolute, out var issuer) ||
            issuer.Scheme != Uri.UriSchemeHttp && issuer.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Authentication:Issuer must be an absolute HTTP or HTTPS URI.");
        }

        builder.SetIssuer(issuer);

        var internalValue = configuration["Authentication:InternalAddress"] ?? issuerValue;
        if (Uri.TryCreate(internalValue.TrimEnd('/') + "/", UriKind.Absolute, out var internalBase))
        {
            builder.SetConfiguration(new OpenIddictConfiguration
            {
                Issuer = issuer,
                JsonWebKeySetUri = new Uri(internalBase, ".well-known/jwks")
            });
        }

        return builder;
    }
}
