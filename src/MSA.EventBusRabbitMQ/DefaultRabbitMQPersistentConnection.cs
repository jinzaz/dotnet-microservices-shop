using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace MSA.EventBusRabbitMQ;

public sealed class DefaultRabbitMQPersistentConnection : IRabbitMQPersistentConnection
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILogger<DefaultRabbitMQPersistentConnection> _logger;
    private readonly int _retryCount;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private IConnection? _connection;
    private bool _disposed;

    public DefaultRabbitMQPersistentConnection(
        IConnectionFactory connectionFactory,
        ILogger<DefaultRabbitMQPersistentConnection> logger,
        int retryCount = 5)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        _retryCount = retryCount;
    }

    public bool IsConnected => _connection is { IsOpen: true } && !_disposed;

    public async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("当前没有可用的 RabbitMQ 连接来创建 Channel");

        var options = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);
        return await _connection!.CreateChannelAsync(options, cancellationToken);
    }

    public async Task<bool> TryConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected) return true;

            var policy = Policy
                .Handle<SocketException>()
                .Or<BrokerUnreachableException>()
                .WaitAndRetryAsync(_retryCount,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (ex, time) => _logger.LogWarning(ex,
                        "RabbitMQ 连接失败，{Timeout}s 后重试", time.TotalSeconds));

            await policy.ExecuteAsync(
                async ct => { _connection = await _connectionFactory.CreateConnectionAsync(ct); }, cancellationToken);

            if (IsConnected && _connection is not null)
            {
                _connection.ConnectionShutdownAsync += OnConnectionShutdownAsync;
                _connection.CallbackExceptionAsync += OnCallbackExceptionAsync;
                _connection.ConnectionBlockedAsync += OnConnectionBlockedAsync;

                _logger.LogInformation("RabbitMQ 持久连接已建立: {HostName}", _connection.Endpoint.HostName);
                return true;
            }

            _logger.LogCritical("无法建立 RabbitMQ 连接");
            return false;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private Task OnConnectionBlockedAsync(object? sender, ConnectionBlockedEventArgs e)
    {
        if (_disposed) return Task.CompletedTask;
        return TryConnectAsync();
    }

    private Task OnCallbackExceptionAsync(object? sender, CallbackExceptionEventArgs e)
    {
        if (_disposed) return Task.CompletedTask;
        return TryConnectAsync();
    }

    private Task OnConnectionShutdownAsync(object? sender, ShutdownEventArgs reason)
    {
        if (_disposed) return Task.CompletedTask;
        return TryConnectAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_connection != null)
        {
            try
            {
                await _connection.DisposeAsync();
            }
            catch (IOException)
            {

                throw;
            }
        }

        _connectionLock.Dispose();
    }
}
