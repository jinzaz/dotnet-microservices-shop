using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Infrastrucute.Mediator;
using MSA.PaymentService.Domain;
using MSA.PaymentService.Infrastructure;

namespace MSA.PaymentService.Application;

public sealed record PaymentStatusSummaryDto(string Status, string Currency, int Count, decimal Amount, decimal RefundedAmount);

public sealed record PaymentSummaryDto(
    DateTimeOffset FromUtc,
    int TotalCount,
    IReadOnlyList<PaymentStatusSummaryDto> Statuses,
    DateTimeOffset GeneratedOnUtc);

/// <summary>Payment outcomes created within the trailing window, grouped by status and currency.</summary>
public sealed record GetPaymentSummaryQuery(int Days) : IQuery<PaymentSummaryDto>;

internal sealed class GetPaymentSummaryHandler(PaymentDbContext db, TimeProvider clock)
    : IRequestHandler<GetPaymentSummaryQuery, PaymentSummaryDto>
{
    public async Task<PaymentSummaryDto> Handle(GetPaymentSummaryQuery request, CancellationToken cancellationToken)
    {
        if (request.Days is < 1 or > 31)
            throw new ArgumentOutOfRangeException(nameof(request), "Days must be between 1 and 31.");

        var now = clock.GetUtcNow();
        var fromUtc = now.AddDays(-request.Days);
        var rows = await db.Payments.AsNoTracking()
            .Where(payment => payment.CreatedOnUtc >= fromUtc)
            .GroupBy(payment => new { payment.Status, payment.Currency })
            .Select(group => new
            {
                group.Key.Status,
                group.Key.Currency,
                Count = group.Count(),
                Amount = group.Sum(payment => payment.Amount),
                RefundedAmount = group.Sum(payment => payment.RefundedAmount)
            })
            .ToListAsync(cancellationToken);

        var statuses = rows
            .OrderBy(row => row.Status)
            .ThenBy(row => row.Currency, StringComparer.Ordinal)
            .Select(row => new PaymentStatusSummaryDto(
                row.Status.ToString(), row.Currency, row.Count, row.Amount, row.RefundedAmount))
            .ToArray();
        return new PaymentSummaryDto(fromUtc, statuses.Sum(row => row.Count), statuses, now);
    }
}
