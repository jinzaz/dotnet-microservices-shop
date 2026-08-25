using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace MSA.Infrastrucute.Abstractions;

public interface IDomainEventHandler<TDomainEvent>
    : INotificationHandler<TDomainEvent>
    where TDomainEvent : IDomainEvent
{
}
