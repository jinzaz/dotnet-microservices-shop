using MSA.Infrastrucute.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace MSA.Infrastrucute.Events;

public abstract record DomainEvent : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
