using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MSA.ApiGateway
{
    public class ConsulRefreshHostedService : IHostedService, IDisposable
    {
        private readonly ConsulProxyConfigProvider _provider;
        private readonly ILogger<ConsulRefreshHostedService> _logger;
        private Timer _timer;

        public ConsulRefreshHostedService(ConsulProxyConfigProvider provider, ILogger<ConsulRefreshHostedService> logger)
        {
            _provider = provider;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _timer = new Timer(async _ => await Refresh(), null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
            return Task.CompletedTask;
        }

        private async Task Refresh()
        {
            try
            {
                await _provider.RefreshAsync();
                _logger.LogDebug("Consul proxy config refreshed");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Consul refresh failed");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
