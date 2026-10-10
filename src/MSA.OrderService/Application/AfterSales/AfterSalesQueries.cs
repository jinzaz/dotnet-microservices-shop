using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.Infrastrucute.Mediator;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;

namespace MSA.OrderService.Application.AfterSales;

/// <param name="RestockedQuantity">Back-office only; null for customers and before the return is disposed.</param>
public sealed record AfterSalesLineDto(string Sku, string ProductName, string VariantName, int Quantity, decimal RefundAmount, int? RestockedQuantity);

public sealed record AfterSalesHistoryDto(AfterSalesStatus Status, string? Note, string Actor, DateTimeOffset OccurredOnUtc);

public sealed record AfterSalesShipmentDto(string? CarrierCode, string CarrierName, string TrackingNumber, DateTimeOffset ShippedOnUtc);

/// <summary>Warehouse-side facts that only the back office sees.</summary>
public sealed record AfterSalesWarehouseDto(
    ReplacementStockStatus ReplacementStock,
    string? ReplacementStockFailure,
    ReturnDisposition ReturnDisposition,
    string? DispositionNote,
    string? DisposedBy,
    DateTimeOffset? DisposedOnUtc);

/// <param name="DeadlineOnUtc">When the current status is handled automatically if nobody acts; null when nothing is automatic.</param>
public sealed record AfterSalesDto(
    Guid Id,
    string RequestNumber,
    Guid OrderId,
    string OrderNumber,
    string UserId,
    AfterSalesType Type,
    AfterSalesStatus Status,
    AfterSalesReason ReasonCode,
    string Description,
    IReadOnlyList<AfterSalesLineDto> Lines,
    decimal RefundAmount,
    string Currency,
    IReadOnlyList<string> EvidenceUrls,
    string? ReturnAddress,
    AfterSalesShipmentDto? ReturnShipment,
    AfterSalesShipmentDto? ReplacementShipment,
    string? RejectionReason,
    string? RefundFailureReason,
    DateTimeOffset? DeadlineOnUtc,
    AfterSalesWarehouseDto? Warehouse,
    IReadOnlyList<AfterSalesHistoryDto> History,
    DateTimeOffset CreatedOnUtc,
    DateTimeOffset UpdatedOnUtc);

public sealed record AfterSalesSummaryDto(
    Guid Id,
    string RequestNumber,
    Guid OrderId,
    string OrderNumber,
    string UserId,
    AfterSalesType Type,
    AfterSalesStatus Status,
    decimal RefundAmount,
    string Currency,
    IReadOnlyList<string> ProductNames,
    ReplacementStockStatus ReplacementStock,
    ReturnDisposition ReturnDisposition,
    DateTimeOffset? DeadlineOnUtc,
    DateTimeOffset CreatedOnUtc,
    DateTimeOffset UpdatedOnUtc);

/// <param name="UserId">Customer scope; null for back-office reads.</param>
public sealed record GetAfterSalesQuery(Guid Id, string? UserId) : IQuery<AfterSalesDto?>;

/// <param name="Disposition">Back-office queue filter, e.g. returns waiting to be restocked.</param>
/// <param name="ReplacementStock">Back-office queue filter, e.g. replacements short of stock.</param>
public sealed record ListAfterSalesQuery(
    string? UserId,
    Guid? OrderId,
    AfterSalesStatus? Status,
    AfterSalesType? Type,
    string? Search,
    int Skip,
    int Take,
    ReturnDisposition? Disposition = null,
    ReplacementStockStatus? ReplacementStock = null) : IQuery<IReadOnlyList<AfterSalesSummaryDto>>;

internal sealed class GetAfterSalesQueryHandler(
    OrderDbContext db,
    AfterSalesEvidenceStorage evidence,
    IOptions<AfterSalesOptions> options)
    : IRequestHandler<GetAfterSalesQuery, AfterSalesDto?>
{
    public async Task<AfterSalesDto?> Handle(GetAfterSalesQuery query, CancellationToken cancellationToken)
    {
        var request = await db.AfterSalesRequests
            .AsNoTracking()
            .Include(value => value.Lines)
            .Include(value => value.History)
            .AsSplitQuery()
            .SingleOrDefaultAsync(value => value.Id == query.Id && (query.UserId == null || value.UserId == query.UserId), cancellationToken);
        if (request is null)
            return null;

        var customer = query.UserId is not null;
        return new AfterSalesDto(
            request.Id,
            request.RequestNumber,
            request.OrderId,
            request.OrderNumber,
            request.UserId,
            request.Type,
            request.Status,
            request.Reason,
            request.Description ?? string.Empty,
            request.Lines
                .Select(line => new AfterSalesLineDto(line.Sku, line.ProductName, line.VariantName, line.Quantity, line.RefundAmount, customer ? null : line.RestockedQuantity))
                .ToArray(),
            request.RefundAmount,
            request.Currency,
            // 私有桶对象只给短期签名地址，页面过期后重新拉详情即可。
            request.EvidenceObjectKeys.Select(evidence.CreateReadUrl).ToArray(),
            request.ReturnAddress,
            request.ReturnTrackingNumber is null
                ? null
                : new AfterSalesShipmentDto(null, request.ReturnCarrierName!, request.ReturnTrackingNumber, request.ReturnShippedOnUtc!.Value),
            request.ReplacementTrackingNumber is null
                ? null
                : new AfterSalesShipmentDto(request.ReplacementCarrierCode, request.ReplacementCarrierName!, request.ReplacementTrackingNumber, request.ReplacementShippedOnUtc!.Value),
            request.RejectionReason,
            // 渠道失败原因可能带技术细节，只给后台看；买家看到的是「退款失败，商家处理中」。
            customer ? null : request.RefundFailureReason,
            options.Value.DeadlineFor(request.Status, request.Type, request.StatusChangedOnUtc),
            customer
                ? null
                : new AfterSalesWarehouseDto(
                    request.ReplacementStock,
                    request.ReplacementStockFailure,
                    request.ReturnDisposition,
                    request.DispositionNote,
                    request.DisposedBy,
                    request.DisposedOnUtc),
            request.History
                .OrderBy(history => history.OccurredOnUtc)
                .Select(history => new AfterSalesHistoryDto(
                    history.ToStatus,
                    history.Note,
                    customer ? CustomerFacingActor(history.Actor, request.UserId) : history.Actor,
                    history.OccurredOnUtc))
                .ToArray(),
            request.CreatedOnUtc,
            request.UpdatedOnUtc);
    }

    // 不向买家暴露后台操作员账号。
    private static string CustomerFacingActor(string actor, string userId) =>
        actor == userId ? "买家" : actor == "system" ? "系统" : "商家";
}

internal sealed class ListAfterSalesQueryHandler(OrderDbContext db, IOptions<AfterSalesOptions> options)
    : IRequestHandler<ListAfterSalesQuery, IReadOnlyList<AfterSalesSummaryDto>>
{
    public async Task<IReadOnlyList<AfterSalesSummaryDto>> Handle(ListAfterSalesQuery query, CancellationToken cancellationToken)
    {
        if (query.Skip < 0 || query.Take is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(query), "Skip must be non-negative and take must be between 1 and 200.");

        var requests = db.AfterSalesRequests.AsNoTracking();
        if (query.UserId is not null)
            requests = requests.Where(value => value.UserId == query.UserId);
        if (query.OrderId is Guid orderId)
            requests = requests.Where(value => value.OrderId == orderId);
        if (query.Status is AfterSalesStatus status)
            requests = requests.Where(value => value.Status == status);
        if (query.Type is AfterSalesType type)
            requests = requests.Where(value => value.Type == type);
        if (query.Disposition is ReturnDisposition disposition)
            requests = requests.Where(value => value.ReturnDisposition == disposition);
        if (query.ReplacementStock is ReplacementStockStatus stock)
            requests = requests.Where(value => value.ReplacementStock == stock);
        // 搜索仅对后台开放，与订单列表一致。
        if (query.UserId is null && !string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            var lowered = search.ToLowerInvariant();
            var upper = search.ToUpperInvariant();
            requests = requests.Where(value =>
                value.RequestNumber.ToLower() == lowered
                || value.OrderNumber.ToLower() == lowered
                || value.ReturnTrackingNumber == upper
                || value.UserId == search);
        }

        var rows = await requests
            .OrderByDescending(value => value.CreatedOnUtc)
            .ThenByDescending(value => value.Id)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(value => new
            {
                value.Id,
                value.RequestNumber,
                value.OrderId,
                value.OrderNumber,
                value.UserId,
                value.Type,
                value.Status,
                value.RefundAmount,
                value.Currency,
                ProductNames = value.Lines.Select(line => line.ProductName).ToList(),
                value.ReplacementStock,
                value.ReturnDisposition,
                value.StatusChangedOnUtc,
                value.CreatedOnUtc,
                value.UpdatedOnUtc
            })
            .ToArrayAsync(cancellationToken);
        return rows
            .Select(row => new AfterSalesSummaryDto(
                row.Id,
                row.RequestNumber,
                row.OrderId,
                row.OrderNumber,
                row.UserId,
                row.Type,
                row.Status,
                row.RefundAmount,
                row.Currency,
                row.ProductNames,
                row.ReplacementStock,
                row.ReturnDisposition,
                options.Value.DeadlineFor(row.Status, row.Type, row.StatusChangedOnUtc),
                row.CreatedOnUtc,
                row.UpdatedOnUtc))
            .ToArray();
    }
}
