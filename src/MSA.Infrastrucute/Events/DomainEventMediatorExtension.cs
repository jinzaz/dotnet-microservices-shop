using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Infrastrucute.Abstractions;
using MSA.Infrastrucute.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace MSA.Infrastrucute.Events;

public static class DomainEventMediatorExtension
{
    public static async Task DispatchDomainEventsAsync(this IMediator mediator, MSADbContext ctx, CancellationToken cancellationToken = default)
    {
        var domainEntities = ctx.ChangeTracker
            .Entries<IEntity>()
            .Where(x => x.Entity.DomainEvents != null && x.Entity.DomainEvents.Any());

        var domainEvents = domainEntities
            .SelectMany(x => x.Entity.DomainEvents)
            .ToList();

        domainEntities.ToList()
            .ForEach(entity => entity.Entity.ClearDomainEvents());

        foreach (var domainEvent in domainEvents)
            await mediator.Publish(domainEvent, cancellationToken);
    }
}
