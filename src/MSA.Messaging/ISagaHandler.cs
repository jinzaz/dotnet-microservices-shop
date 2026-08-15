using System.Collections.Generic;
using System.Threading.Tasks;

namespace MSA.Libraries.Messaging
{
    // Generic saga handler interface. Implementations declare which event types they subscribe to
    // and contain the logic to handle incoming events for a saga instance.
    public interface ISagaHandler
    {
        string SagaType { get; }

        // Event type names this handler wants to receive
        IEnumerable<string> SubscribedEventTypes { get; }

        Task HandleAsync(IIntegrationEvent @event, SagaState state, ISagaRepository repository, IEventBus bus);
    }
}
