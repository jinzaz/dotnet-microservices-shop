using MSA.Infrastrucute.Entities;

namespace MSA.UserService.Domain;

public sealed class LoyaltyPointAccount : AggregateRoot<string>
{
    private LoyaltyPointAccount()
    {
    }

    private LoyaltyPointAccount(string userId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        Id = userId.Trim();
        CreatedOnUtc = now;
        UpdatedOnUtc = now;
    }

    public long Balance { get; private set; }
    public long LifetimeEarned { get; private set; }
    public long LifetimeSpent { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public long Version { get; private set; }

    public static LoyaltyPointAccount Open(string userId, DateTimeOffset now) => new(userId, now);

    public PointBalanceChange Adjust(long delta, DateTimeOffset now)
    {
        if (delta == 0)
            throw new ArgumentOutOfRangeException(nameof(delta), "Point delta cannot be zero.");

        // 余额、累计获得和累计消费必须在同一次聚合变更中推进，流水据此记录前后余额。
        long nextBalance;
        var nextLifetimeEarned = LifetimeEarned;
        var nextLifetimeSpent = LifetimeSpent;
        try
        {
            nextBalance = checked(Balance + delta);
            if (delta > 0)
                nextLifetimeEarned = checked(LifetimeEarned + delta);
            else
                nextLifetimeSpent = checked(LifetimeSpent + -delta);
        }
        catch (OverflowException)
        {
            throw new UserBusinessException("The point balance would overflow.");
        }

        if (nextBalance < 0)
            throw new UserBusinessException("The point balance cannot become negative.");

        var before = Balance;
        Balance = nextBalance;
        LifetimeEarned = nextLifetimeEarned;
        LifetimeSpent = nextLifetimeSpent;
        UpdatedOnUtc = now;
        Version++;
        return new PointBalanceChange(before, Balance);
    }
}

public readonly record struct PointBalanceChange(long BalanceBefore, long BalanceAfter);

public sealed class LoyaltyPointEntry : Entity<Guid>
{
    private LoyaltyPointEntry()
    {
    }

    public LoyaltyPointEntry(
        Guid id,
        string userId,
        string businessKey,
        long delta,
        long balanceBefore,
        long balanceAfter,
        string source,
        string reason,
        string operatorId,
        DateTimeOffset occurredOnUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Point entry id is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);
        if (delta == 0 || balanceBefore < 0 || balanceAfter < 0 || balanceAfter != balanceBefore + delta)
            throw new ArgumentException("The point balance change is invalid.", nameof(delta));

        Id = id;
        UserId = userId.Trim();
        BusinessKey = NormalizeBusinessKey(businessKey);
        Delta = delta;
        BalanceBefore = balanceBefore;
        BalanceAfter = balanceAfter;
        Source = Normalize(source, nameof(source), 64);
        Reason = Normalize(reason, nameof(reason), 500);
        OperatorId = Normalize(operatorId, nameof(operatorId), 200);
        OccurredOnUtc = occurredOnUtc;
    }

    public string UserId { get; private set; } = string.Empty;
    public string BusinessKey { get; private set; } = string.Empty;
    public long Delta { get; private set; }
    public long BalanceBefore { get; private set; }
    public long BalanceAfter { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string OperatorId { get; private set; } = string.Empty;
    public DateTimeOffset OccurredOnUtc { get; private set; }

    public bool Matches(long delta, string source, string reason) =>
        Delta == delta
        && string.Equals(Source, source.Trim(), StringComparison.Ordinal)
        && string.Equals(Reason, reason.Trim(), StringComparison.Ordinal);

    public static string NormalizeBusinessKey(string value) =>
        Normalize(value, nameof(value), 128).ToUpperInvariant();

    private static string Normalize(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"{parameterName} cannot exceed {maximumLength} characters.", parameterName);
        return normalized;
    }
}
