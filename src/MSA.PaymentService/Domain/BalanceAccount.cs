using MSA.Infrastrucute.Entities;

namespace MSA.PaymentService.Domain;

/// <summary>用户账户余额，主键即用户标识。余额只有人民币一种币种。</summary>
public sealed class BalanceAccount : AggregateRoot<string>
{
    public const string Currency = "CNY";

    private BalanceAccount()
    {
    }

    public static BalanceAccount Open(string userId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new BalanceAccount { Id = userId.Trim(), CreatedOnUtc = now, UpdatedOnUtc = now };
    }

    public decimal Balance { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public long Version { get; private set; }

    /// <summary>调用方必须先锁住账户行；余额任何时候都不能为负，流水按变更后余额记录。</summary>
    public decimal Change(decimal delta, DateTimeOffset now)
    {
        if (delta == 0 || decimal.Round(delta, 2) != delta)
            throw new BalanceRuleException("余额变动金额必须非零且最多两位小数。");
        if (Balance + delta < 0)
            throw new BalanceRuleException("账户余额不足。");

        Balance += delta;
        UpdatedOnUtc = now;
        Version++;
        return Balance;
    }
}

public sealed class BalanceEntry : Entity<Guid>
{
    private BalanceEntry()
    {
    }

    public string UserId { get; private set; } = string.Empty;
    public BalanceEntryType Type { get; private set; }
    public decimal Amount { get; private set; }
    public decimal BalanceAfter { get; private set; }
    public string BusinessKey { get; private set; } = string.Empty;
    public Guid? OrderId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public Guid? RefundId { get; private set; }
    public string? AfterSalesRequestNumber { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? OperatorId { get; private set; }
    public DateTimeOffset OccurredOnUtc { get; private set; }

    public const string WelcomeKey = "WELCOME";
    public static string PaymentKey(Guid paymentId) => $"PAYMENT:{paymentId:N}".ToUpperInvariant();
    public static string RefundKey(Guid refundId) => $"REFUND:{refundId:N}".ToUpperInvariant();
    public static string AdjustmentKey(string businessKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessKey);
        return $"ADJUST:{businessKey.Trim()}".ToUpperInvariant();
    }

    public static BalanceEntry Record(
        BalanceAccount account,
        BalanceEntryType type,
        decimal amount,
        decimal balanceAfter,
        string businessKey,
        string reason,
        DateTimeOffset now,
        Guid? orderId = null,
        Guid? paymentId = null,
        Guid? refundId = null,
        string? afterSalesRequestNumber = null,
        string? operatorId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var normalizedReason = reason.Trim();
        return new BalanceEntry
        {
            Id = Guid.NewGuid(),
            UserId = account.Id,
            Type = type,
            Amount = amount,
            BalanceAfter = balanceAfter,
            BusinessKey = businessKey,
            OrderId = orderId,
            PaymentId = paymentId,
            RefundId = refundId,
            AfterSalesRequestNumber = string.IsNullOrWhiteSpace(afterSalesRequestNumber) ? null : afterSalesRequestNumber.Trim(),
            Reason = normalizedReason[..Math.Min(normalizedReason.Length, 500)],
            OperatorId = string.IsNullOrWhiteSpace(operatorId) ? null : operatorId.Trim(),
            OccurredOnUtc = now
        };
    }
}

public enum BalanceEntryType
{
    Adjustment = 0,
    Payment = 1,
    Refund = 2
}

/// <summary>余额业务规则不满足（余额不足、金额非法）；消息原样返回给调用方。</summary>
public sealed class BalanceRuleException(string message) : InvalidOperationException(message);

/// <summary>同一个调整幂等键被用于不同的调整内容。</summary>
public sealed class BalanceIdempotencyConflictException(string message) : InvalidOperationException(message);
