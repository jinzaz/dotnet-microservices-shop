using System.Text.Json;

namespace MSA.CatalogService.Application;

/// <summary>Request-level limits mirrored from the catalog domain guards and decimal(18,2) columns.</summary>
internal static class CatalogValidation
{
    public const decimal MaximumPrice = 9_999_999_999_999_999.99m;
    public const string CurrencyPattern = "^[A-Za-z]{3}$";

    public static bool IsJsonObjectOrEmpty(string? value)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
