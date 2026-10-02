using System.ComponentModel.DataAnnotations;
using MSA.Common;

namespace MSA.Grpc;

/// <summary>
/// 服务自身的机器身份，用于 OpenIddict 的 client_credentials 流程换取访问令牌。
/// 走的是现有的 scope 与 resource，不额外引入一套权限体系。
/// </summary>
public sealed class ServiceAuthenticationOptions
{
    public const string SectionName = "ServiceAuthentication";

    /// <summary>IdentityService 的地址，例如 https://localhost:10002/ 。</summary>
    [Required]
    public string Authority { get; set; } = string.Empty;

    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>只读环境变量 MSA_CLIENT_SECRET_&lt;ClientId 大写&gt;，不从配置绑定：appsettings 里写了也不生效。</summary>
    [Required(ErrorMessage = "Service client secret is missing: set MSA_CLIENT_SECRET_<ClientId upper case> in the environment or in msa.env.")]
    public string? ClientSecret => MsaEnvironment.ClientSecret(ClientId);

    /// <summary>提前多久刷新令牌，避免拿着马上就过期的令牌发起调用。</summary>
    public TimeSpan RefreshSkew { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>开发环境用自签证书时放行；生产必须为 false。</summary>
    public bool AllowUntrustedCertificates { get; set; }
}

/// <summary>单个 gRPC 客户端的连接、鉴权与容错配置。</summary>
public sealed class GrpcClientOptions
{
    /// <summary>目标服务地址，例如 https://localhost:10003 。</summary>
    [Required]
    public string Address { get; set; } = string.Empty;

    /// <summary>调用该服务需要的 scope，例如 ["catalog"]。令牌按 scope 组合分别缓存。</summary>
    public string[] Scopes { get; set; } = [];

    /// <summary>默认截止时间：同步调用必须有上限，否则一个慢下游会拖垮上游。</summary>
    public TimeSpan Deadline { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>可重试状态码上的最大尝试次数（含首次）。只对幂等的只读调用有意义。</summary>
    public int MaxAttempts { get; set; } = 3;

    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>开发环境用自签证书时放行；生产必须为 false。</summary>
    public bool AllowUntrustedCertificates { get; set; }
}
