using System.Globalization;
using Grpc.Core;
using MediatR;
using MSA.CatalogService.Application;
using MSA.Grpc.Contracts.Catalog;

namespace MSA.CatalogService.Grpc;

/// <summary>
/// gRPC 只是传输层：请求转成查询交给应用层，结果映射回契约。
/// 业务规则一律留在 Application 里，REST 与 gRPC 共用同一套实现。
/// </summary>
internal sealed class CatalogPricingGrpcService(ISender sender) : CatalogPricing.CatalogPricingBase
{
    public override async Task<ConfirmPricesResponse> ConfirmPrices(
        ConfirmPricesRequest request,
        ServerCallContext context)
    {
        var confirmed = await sender.Send(
            new ConfirmSkuPricesQuery(request.Skus),
            context.CancellationToken);

        var response = new ConfirmPricesResponse();
        response.Items.AddRange(confirmed.Items.Select(item => new ConfirmedSku
        {
            Sku = item.Sku,
            ProductId = item.ProductId.ToString(),
            VariantId = item.VariantId.ToString(),
            ProductName = item.ProductName,
            VariantName = item.VariantName,
            // 金额按不变文化格式化，跨服务传递不受本地化影响。
            Price = item.Price.ToString(CultureInfo.InvariantCulture),
            Currency = item.Currency,
            IsActive = item.IsActive,
            Revision = item.Revision,
            CategoryId = item.CategoryId.ToString(),
            AttributesJson = item.AttributesJson
        }));
        response.UnknownSkus.AddRange(confirmed.UnknownSkus);
        return response;
    }
}
