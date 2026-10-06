using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.Certificates;
using MSA.CertificateService.Application;
using MSA.CertificateService.Domain;
using MSA.CertificateService.Infrastructure;

namespace MSA.CertificateService.Acme;

public sealed class AcmeOptions
{
    public const string SectionName = "Acme";
    public const string HttpClientName = "acme";

    public string ProductionDirectory { get; set; } = "https://acme-v02.api.letsencrypt.org/directory";

    /// <summary>Let's Encrypt 测试环境：频率限制宽松、证书不受浏览器信任，先在这里跑通再切正式环境。</summary>
    public string StagingDirectory { get; set; } = "https://acme-staging-v02.api.letsencrypt.org/directory";

    /// <summary>DNS-over-HTTPS（JSON 格式）解析器，用来确认 TXT 记录已生效。默认阿里公共 DNS，国内外都能访问。</summary>
    public string DohResolver { get; set; } = "https://dns.alidns.com/resolve";

    public TimeSpan DnsPropagationTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>等 CA 完成验证、签发证书的最长时间。</summary>
    public TimeSpan ValidationTimeout { get; set; } = TimeSpan.FromMinutes(3);
}

/// <summary>
/// 为一个 ACME 订阅申请一张证书：按订阅的验证方式完成每个域名的授权（DNS-01 支持普通与通配符域名，
/// HTTP-01 只支持普通域名），用新生成的 P-256 密钥提交 CSR，下载证书链后作为该站点的当前 TLS 证书入库。
/// 验证用的 TXT 记录 / HTTP 文件不论成败都会清理。
/// </summary>
internal sealed partial class AcmeIssuer(
    CertificateDbContext db,
    CertificateRegistry registry,
    CertificateKekRing keks,
    IHttpClientFactory httpClientFactory,
    IEnumerable<IDnsChallengeProvider> dnsProviders,
    DnsPropagationChecker propagation,
    IOptions<AcmeOptions> options,
    TimeProvider time,
    ILogger<AcmeIssuer> logger)
{
    private const string AccountSecretKind = "acme-account";

    public async Task<ManagedCertificate> IssueAsync(AcmeSubscription subscription, string actor, CancellationToken cancellationToken)
    {
        var (accountKey, accountUrl) = await LoadAccountAsync(subscription, cancellationToken);
        using var accountKeyScope = accountKey;
        var client = new AcmeClient(httpClientFactory.CreateClient(AcmeOptions.HttpClientName), new Uri(subscription.DirectoryUrl), accountKey)
        {
            AccountUrl = accountUrl
        };
        if (client.AccountUrl is null)
            await RegisterAccountAsync(client, subscription, accountKey, cancellationToken);

        var (orderUrl, order) = await client.CreateOrderAsync(subscription.Domains, cancellationToken);
        var cleanup = new List<Func<Task>>();
        try
        {
            var pending = new List<(string AuthorizationUrl, AcmeChallenge Challenge)>();
            var txtRecords = new List<(string Name, string Value)>();
            foreach (var authorizationUrl in order.Authorizations)
            {
                var authorization = await client.GetAuthorizationAsync(authorizationUrl, cancellationToken);
                if (authorization.Status == "valid")
                    continue; // 同一账户近期验证过的域名，CA 会直接复用授权。

                var challenge = await PrepareChallengeAsync(client, subscription, authorization, txtRecords, cleanup, cancellationToken);
                pending.Add((authorizationUrl, challenge));
            }

            if (txtRecords.Count > 0)
                await propagation.WaitAsync(txtRecords, cancellationToken);

            foreach (var (_, challenge) in pending)
                await client.AnswerChallengeAsync(challenge.Url, cancellationToken);
            foreach (var (authorizationUrl, _) in pending)
                await client.WaitForAuthorizationAsync(authorizationUrl, options.Value.ValidationTimeout, cancellationToken);

            using var certificateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var certificateUrl = await client.FinalizeAsync(orderUrl, CreateSigningRequest(certificateKey, subscription.Domains), options.Value.ValidationTimeout, cancellationToken);
            var chainPem = await client.DownloadCertificateAsync(certificateUrl, cancellationToken);

            var chain = CertificateMaterial.ReadChain(chainPem);
            using var material = new CertificateMaterialSet(chain[0], chain.Skip(1).ToList(), certificateKey.ExportPkcs8PrivateKey());
            var certificate = await registry.AddAsync(
                new NewCertificate(
                    CertificateConsumers.Edge,
                    CertificatePurposes.TlsCode,
                    subscription.Slot,
                    $"{subscription.Slot} · ACME",
                    ActivateAt: null,
                    Exportable: true,
                    AutoRotate: false),
                material,
                CertificateSource.Acme,
                subscription.Id,
                actor,
                cancellationToken);
            LogIssued(subscription.Slot, certificate.NotAfter);
            return certificate;
        }
        finally
        {
            foreach (var step in cleanup)
            {
                try
                {
                    await step();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogCleanupFailed(exception, subscription.Slot);
                }
            }
        }
    }

    private async Task<AcmeChallenge> PrepareChallengeAsync(
        AcmeClient client,
        AcmeSubscription subscription,
        AcmeAuthorization authorization,
        List<(string Name, string Value)> txtRecords,
        List<Func<Task>> cleanup,
        CancellationToken cancellationToken)
    {
        var domain = authorization.Identifier.Value;
        if (subscription.ChallengeType == AcmeChallengeType.Dns01)
        {
            var challenge = authorization.Challenges.FirstOrDefault(item => item.Type == "dns-01")
                ?? throw new AcmeException($"The CA offers no dns-01 challenge for {domain}.");
            var provider = dnsProviders.FirstOrDefault(item => item.Name == subscription.DnsProvider && item.IsConfigured)
                ?? throw new AcmeException($"DNS provider '{subscription.DnsProvider}' is not configured.");

            // 通配符域名的授权标识是去掉 *. 的基础域名，TXT 记录同样放在 _acme-challenge.<基础域名>；
            // example.com 与 *.example.com 同时申请时同一个名字下会有两条值不同的 TXT，CA 任一匹配即可。
            var name = $"_acme-challenge.{domain}";
            var value = AcmeClient.DnsTxtValue(client.KeyAuthorization(challenge.Token));
            var handle = await provider.AddTxtRecordAsync(name, value, cancellationToken);
            cleanup.Add(() => provider.RemoveTxtRecordAsync(handle, CancellationToken.None));
            txtRecords.Add((name, value));
            return challenge;
        }

        if (authorization.Wildcard == true)
            throw new AcmeException($"Wildcard domain *.{domain} can only be validated with dns-01.");

        var httpChallenge = authorization.Challenges.FirstOrDefault(item => item.Type == "http-01")
            ?? throw new AcmeException($"The CA offers no http-01 challenge for {domain}.");
        db.AcmeHttpChallenges.Add(new AcmeHttpChallenge
        {
            Token = httpChallenge.Token,
            KeyAuthorization = client.KeyAuthorization(httpChallenge.Token),
            ExpiresAt = time.GetUtcNow().AddHours(1)
        });
        await db.SaveChangesAsync(cancellationToken);
        cleanup.Add(() => db.AcmeHttpChallenges.Where(item => item.Token == httpChallenge.Token).ExecuteDeleteAsync(CancellationToken.None));
        return httpChallenge;
    }

    /// <summary>CSR 的 SAN 包含全部域名；CN 用第一个域名（超过 64 个字符时留空，CA 只看 SAN）。</summary>
    internal static byte[] CreateSigningRequest(ECDsa key, IReadOnlyList<string> domains)
    {
        var subject = domains[0].Length <= 64 ? $"CN={domains[0]}" : string.Empty;
        var request = new CertificateRequest(new X500DistinguishedName(subject), key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        foreach (var domain in domains)
            names.AddDnsName(domain);
        request.CertificateExtensions.Add(names.Build());
        return request.CreateSigningRequest();
    }

    /// <summary>同一 CA、同一联系邮箱共用一个账户；第一次用时新建密钥，注册成功后才入库。</summary>
    private async Task<(ECDsa Key, string? AccountUrl)> LoadAccountAsync(AcmeSubscription subscription, CancellationToken cancellationToken)
    {
        var account = await db.AcmeAccounts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.DirectoryUrl == subscription.DirectoryUrl && item.ContactEmail == subscription.ContactEmail,
            cancellationToken);
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (account is null)
            return (key, null);

        var pkcs8 = keks.For(CertificateConsumers.Edge).Decrypt(account.PrivateKey, CertificateKek.SecretContext(AccountSecretKind, account.Id));
        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out _);
            return (key, account.AccountUrl);
        }
        catch
        {
            key.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }

    private async Task RegisterAccountAsync(AcmeClient client, AcmeSubscription subscription, ECDsa accountKey, CancellationToken cancellationToken)
    {
        var url = await client.RegisterAccountAsync(subscription.ContactEmail, cancellationToken);
        var kek = keks.For(CertificateConsumers.Edge);
        var id = Guid.CreateVersion7();
        db.AcmeAccounts.Add(new AcmeAccount
        {
            Id = id,
            DirectoryUrl = subscription.DirectoryUrl,
            ContactEmail = subscription.ContactEmail,
            AccountUrl = url,
            PrivateKey = kek.Encrypt(accountKey.ExportPkcs8PrivateKey(), CertificateKek.SecretContext(AccountSecretKind, id)),
            KekId = kek.Id,
            CreatedAt = time.GetUtcNow()
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Issued an ACME certificate for site {Slot}, valid until {NotAfter}.")]
    private partial void LogIssued(string slot, DateTimeOffset notAfter);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleaning up an ACME challenge for site {Slot} failed; remove the record by hand if it lingers.")]
    private partial void LogCleanupFailed(Exception exception, string slot);
}
