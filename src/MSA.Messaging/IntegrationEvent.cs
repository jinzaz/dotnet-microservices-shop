using System;

namespace MSA.Libraries.Messaging
{
    public class IntegrationEvent : IIntegrationEvent
    {
        public IntegrationEvent(string eventType, string payload, System.Collections.Generic.IDictionary<string, string> headers = null)
        {
            Id = Guid.NewGuid().ToString("D");
            EventType = eventType;
            Payload = payload;
            OccurredAt = DateTime.UtcNow;
            Headers = headers ?? new System.Collections.Generic.Dictionary<string, string>();
        }

        public string Id { get; }
        public string EventType { get; }
        public DateTime OccurredAt { get; }
        public string Payload { get; }
        public System.Collections.Generic.IDictionary<string, string> Headers { get; }
    }
}
