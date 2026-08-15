using System;

namespace MSA.Libraries.Messaging
{
    public class SagaState
    {
        public string Id { get; set; }
        public string SagaType { get; set; }
        public string CorrelationId { get; set; }
        public string State { get; set; }
        public string Data { get; set; }
        public bool Completed { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
