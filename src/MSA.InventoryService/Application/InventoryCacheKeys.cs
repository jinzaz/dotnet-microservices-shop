namespace MSA.InventoryService.Application;

/// <summary>Single source of the inventory read-model cache keys used by readers and invalidators.</summary>
public static class InventoryCacheKeys
{
    public static string Stock(string normalizedSku) =>
        $"inventory:availability:{normalizedSku}";

    public static string Allocation(Guid allocationId) =>
        $"inventory:allocation:{allocationId:N}";
}
