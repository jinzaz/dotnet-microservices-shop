using MSA.Infrastrucute.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace MSA.Infrastrucute.Entities;

public abstract class AggregateRoot<TId>
    : Entity<TId>, IAggregateRoot
    where TId : notnull
{
}
