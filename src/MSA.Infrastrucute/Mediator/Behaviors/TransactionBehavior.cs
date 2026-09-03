using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MSA.Infrastrucute.Mediator.Behaviors;

public sealed class TransactionBehavior<TRequest, TResponse>(MSADbContext dbContext, ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ICommand)
            return await next();
        if (dbContext.HasActiveTransaction)
            return await next();

        var requestName = typeof(TRequest).Name;
        try
        {
            TResponse response = default!;
            await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);
                using var scope = logger.BeginScope(new Dictionary<string, object> { ["TransactionId"] = transaction.TransactionId });
                logger.LogInformation("Beginning transaction for {RequestName}", requestName);
                response = await next();
                await dbContext.CommitTransactionAsync(transaction, cancellationToken);
                logger.LogInformation("Committed transaction for {RequestName}", requestName);
            });
            return response;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Transaction failed for {RequestName}", requestName);
            throw;
        }
    }
}
