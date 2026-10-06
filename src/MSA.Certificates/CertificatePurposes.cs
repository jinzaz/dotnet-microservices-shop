namespace MSA.Certificates;

/// <summary>
/// 证书的使用方。每个使用方一把 KEK（环境变量 MSA_CERT_KEK_&lt;使用方&gt;），只解得开自己名下的私钥：
/// 所有服务共用一个数据库账号，库里谁都读得到密文，隔离靠的是"拿不到别人的 KEK"。
/// </summary>
public static class CertificateConsumers
{
    public const string Identity = "identity";
    public const string BffWeb = "bff-web";
    public const string BffBm = "bff-bm";

    /// <summary>入口 nginx。它不读证书库：证书服务把当前 TLS 证书写成 PEM 文件交给它。</summary>
    public const string Edge = "edge";

    public static IReadOnlyList<string> All { get; } = [Identity, BffWeb, BffBm, Edge];

    public static bool IsKnown(string? consumer) => consumer is not null && All.Contains(consumer, StringComparer.Ordinal);
}

/// <summary>导入时允许的密钥类型。</summary>
[Flags]
public enum CertificateKeyTypes
{
    None = 0,
    /// <summary>RSA，至少 2048 位。</summary>
    Rsa = 1,
    EcP256 = 2,
    EcP384 = 4
}

/// <summary>
/// 一种证书用途及其轮换规则。同一个（使用方、用途、槽位）里的证书按激活时间依次替换：
/// 新证书先以"待生效"状态被所有实例加载（<see cref="ActivationLead"/>），到点成为当前证书；
/// 被替换的旧证书再保留 <see cref="Retention"/>，只用来验签 / 解密此前签发的令牌，之后彻底退役。
/// </summary>
public sealed record CertificatePurpose(
    string Code,
    string DisplayName,
    IReadOnlyList<string> Consumers,
    CertificateKeyTypes AcceptedKeys,
    CertificateKeyTypes GeneratedKey,
    TimeSpan ActivationLead,
    TimeSpan Retention,
    TimeSpan RotateAfter,
    TimeSpan Validity)
{
    /// <summary>TLS 证书来自 CA（Let's Encrypt 或导入），平台不自签。</summary>
    public bool CanGenerate => GeneratedKey != CertificateKeyTypes.None;

    public bool AllowsConsumer(string consumer) => Consumers.Contains(consumer, StringComparer.Ordinal);
}

public static class CertificatePurposes
{
    public const string DefaultSlot = "default";

    public const string OidcSigningCode = "oidc-signing";
    public const string OidcEncryptionCode = "oidc-encryption";
    public const string OidcClientCode = "oidc-client";
    public const string BodyEncryptionCode = "body-encryption";
    public const string TlsCode = "tls";

    /// <summary>
    /// 认证中心签名令牌。资源服务缓存 JWKS（IdentityModel 默认 12 小时刷新），新密钥至少提前 2 天出现在 JWKS 里；
    /// refresh token 也由它签名、最长 14 天，所以旧密钥保留 15 天。
    /// </summary>
    public static CertificatePurpose OidcSigning { get; } = new(
        OidcSigningCode, "认证中心令牌签名", [CertificateConsumers.Identity],
        CertificateKeyTypes.Rsa, CertificateKeyTypes.Rsa,
        ActivationLead: TimeSpan.FromDays(2), Retention: TimeSpan.FromDays(15),
        RotateAfter: TimeSpan.FromDays(90), Validity: TimeSpan.FromDays(365));

    /// <summary>认证中心加密授权码 / refresh token（RSA-OAEP）。加密密钥不对外发布，提前 10 分钟让各实例加载即可。</summary>
    public static CertificatePurpose OidcEncryption { get; } = new(
        OidcEncryptionCode, "认证中心令牌加密", [CertificateConsumers.Identity],
        CertificateKeyTypes.Rsa, CertificateKeyTypes.Rsa,
        ActivationLead: TimeSpan.FromMinutes(10), Retention: TimeSpan.FromDays(15),
        RotateAfter: TimeSpan.FromDays(90), Validity: TimeSpan.FromDays(365));

    /// <summary>BFF 作为 OIDC 客户端签名 + 加密自己的 state token（登录往返几分钟），旧密钥留 1 天足够。</summary>
    public static CertificatePurpose OidcClient { get; } = new(
        OidcClientCode, "BFF 登录状态令牌", [CertificateConsumers.BffWeb, CertificateConsumers.BffBm],
        CertificateKeyTypes.Rsa, CertificateKeyTypes.Rsa,
        ActivationLead: TimeSpan.FromMinutes(10), Retention: TimeSpan.FromDays(1),
        RotateAfter: TimeSpan.FromDays(90), Validity: TimeSpan.FromDays(365));

    /// <summary>浏览器 ⇄ BFF 请求体加密（ECDH P-256）。打开着的页面缓存了旧公钥，旧密钥留 1 天，过后前端按 kid 失效自动重拉。</summary>
    public static CertificatePurpose BodyEncryption { get; } = new(
        BodyEncryptionCode, "请求体加密", [CertificateConsumers.BffWeb, CertificateConsumers.BffBm],
        CertificateKeyTypes.EcP256, CertificateKeyTypes.EcP256,
        ActivationLead: TimeSpan.FromMinutes(10), Retention: TimeSpan.FromDays(1),
        RotateAfter: TimeSpan.FromDays(30), Validity: TimeSpan.FromDays(180));

    /// <summary>HTTPS 站点证书。槽位即站点名（部署目录名），新证书立即替换旧证书，没有重叠期。</summary>
    public static CertificatePurpose Tls { get; } = new(
        TlsCode, "HTTPS 站点证书", [CertificateConsumers.Edge],
        CertificateKeyTypes.Rsa | CertificateKeyTypes.EcP256 | CertificateKeyTypes.EcP384, CertificateKeyTypes.None,
        ActivationLead: TimeSpan.Zero, Retention: TimeSpan.Zero,
        RotateAfter: TimeSpan.Zero, Validity: TimeSpan.Zero);

    public static IReadOnlyList<CertificatePurpose> All { get; } = [OidcSigning, OidcEncryption, OidcClient, BodyEncryption, Tls];

    /// <summary>自动轮换除了按周期，还在证书剩余有效期不足这个值时提前换掉。</summary>
    public static TimeSpan ExpiryMargin { get; } = TimeSpan.FromDays(30);

    public static CertificatePurpose? Find(string? code) =>
        All.FirstOrDefault(purpose => string.Equals(purpose.Code, code, StringComparison.Ordinal));

    public static CertificatePurpose Get(string code) =>
        Find(code) ?? throw new ArgumentException($"Unknown certificate purpose '{code}'.", nameof(code));
}
