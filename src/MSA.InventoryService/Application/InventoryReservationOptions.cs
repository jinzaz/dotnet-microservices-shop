namespace MSA.InventoryService.Application;

public sealed class InventoryReservationOptions
{
    public const string SectionName = "InventoryReservations";

    public TimeSpan RegularLifetime { get; set; } = TimeSpan.FromMinutes(20);
    public TimeSpan FlashSaleLifetime { get; set; } = TimeSpan.FromMinutes(10);
}
