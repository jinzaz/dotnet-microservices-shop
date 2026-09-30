using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MSA.Grpc;

namespace MSA.Grpc.Contracts.Catalog;

/// <summary>目录对某个 SKU 此刻的权威判定。</summary>
public sealed record ConfirmedSkuPrice(
    string Sku,
    Guid ProductId,
    Guid VariantId,
    string ProductName,
    string VariantName,
    decimal Price,
    string Currency,
    bool IsActive,
    long Revision,
    Guid? CategoryId,
    string AttributesJson);

public sealed record PriceConfirmation(
    IReadOnlyDictionary<string, ConfirmedSkuPrice> Items,
    IReadOnlyList<string> UnknownSkus);

/// <summary>
/// 目录的实时价格权威。返回 null 表示「这次问不到」（下游不可用或超时），
/// 调用方据此自行决定是降级还是拒绝，而不是把异常当成业务结论。
/// 购物车与订单共用这一份适配：确认语义只有一处实现，映射错了不会两边各错一次。
/// </summary>
public interface ICatalogPriceAuthority
{
    Task<PriceConfirmation?> ConfirmAsync(IReadOnlyCollection<string> skus, CancellationToken cancellationToken);
}

internal sealed class CatalogPriceAuthorityGateway(
    CatalogPricing.CatalogPricingClient client,
    ILogger<CatalogPriceAuthorityGateway> logger)
    : MsaGrpcGateway<CatalogPriceAuthorityGateway>(logger), ICatalogPriceAuthority
{
    public Task<PriceConfirmation?> ConfirmAsync(
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken) =>
        CallAsync<PriceConfirmation>(nameof(ConfirmAsync), async () =>
        {
            if (skus.Count == 0)
                return new PriceConfirmation(new Dictionary<string, ConfirmedSkuPrice>(StringComparer.Ordinal), []);

            var request = new ConfirmPricesRequest();
            request.Skus.AddRange(skus);
            var response = await client.ConfirmPricesAsync(request, cancellationToken: cancellationToken);
            var items = response.Items.ToDictionary(
                item => item.Sku.Trim().ToUpperInvariant(),
                item => new ConfirmedSkuPrice(
                    item.Sku,
                    Guid.Parse(item.ProductId),
                    Guid.Parse(item.VariantId),
                    item.ProductName,
                    item.VariantName,
                    ParseAmount(item.Price),
                    item.Currency,
                    item.IsActive,
                    item.Revision,
                    Guid.TryParse(item.CategoryId, out var categoryId) ? categoryId : null,
                    string.IsNullOrWhiteSpace(item.AttributesJson) ? "{}" : item.AttributesJson),
                StringComparer.Ordinal);
            return new PriceConfirmation(items, response.UnknownSkus.ToArray());
        });
}

public static class CatalogPriceAuthorityRegistration
{
    /// <summary>注册目录价格权威（客户端 + 网关）。调用方只注入 <see cref="ICatalogPriceAuthority"/>。</summary>
    public static IServiceCollection AddMsaCatalogPriceAuthority(
        this IServiceCollection services,
        IConfiguration section)
    {
        services.AddMsaGrpcClient<CatalogPricing.CatalogPricingClient>(section);
        services.AddScoped<ICatalogPriceAuthority, CatalogPriceAuthorityGateway>();
        return services;
    }
}
