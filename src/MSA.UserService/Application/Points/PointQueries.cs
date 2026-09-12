using MediatR;
using MSA.Infrastrucute.Mediator;

namespace MSA.UserService.Application.Points;

public sealed record GetLoyaltyPointAccountQuery(string UserId) : IQuery<LoyaltyPointAccountResponse>;

public sealed record GetLoyaltyPointEntryQuery(
    string UserId,
    Guid EntryId) : IQuery<LoyaltyPointEntryResponse?>;

public sealed record ListLoyaltyPointEntriesQuery(
    string UserId,
    int Skip,
    int Take) : IQuery<IReadOnlyList<LoyaltyPointEntryResponse>>;

internal sealed class GetLoyaltyPointAccountQueryHandler(IUserReadStore readStore)
    : IRequestHandler<GetLoyaltyPointAccountQuery, LoyaltyPointAccountResponse>
{
    public Task<LoyaltyPointAccountResponse> Handle(
        GetLoyaltyPointAccountQuery request,
        CancellationToken cancellationToken) =>
        readStore.GetPointAccountAsync(request.UserId, cancellationToken);
}

internal sealed class GetLoyaltyPointEntryQueryHandler(IUserReadStore readStore)
    : IRequestHandler<GetLoyaltyPointEntryQuery, LoyaltyPointEntryResponse?>
{
    public Task<LoyaltyPointEntryResponse?> Handle(
        GetLoyaltyPointEntryQuery request,
        CancellationToken cancellationToken) =>
        readStore.GetPointEntryAsync(request.UserId, request.EntryId, cancellationToken);
}

internal sealed class ListLoyaltyPointEntriesQueryHandler(IUserReadStore readStore)
    : IRequestHandler<ListLoyaltyPointEntriesQuery, IReadOnlyList<LoyaltyPointEntryResponse>>
{
    public Task<IReadOnlyList<LoyaltyPointEntryResponse>> Handle(
        ListLoyaltyPointEntriesQuery request,
        CancellationToken cancellationToken) =>
        readStore.ListPointEntriesAsync(request.UserId, request.Skip, request.Take, cancellationToken);
}
