using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace MSA.Certificates;

public sealed class CertificateStoreOptions
{
    public const string SectionName = "Certificates";

    /// <summary>使用方名，见 <see cref="CertificateConsumers"/>。</summary>
    public string? Consumer { get; set; }

    /// <summary>
    /// 多久查一次证书库有没有变化。新证书都提前排好激活时间（至少 10 分钟），晚一两分钟发现无所谓，
    /// 所以用轮询，不用 LISTEN/NOTIFY 或消息总线。
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>证书库连接。单独包一层，避免跟使用方自己可能注册的 NpgsqlDataSource 在 DI 里撞车。</summary>
internal sealed class CertificateDatabase(NpgsqlDataSource dataSource) : IDisposable
{
    public NpgsqlDataSource DataSource { get; } = dataSource;

    public void Dispose() => DataSource.Dispose();
}

/// <summary>
/// 只读访问 CertificateDb 的 pki.certificates（表结构归 MSA.CertificateService 的迁移管理，列名要跟它的 EF 映射一致）。
/// 只取本使用方、未吊销、未过期的行，私钥用本使用方的 KEK 解密。
/// </summary>
internal sealed class CertificateStoreLoader(CertificateDatabase database, CertificateKek kek)
{
    public async Task<string> ReadStampAsync(CancellationToken cancellationToken)
    {
        // 证书服务每次写入都会更新 updated_at（吊销也是更新），行数 + 最新更新时间就能判断要不要重新加载。
        await using var command = database.DataSource.CreateCommand(
            "SELECT count(*), max(updated_at) FROM pki.certificates WHERE consumer = $1");
        command.Parameters.Add(new NpgsqlParameter { Value = kek.Consumer });
        await using var reader = await ExecuteAsync(command, cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var updatedAt = reader.IsDBNull(1) ? 0 : reader.GetFieldValue<DateTimeOffset>(1).UtcTicks;
        return $"{reader.GetInt64(0)}:{updatedAt}";
    }

    public async Task<IReadOnlyList<StoredCertificate>> LoadAsync(CancellationToken cancellationToken)
    {
        await using var command = database.DataSource.CreateCommand(
            """
            SELECT id, purpose, slot, activate_at, not_after, certificate_pem, private_key, kek_id
            FROM pki.certificates
            WHERE consumer = $1 AND revoked_at IS NULL AND not_after > now()
            """);
        command.Parameters.Add(new NpgsqlParameter { Value = kek.Consumer });
        await using var reader = await ExecuteAsync(command, cancellationToken);

        var certificates = new List<StoredCertificate>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            var purpose = reader.GetString(1);
            if (CertificatePurposes.Find(purpose) is null)
                continue; // 新版本证书服务加的用途，这个版本用不上。

            var kekId = reader.GetString(7);
            if (kekId != kek.Id)
            {
                throw new InvalidOperationException(
                    $"Certificate {id} was encrypted with KEK {kekId}, but {CertificateKek.VariableName(kek.Consumer)} is KEK {kek.Id}.");
            }

            var pkcs8 = kek.Decrypt(reader.GetFieldValue<byte[]>(6), CertificateKek.CertificateContext(id, kek.Consumer, purpose));
            try
            {
                var chain = CertificateMaterial.ReadChain(reader.GetString(5));
                var certificate = CertificateMaterial.WithPrivateKey(chain[0], pkcs8, purpose);
                foreach (var item in chain)
                    item.Dispose();

                certificates.Add(new StoredCertificate(
                    id,
                    purpose,
                    reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3),
                    reader.GetFieldValue<DateTimeOffset>(4),
                    certificate));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pkcs8);
            }
        }

        return certificates;
    }

    private static async Task<NpgsqlDataReader> ExecuteAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await command.ExecuteReaderAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            throw new InvalidOperationException(
                "CertificateDb has not been migrated yet: apply the MSA.CertificateService migrations first.", exception);
        }
    }
}

/// <summary>启动时加载一次证书库（读不到就不启动），之后按 <see cref="CertificateStoreOptions.PollInterval"/> 轮询。</summary>
internal sealed partial class CertificateStoreRefresher(
    CertificateStore store,
    CertificateStoreLoader loader,
    IOptions<CertificateStoreOptions> options,
    ILogger<CertificateStoreRefresher> logger) : BackgroundService
{
    private const int StartupAttempts = 3;

    private string? _stamp;
    private string _state = string.Empty;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // 配置了证书库却读不到时宁可起不来，也不能悄悄退回环境变量里的旧证书——那样两边的密钥会对不上。
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await RefreshAsync(cancellationToken);
                break;
            }
            catch (NpgsqlException exception) when (exception.IsTransient && attempt < StartupAttempts)
            {
                LogStartupRetry(exception, attempt);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
            }
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 刷新失败不影响已加载的证书继续服务；下一轮再试。
                LogRefreshFailed(exception);
            }
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var stamp = await loader.ReadStampAsync(cancellationToken);
        if (stamp != _stamp)
        {
            var certificates = await loader.LoadAsync(cancellationToken);
            store.Publish(certificates);
            _stamp = stamp;
            _state = store.DescribeState();
            LogLoaded(certificates.Count, store.Consumer);
            return;
        }

        var state = store.DescribeState();
        if (state != _state)
        {
            _state = state;
            store.NotifyChanged();
            LogStageChanged(store.Consumer);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} certificates for consumer {Consumer} from the certificate store.")]
    private partial void LogLoaded(int count, string consumer);

    [LoggerMessage(Level = LogLevel.Information, Message = "A certificate of consumer {Consumer} became active or retired; rebuilding dependent options.")]
    private partial void LogStageChanged(string consumer);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The certificate store is unreachable (attempt {Attempt}); retrying.")]
    private partial void LogStartupRetry(Exception exception, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refreshing the certificate store failed; the previously loaded certificates stay in use.")]
    private partial void LogRefreshFailed(Exception exception);
}
