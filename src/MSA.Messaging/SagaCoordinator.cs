using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MSA.Libraries.Messaging
{
    public class SagaCoordinator
    {
        private readonly IEventBus _bus;
        private readonly ISagaRepository _repo;
        private readonly ConcurrentDictionary<string, List<ISagaHandler>> _handlersByEvent = new();

        public SagaCoordinator(IEventBus bus, ISagaRepository repo)
        {
            _bus = bus;
            _repo = repo;
        }

        public void RegisterHandler(ISagaHandler handler)
        {
            if (handler == null) return;

            foreach (var evt in handler.SubscribedEventTypes ?? Enumerable.Empty<string>())
            {
                var list = _handlersByEvent.GetOrAdd(evt, _ => new List<ISagaHandler>());
                lock (list)
                {
                    list.Add(handler);
                }

                // subscribe to bus
                _bus.Subscribe(evt, async (e) => await OnEventReceivedAsync(evt, e));
            }
        }

        private async Task OnEventReceivedAsync(string eventType, IIntegrationEvent @event)
        {
            if (!_handlersByEvent.TryGetValue(eventType, out var handlers)) return;

            foreach (var handler in handlers)
            {
                try
                {
                    // determine correlation id from headers or fallback to event id
                    var correlationId = @event.Headers != null && @event.Headers.TryGetValue("CorrelationId", out var c) ? c : @event.Id;

                    var state = await _repo.GetByCorrelationIdAsync(handler.SagaType, correlationId);
                    if (state == null)
                    {
                        state = new SagaState
                        {
                            Id = Guid.NewGuid().ToString("D"),
                            SagaType = handler.SagaType,
                            CorrelationId = correlationId,
                            State = "Started",
                            Data = null,
                            Completed = false,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        await _repo.AddAsync(state);
                    }

                    await handler.HandleAsync(@event, state, _repo, _bus);

                    // persist potential changes
                    state.UpdatedAt = DateTime.UtcNow;
                    await _repo.UpdateAsync(state);
                }
                catch (Exception ex)
                {
                    // Minimal error handling: log to console. Consumers can implement more robust policies.
                    System.Console.WriteLine($"[SagaCoordinator] Handler {handler.SagaType} failed for event {eventType}: {ex.Message}");
                }
            }
        }
    }
}
