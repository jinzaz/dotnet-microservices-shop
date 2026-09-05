using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MSA.InventoryService.Application.FlashSales;
using MSA.InventoryService.Domain;

namespace MSA.FlashSaleTests;

[TestClass]
public sealed class CreateFlashSaleCommandValidatorTests
{
    [TestMethod]
    public void Validate_ExplicitTooManyBucketsForPurchaseLimit_ReturnsValidationFailure()
    {
        // Arrange
        var validator = new CreateFlashSaleCommandValidator(Options.Create(new FlashSaleOptions
        {
            MaximumBucketCount = 128
        }));
        var startsOnUtc = new DateTimeOffset(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
        var command = new CreateFlashSaleCommand(
            "SKU-FLASH-001",
            TotalQuantity: 10_003,
            StartsOnUtc: startsOnUtc,
            EndsOnUtc: startsOnUtc.AddMinutes(10),
            PurchaseLimit: 999,
            Strategy: FlashSaleStockStrategy.Bucketed,
            BucketCount: 32);

        // Act
        var result = validator.Validate(command);

        // Assert
        Assert.IsFalse(result.IsValid);
        Assert.HasCount(1, result.Errors);
        Assert.AreEqual(
            "Every stock bucket must hold at least one maximum-size purchase.",
            result.Errors[0].ErrorMessage);
    }
}
