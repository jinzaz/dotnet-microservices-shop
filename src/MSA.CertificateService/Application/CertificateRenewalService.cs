using Microsoft.Extensions.Options;
using MSA.Certificates;
using MSA.CertificateService.Acme;

namespace MSA.CertificateService.Application;

public sealed class CertificateRenewalOptions
{
    public const string SectionName = "Renewal";

    /// <summary>
    /// 定时检查的间隔。检查本身幂等、很便宜：内部密钥提前 30 天轮换，ACME 证书用掉 2/3 寿命就续订，
    /// 每小时一次足够，失败也有数周的重试余地。
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>启动后多久跑第一轮，留时间给服务完全起来。</summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>后台"立即执行"与定时循环之间的信号。</summary>
public sealed class CertificateRenewalTrigger
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Request()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // 已经有一个待处理的请求，合并即可。
        }
    }

    internal Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}

public sealed record CertificateRenewalRun(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int Rotated,
    int AcmeRenewed,
    int AcmeFailed,
    int TlsDeployed,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

/// <summary>最近一次定时任务的结果与下次执行时间，给后台展示。</summary>
public sealed class CertificateRenewalStatus
{
    public CertificateRenewalRun? LastRun { get; internal set; }
    public DateTimeOffset? NextRunAt { get; internal set; }
}

/// <summary>
/// 证书自动续期的定时任务。每轮依次：内部密钥自动轮换 → ACME 证书续订 → 把当前 TLS 证书部署给 nginx → 检查
/// 快到期却没人管的证书并告警。每一步单独兜底，前一步失败不影响后面。
/// </summary>
internal sealed partial class CertificateRenewalService(
    IServiceScopeFactory scopeFactory,
    CertificateRenewalTrigger trigger,
    CertificateRenewalStatus status,
    IOptions<CertificateRenewalOptions> options,
    TimeProvider time,
    ILogger<CertificateRenewalService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        status.NextRunAt = time.GetUtcNow() + options.Value.StartupDelay;
        await trigger.WaitAsync(options.Value.StartupDelay, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            status.LastRun = await RunOnceAsync(stoppingToken);
            status.NextRunAt = time.GetUtcNow() + options.Value.Interval;
            await trigger.WaitAsync(options.Value.Interval, stoppingToken);
        }
    }

    internal async Task<CertificateRenewalRun> RunOnceAsync(CancellationToken cancellationToken)
    {
        var startedAt = time.GetUtcNow();
        var warnings = new List<string>();
        var errors = new List<string>();
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var rotated = await StepAsync("rotation", errors, async () =>
        {
            var result = await services.GetRequiredService<CertificateRegistry>().RotateDueAsync(cancellationToken);
            warnings.AddRange(result.Unattended);
            return result.Rotated;
        });

        AcmeRenewalResult? acme = null;
        await StepAsync("acme", errors, async () =>
        {
            acme = await services.GetRequiredService<AcmeSubscriptions>().RenewDueAsync(cancellationToken);
            return acme.Renewed;
        });

        var deployed = await StepAsync("tls-deploy", errors, () => services.GetRequiredService<TlsDeployer>().SyncAsync(cancellationToken));

        if (acme is not null)
        {
            await StepAsync("expiry-check", errors, async () =>
            {
                warnings.AddRange(await UnmanagedTlsExpiringAsync(services.GetRequiredService<CertificateRegistry>(), acme.ManagedSlots, cancellationToken));
                return 0;
            });
        }

        foreach (var warning in warnings)
            LogNeedsAttention(warning);

        return new CertificateRenewalRun(startedAt, time.GetUtcNow(), rotated, acme?.Renewed ?? 0, acme?.Failed ?? 0, deployed, warnings, errors);
    }

    /// <summary>没有 ACME 托管（导入的）TLS 证书没法自动续，快到期时只能告警。</summary>
    private async Task<IEnumerable<string>> UnmanagedTlsExpiringAsync(CertificateRegistry registry, IReadOnlySet<string> managedSlots, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return (await registry.ListAsync(CertificateConsumers.Edge, CertificatePurposes.TlsCode, includeInactive: false, cancellationToken))
            .Where(certificate => certificate.Stage == CertificateStage.Active &&
                                  !managedSlots.Contains(certificate.Slot) &&
                                  certificate.NotAfter - now <= CertificatePurposes.ExpiryMargin)
            .Select(certificate => $"TLS {certificate.Slot}（{certificate.NotAfter:yyyy-MM-dd} 到期，没有 ACME 托管）");
    }

    private async Task<int> StepAsync(string step, List<string> errors, Func<Task<int>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.Add($"{step}: {exception.Message}");
            LogStepFailed(exception, step);
            return 0;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Certificate needs attention: {Warning}")]
    private partial void LogNeedsAttention(string warning);

    [LoggerMessage(Level = LogLevel.Error, Message = "Certificate renewal step {Step} failed.")]
    private partial void LogStepFailed(Exception exception, string step);
}
