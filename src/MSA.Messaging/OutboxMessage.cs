using System;
using System.Collections.Generic;

namespace MSA.Libraries.Messaging
{
    public class OutboxMessage
    {
        public string Id { get; set; }
        public string MessageId { get; set; }
        public string EventType { get; set; }
        public string AggregateType { get; set; }
        public string Payload { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new();
        public bool Sent { get; set; }
        public int Retries { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
    }
}
