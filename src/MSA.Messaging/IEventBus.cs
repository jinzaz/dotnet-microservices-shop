using System;
using System.Threading.Tasks;

namespace MSA.Libraries.Messaging
{
    public delegate Task EventHandlerDelegate(IIntegrationEvent @event);

    public interface IEventBus
    {
        Task PublishAsync(IIntegrationEvent @event);

        // Subscribe a handler to a specific event type (EventType string)
        void Subscribe(string eventType, EventHandlerDelegate handler);

        // Unsubscribe a previously registered handler
        void Unsubscribe(string eventType, EventHandlerDelegate handler);
    }
}
