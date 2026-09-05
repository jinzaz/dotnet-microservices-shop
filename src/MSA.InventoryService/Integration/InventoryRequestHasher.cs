using MSA.Contracts;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace MSA.InventoryService.Integration;

internal static class InventoryRequestHasher
{
    public static string Compute(OrderSubmittedIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // Line order is not business-significant. All values that affect either
        // reservation or the resulting payment request remain part of the hash.
        var canonicalItems = integrationEvent.Items
            .OrderBy(item => item.Sku, StringComparer.Ordinal)
            .ThenBy(item => item.UnitPrice)
            .ThenBy(item => item.Quantity)
            .Select(item => new CanonicalOrderItem(
                item.Sku.Trim().ToUpperInvariant(),
                item.Quantity,
                item.UnitPrice.ToString("G29", CultureInfo.InvariantCulture)))
            .ToArray();
        var canonicalRequest = new CanonicalInventoryRequest(
            integrationEvent.UserId.Trim(),
            integrationEvent.FlashSaleCampaignId,
            canonicalItems,
            integrationEvent.Amount.ToString("G29", CultureInfo.InvariantCulture),
            integrationEvent.Currency.Trim().ToUpperInvariant(),
            integrationEvent.PaymentMethod.Trim().ToUpperInvariant(),
            integrationEvent.PaymentDueOnUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var payload = JsonSerializer.SerializeToUtf8Bytes(canonicalRequest);
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private sealed record CanonicalOrderItem(string Sku, int Quantity, string UnitPrice);

    private sealed record CanonicalInventoryRequest(
        string UserId,
        Guid? FlashSaleCampaignId,
        IReadOnlyList<CanonicalOrderItem> Items,
        string Amount,
        string Currency,
        string PaymentMethod,
        string? PaymentDueOnUtc);
}
