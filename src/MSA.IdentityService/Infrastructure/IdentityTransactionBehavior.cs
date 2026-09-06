using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Infrastrucute.Mediator;

namespace MSA.IdentityService.Infrastructure;

public sealed class IdentityTransactionBehavior<TRequest, TResponse>(
    ApplicationDbContext db,
    ILogger<IdentityTransactionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICommand || db.Database.CurrentTransaction is not null)
            return await next();

        var response = default(TResponse)!;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                response = await next();
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });

        logger.LogInformation("Committed identity business command {CommandName}", typeof(TRequest).Name);
        return response;
    }
}
