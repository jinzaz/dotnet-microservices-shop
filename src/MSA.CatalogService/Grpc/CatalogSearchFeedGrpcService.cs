using System.Globalization;
using Grpc.Core;
using MediatR;
using MSA.CatalogService.Application;
using MSA.Grpc.Contracts.Catalog;

namespace MSA.CatalogService.Grpc;

/// <summary>给搜索服务读商品：传输层只做契约映射，读取与可见性规则在 Application 里。</summary>
internal sealed class CatalogSearchFeedGrpcService(ISender sender) : CatalogSearchFeed.CatalogSearchFeedBase
{
    public override async Task<SearchProductsResponse> GetProducts(GetSearchProductsRequest request, ServerCallContext context)
    {
        var ids = request.ProductIds
            .Select(value => Guid.TryParse(value, out var id) ? id : throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid product id '{value}'.")))
            .ToArray();
        var products = await sender.Send(new GetSearchFeedProductsQuery(ids), context.CancellationToken);
        return ToResponse(products, nextAfterId: null);
    }

    public override async Task<SearchProductsResponse> ExportProducts(ExportSearchProductsRequest request, ServerCallContext context)
    {
        var page = await sender.Send(
            new ExportSearchFeedProductsQuery(
                OptionalId(request.AfterId, "after_id"),
                request.PageSize <= 0 ? 500 : request.PageSize,
                OptionalId(request.CategoryId, "category_id")),
            context.CancellationToken);
        return ToResponse(page.Products, page.NextAfterId);
    }

    private static Guid? OptionalId(string value, string field) =>
        string.IsNullOrEmpty(value) ? null
        : Guid.TryParse(value, out var id) ? id
        : throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid {field} '{value}'."));

    private static SearchProductsResponse ToResponse(IEnumerable<SearchFeedProductDto> products, Guid? nextAfterId)
    {
        var response = new SearchProductsResponse { NextAfterId = nextAfterId?.ToString() ?? string.Empty };
        response.Products.AddRange(products.Select(product =>
        {
            var message = new SearchProduct
            {
                Id = product.Id.ToString(),
                CategoryId = product.CategoryId.ToString(),
                CategoryName = product.CategoryName,
                Name = product.Name,
                Description = product.Description,
                Version = product.Version,
                Searchable = product.Searchable,
                EffectiveOnUtc = product.EffectiveOnUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                UpdatedOnUtc = product.UpdatedOnUtc.ToString("O", CultureInfo.InvariantCulture),
                ImageUrl = product.ImageUrl ?? string.Empty
            };
            message.Variants.AddRange(product.Variants.Select(variant => new SearchProductVariant
            {
                Sku = variant.Sku,
                NormalizedSku = variant.NormalizedSku,
                DisplayName = variant.DisplayName,
                AttributesJson = variant.AttributesJson,
                Barcode = variant.Barcode ?? string.Empty,
                Price = variant.Price.ToString(CultureInfo.InvariantCulture),
                Currency = variant.Currency,
                IsDefault = variant.IsDefault
            }));
            return message;
        }));
        return response;
    }
}
