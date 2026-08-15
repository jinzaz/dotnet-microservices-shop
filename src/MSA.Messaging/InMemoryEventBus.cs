using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MSA.Libraries.Messaging
{
    // Simple in-memory event bus for local development and Saga choreography.
    // Not intended for production where a real message broker should be used.
    public class InMemoryEventBus : IEventBus
    {
        private readonly ConcurrentDictionary<string, List<EventHandlerDelegate>> _handlers = new();

        public Task PublishAsync(IIntegrationEvent @event)
        {
            if (@event == null) return Task.CompletedTask;

            if (_handlers.TryGetValue(@event.EventType, out var handlers))
            {
                // Invoke handlers concurrently and do not fail the publisher when one handler fails.
                var tasks = new List<Task>();
                foreach (var h in handlers)
                {
                    try
                    {
                        tasks.Add(h(@event));
                    }
                    catch
                    {
                        // swallow to allow other handlers to run; handlers should catch/log as needed
                    }
                }

                return Task.WhenAll(tasks);
            }

            return Task.CompletedTask;
        }

        public void Subscribe(string eventType, EventHandlerDelegate handler)
        {
            if (string.IsNullOrWhiteSpace(eventType) || handler == null) return;

            var list = _handlers.GetOrAdd(eventType, _ => new List<EventHandlerDelegate>());
            lock (list)
            {
                list.Add(handler);
            }
        }

        public void Unsubscribe(string eventType, EventHandlerDelegate handler)
        {
            if (string.IsNullOrWhiteSpace(eventType) || handler == null) return;

            if (_handlers.TryGetValue(eventType, out var list))
            {
                lock (list)
                {
                    list.Remove(handler);
                }
            }
        }
    }
}
