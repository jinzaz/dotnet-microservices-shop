using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MSA.Libraries.Messaging
{
    public class OutboxSenderHostedService : BackgroundService
    {
        private readonly IOutboxRepository _repo;
        private readonly IEventBus _bus;
        private readonly ILogger<OutboxSenderHostedService> _logger;

        public OutboxSenderHostedService(IOutboxRepository repo, IEventBus bus, ILogger<OutboxSenderHostedService> logger)
        {
            _repo = repo;
            _bus = bus;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var pending = await _repo.GetPendingAsync(50);
                    foreach (var msg in pending)
                    {
                        try
                        {
                            var integration = new IntegrationEvent(msg.EventType ?? msg.AggregateType ?? "unknown", msg.Payload, msg.Headers);
                            await _bus.PublishAsync(integration);
                            await _repo.MarkAsSentAsync(msg.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to publish outbox message {Id}", msg.Id);
                            // Optionally increment retry count in repo
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Outbox processing error");
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
