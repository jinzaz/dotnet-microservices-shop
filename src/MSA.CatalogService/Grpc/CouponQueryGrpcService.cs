using Grpc.Core;
using MediatR;
using MSA.CatalogService.Application;
using MSA.Grpc.Contracts.Catalog;

namespace MSA.CatalogService.Grpc;

/// <summary>传输层适配：契约翻译成应用层查询，券的判定规则只在 Application 里有一份。</summary>
internal sealed class CouponQueryGrpcService(ISender sender) : CouponQuery.CouponQueryBase
{
    public override async Task<ConfirmCouponsResponse> ConfirmCoupons(
        ConfirmCouponsRequest request,
        ServerCallContext context)
    {
        var couponIds = request.CouponIds
            .Select(id => Guid.TryParse(id, out var couponId) ? couponId : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToArray();

        var confirmed = await sender.Send(
            new ConfirmCouponsQuery(
                couponIds,
                string.IsNullOrWhiteSpace(request.UserId) ? null : request.UserId),
            context.CancellationToken);

        var response = new ConfirmCouponsResponse();
        response.Items.AddRange(confirmed.Items.Select(item => new ConfirmedCoupon
        {
            CouponId = item.CouponId.ToString(),
            Usable = item.Usable,
            Reason = item.Reason ?? string.Empty,
            Status = item.Status,
            PromotionId = item.PromotionId.ToString()
        }));
        return response;
    }
}
