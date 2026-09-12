using MediatR;
using MSA.UserService.Domain;
using MSA.Infrastrucute.Mediator;

namespace MSA.UserService.Application.Points;

public sealed record AdjustLoyaltyPointsCommand(
    string UserId,
    long Delta,
    string BusinessKey,
    string Source,
    string Reason,
    string OperatorId) : ICommand<PointAdjustmentResult>;

internal sealed class AdjustLoyaltyPointsCommandHandler(
    ILoyaltyPointRepository points,
    IUserBusinessLock userLock,
    TimeProvider clock) : IRequestHandler<AdjustLoyaltyPointsCommand, PointAdjustmentResult>
{
    public async Task<PointAdjustmentResult> Handle(
        AdjustLoyaltyPointsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Delta is < -1_000_000_000 or > 1_000_000_000 || request.Delta == 0)
            throw new ArgumentOutOfRangeException(nameof(request.Delta), "Point delta must be between -1,000,000,000 and 1,000,000,000 and cannot be zero.");

        var businessKey = LoyaltyPointEntry.NormalizeBusinessKey(request.BusinessKey);
        await userLock.AcquireAsync(request.UserId, "points", cancellationToken);
        var existing = await points.GetEntryAsync(request.UserId, businessKey, cancellationToken);
        if (existing is not null)
        {
            if (!existing.Matches(request.Delta, request.Source, request.Reason))
                throw new UserIdempotencyConflictException("The business key is already bound to a different point adjustment.");
            return new PointAdjustmentResult(UserResponseMapper.ToResponse(existing), true);
        }

        var now = clock.GetUtcNow();
        var account = await points.GetAccountAsync(request.UserId, cancellationToken);
        if (account is null)
        {
            account = LoyaltyPointAccount.Open(request.UserId, now);
            points.AddAccount(account);
        }

        var balanceChange = account.Adjust(request.Delta, now);
        var entry = new LoyaltyPointEntry(
            Guid.NewGuid(),
            request.UserId,
            businessKey,
            request.Delta,
            balanceChange.BalanceBefore,
            balanceChange.BalanceAfter,
            request.Source,
            request.Reason,
            request.OperatorId,
            now);
        points.AddEntry(entry);
        return new PointAdjustmentResult(UserResponseMapper.ToResponse(entry), false);
    }
}
