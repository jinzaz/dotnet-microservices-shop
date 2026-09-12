using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Infrastructure;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;

namespace MSA.FlashSaleTests;

[TestClass]
public sealed class FlashSalePersistenceModelTests
{
    [TestMethod]
    public void InventoryModel_FlashSaleReservation_HasUniqueCampaignUserIndex()
    {
        // Arrange
        using var context = CreateInventoryDbContext();

        // Act
        var index = FindIndex<InventoryReservation>(
            context,
            nameof(InventoryReservation.FlashSaleCampaignId),
            nameof(InventoryReservation.UserId));

        // Assert
        Assert.IsTrue(index.IsUnique);
        Assert.AreEqual("\"FlashSaleCampaignId\" IS NOT NULL", index.GetFilter());
    }

    [TestMethod]
    public void OrderModel_FlashSaleOrder_HasUniqueCampaignUserIndex()
    {
        // Arrange
        using var context = CreateOrderDbContext();

        // Act
        var index = FindIndex<Order>(
            context,
            nameof(Order.FlashSaleCampaignId),
            nameof(Order.UserId));

        // Assert
        Assert.IsTrue(index.IsUnique);
        Assert.AreEqual("\"FlashSaleCampaignId\" IS NOT NULL", index.GetFilter());
    }

    [TestMethod]
    public void OrderModel_Idempotency_HasUniqueUserKeyIndex()
    {
        // Arrange
        using var context = CreateOrderDbContext();

        // Act
        var index = FindIndex<Order>(
            context,
            nameof(Order.UserId),
            nameof(Order.IdempotencyKey));

        // Assert
        Assert.IsTrue(index.IsUnique);
        Assert.IsNull(index.GetFilter());
    }

    [TestMethod]
    public void InventoryModel_StockCounters_HaveNonNegativeCheckConstraints()
    {
        // Arrange
        using var context = CreateInventoryDbContext();
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;
        var entityType = GetRequiredEntityType<FlashSaleStockBucket>(designTimeModel);

        // Act
        var allocatedConstraint = entityType.GetCheckConstraints()
            .SingleOrDefault(constraint => constraint.Name == "CK_flash_sale_stock_buckets_allocated");
        var counterConstraint = entityType.GetCheckConstraints()
            .SingleOrDefault(constraint => constraint.Name == "CK_flash_sale_stock_buckets_counters");

        // Assert
        Assert.IsNotNull(allocatedConstraint);
        Assert.AreEqual("\"AllocatedQuantity\" >= 0", allocatedConstraint.Sql);
        Assert.IsNotNull(counterConstraint);
        Assert.AreEqual(
            "\"ReservedQuantity\" >= 0 AND \"SoldQuantity\" >= 0 AND \"ReleasedQuantity\" >= 0 AND \"ReservedQuantity\" + \"SoldQuantity\" + \"ReleasedQuantity\" <= \"AllocatedQuantity\"",
            counterConstraint.Sql);
    }

    [TestMethod]
    public void InventoryModel_CampaignBucketCount_RequiresEveryBucketToFitPurchaseLimit()
    {
        // Arrange
        using var context = CreateInventoryDbContext();
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;
        var entityType = GetRequiredEntityType<FlashSaleCampaign>(designTimeModel);

        // Act
        var constraint = entityType.GetCheckConstraints()
            .SingleOrDefault(check => check.Name == "CK_flash_sale_campaigns_bucket_count");

        // Assert
        Assert.IsNotNull(constraint);
        Assert.AreEqual(
            "\"BucketCount\" BETWEEN 1 AND 128 AND \"BucketCount\" * \"PurchaseLimit\" <= \"TotalQuantity\"",
            constraint.Sql);
    }

    private static InventoryDbContext CreateInventoryDbContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql("Host=localhost;Database=flash_sale_inventory_model_tests;Username=unused;Password=unused")
            .Options;
        return new InventoryDbContext(options, Mock.Of<IMediator>());
    }

    private static OrderDbContext CreateOrderDbContext()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql("Host=localhost;Database=flash_sale_order_model_tests;Username=unused;Password=unused")
            .Options;
        return new OrderDbContext(options, Mock.Of<IMediator>());
    }

    private static IReadOnlyIndex FindIndex<TEntity>(DbContext context, params string[] propertyNames)
        where TEntity : class
    {
        var entityType = GetRequiredEntityType<TEntity>(context.Model);
        var matches = entityType.GetIndexes()
            .Where(index => index.Properties.Select(property => property.Name).SequenceEqual(propertyNames))
            .ToArray();

        Assert.HasCount(1, matches);
        CollectionAssert.AreEqual(propertyNames, matches[0].Properties.Select(property => property.Name).ToArray());
        return matches[0];
    }

    private static IEntityType GetRequiredEntityType<TEntity>(IModel model)
        where TEntity : class
    {
        var entityType = model.FindEntityType(typeof(TEntity));
        Assert.IsNotNull(entityType);
        return entityType;
    }
}
