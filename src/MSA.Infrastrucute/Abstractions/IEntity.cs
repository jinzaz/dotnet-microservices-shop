using System;
using System.Collections.Generic;
using System.Text;

namespace MSA.Infrastrucute.Abstractions;

public interface IEntity
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    IReadOnlyList<IDomainEvent> DequeueDomainEvents();
    void ClearDomainEvents();
}
