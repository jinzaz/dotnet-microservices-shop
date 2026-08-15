using System;

namespace MSA.Libraries.Messaging
{
    public interface IIntegrationEvent
    {
        string Id { get; }
        string EventType { get; }
        DateTime OccurredAt { get; }
        string Payload { get; }
        System.Collections.Generic.IDictionary<string, string> Headers { get; }
    }
}
