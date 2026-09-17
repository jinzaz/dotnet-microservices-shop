using System.Text.Json;
using MSA.EventBus.Abstractions;

namespace MSA.EventBusOutbox;

public static class OutboxStoreExtensions
{
    /// <summary>Serializes an integration event with its runtime type so the outbox processor can restore it.</summary>
    public static void AddIntegrationEvent(this IOutboxStore outbox, IntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var eventType = integrationEvent.GetType();
        outbox.Add(OutboxMessage.Create(
            eventType.AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, eventType)));
    }
}
