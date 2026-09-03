using System;
using System.Collections.Generic;
using System.Text;

namespace MSA.Infrastrucute.Abstractions;

public interface IUnitOfWork
{
    Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default);
}
