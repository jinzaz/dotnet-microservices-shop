//using Microsoft.VisualStudio.TestTools.UnitTesting;
//using MSA.InventoryService.Domain;

//[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

//namespace MSA.FlashSaleTests;

//[TestClass]
//public sealed class FlashSaleCampaignTests
//{
//    private static readonly DateTimeOffset StartsOnUtc = new(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
//    private static readonly DateTimeOffset EndsOnUtc = StartsOnUtc.AddMinutes(10);
//    private static readonly DateTimeOffset CreatedOnUtc = StartsOnUtc.AddMinutes(-5);

//    [TestMethod]
//    public void Create_SmallStock_UsesSingleBucketAndPreservesQuantity()
//    {
//        // Arrange
//        var campaignId = Guid.NewGuid();

//        // Act
//        var campaign = CreateCampaign(
//            campaignId,
//            totalQuantity: 3,
//            FlashSaleStockStrategy.SingleRow,
//            bucketCount: 1);

//        // Assert
//        Assert.AreEqual(campaignId, campaign.Id);
//        Assert.AreEqual(FlashSaleStockStrategy.SingleRow, campaign.Strategy);
//        Assert.AreEqual(1, campaign.BucketCount);
//        Assert.HasCount(1, campaign.StockBuckets);

//        var bucket = campaign.StockBuckets.Single();
//        Assert.AreEqual(campaignId, bucket.CampaignId);
//        Assert.AreEqual(0, bucket.BucketNumber);
//        Assert.AreEqual(3, bucket.AllocatedQuantity);
//        Assert.AreEqual(3, bucket.AvailableQuantity);
//        Assert.AreEqual(0, bucket.ReservedQuantity);
//        Assert.AreEqual(0, bucket.SoldQuantity);
//        Assert.AreEqual(0, bucket.ReleasedQuantity);
//    }

//    [TestMethod]
//    public void TryReserve_ExactRemainingQuantity_SellsOutWithoutGoingNegative()
//    {
//        // Arrange
//        var bucket = CreateSingleBucketCampaign(totalQuantity: 3).StockBuckets.Single();

//        // Act
//        var reserved = bucket.TryReserve(3);
//        var additionalReservation = bucket.TryReserve(1);

//        // Assert
//        Assert.IsTrue(reserved);
//        Assert.IsFalse(additionalReservation);
//        Assert.AreEqual(3, bucket.ReservedQuantity);
//        Assert.AreEqual(0, bucket.AvailableQuantity);
//        Assert.AreEqual(0, bucket.SoldQuantity);
//        Assert.AreEqual(0, bucket.ReleasedQuantity);
//        Assert.AreEqual(1L, bucket.Version);
//    }

//    [TestMethod]
//    public void Create_BucketedStock_DistributesEveryUnitAcrossBuckets()
//    {
//        // Arrange
//        const int totalQuantity = 10_003;
//        const int bucketCount = 32;

//        // Act
//        var campaign = CreateCampaign(
//            Guid.NewGuid(),
//            totalQuantity,
//            FlashSaleStockStrategy.Bucketed,
//            bucketCount);
//        var buckets = campaign.StockBuckets.OrderBy(bucket => bucket.BucketNumber).ToArray();

//        // Assert
//        Assert.AreEqual(FlashSaleStockStrategy.Bucketed, campaign.Strategy);
//        Assert.AreEqual(bucketCount, campaign.BucketCount);
//        Assert.HasCount(bucketCount, buckets);
//        Assert.AreEqual(totalQuantity, buckets.Sum(bucket => bucket.AllocatedQuantity));
//        CollectionAssert.AreEqual(Enumerable.Range(0, bucketCount).ToArray(), buckets.Select(bucket => bucket.BucketNumber).ToArray());
//        Assert.HasCount(19, buckets.Where(bucket => bucket.AllocatedQuantity == 313));
//        Assert.HasCount(13, buckets.Where(bucket => bucket.AllocatedQuantity == 312));
//        Assert.AreEqual(totalQuantity, buckets.Sum(bucket => bucket.AvailableQuantity));
//    }

//    [TestMethod]
//    public void Create_BucketedStockWithBucketBelowPurchaseLimit_ThrowsArgumentOutOfRangeException()
//    {
//        // Arrange
//        const int totalQuantity = 10;
//        const int purchaseLimit = 4;
//        const int bucketCount = 3;

//        // Act
//        var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
//            FlashSaleCampaign.Create(
//                Guid.NewGuid(),
//                "SKU-FLASH-001",
//                totalQuantity,
//                StartsOnUtc,
//                EndsOnUtc,
//                purchaseLimit,
//                FlashSaleStockStrategy.Bucketed,
//                bucketCount,
//                CreatedOnUtc));

//        // Assert
//        Assert.AreEqual("bucketCount", exception.ParamName);
//    }

//    [TestMethod]
//    public void TryReserve_MoreThanRemaining_IsRejectedWithoutChangingCounters()
//    {
//        // Arrange
//        var bucket = CreateSingleBucketCampaign(totalQuantity: 5).StockBuckets.Single();
//        Assert.IsTrue(bucket.TryReserve(3));

//        // Act
//        var reserved = bucket.TryReserve(3);

//        // Assert
//        Assert.IsFalse(reserved);
//        Assert.AreEqual(3, bucket.ReservedQuantity);
//        Assert.AreEqual(2, bucket.AvailableQuantity);
//        Assert.AreEqual(0, bucket.SoldQuantity);
//        Assert.AreEqual(0, bucket.ReleasedQuantity);
//        Assert.AreEqual(1L, bucket.Version);
//    }

//    [TestMethod]
//    public void Commit_ReservedQuantity_MovesReservationToSoldExactlyOnce()
//    {
//        // Arrange
//        const int campaignQuantity = 10;
//        const int orderQuantity = 4;
//        var campaign = CreateSingleBucketCampaign(campaignQuantity);
//        var bucket = campaign.StockBuckets.Single();
//        var stockItem = CreateCampaignStock(campaignQuantity);
//        var reservation = CreateReservation(campaign, orderQuantity);
//        Assert.IsTrue(bucket.TryReserve(orderQuantity));

//        // Act
//        var firstTransition = reservation.TryCommit();
//        if (firstTransition)
//        {
//            bucket.Commit(orderQuantity);
//            stockItem.Commit(orderQuantity);
//        }

//        var secondTransition = reservation.TryCommit();
//        if (secondTransition)
//        {
//            bucket.Commit(orderQuantity);
//            stockItem.Commit(orderQuantity);
//        }

//        // Assert
//        Assert.IsTrue(firstTransition);
//        Assert.IsFalse(secondTransition);
//        Assert.AreEqual(ReservationStatus.Committed, reservation.Status);
//        Assert.AreEqual(1L, reservation.Version);
//        Assert.AreEqual(0, bucket.ReservedQuantity);
//        Assert.AreEqual(orderQuantity, bucket.SoldQuantity);
//        Assert.AreEqual(0, bucket.ReleasedQuantity);
//        Assert.AreEqual(campaignQuantity - orderQuantity, bucket.AvailableQuantity);
//        Assert.AreEqual(2L, bucket.Version);
//        Assert.AreEqual(20 - orderQuantity, stockItem.OnHand);
//        Assert.AreEqual(campaignQuantity - orderQuantity, stockItem.Reserved);
//        Assert.AreEqual(10, stockItem.Available);
//        Assert.AreEqual(2L, stockItem.Version);
//    }

//    [TestMethod]
//    public void Release_OpenCampaign_ReturnsQuantityToCampaignExactlyOnce()
//    {
//        // Arrange
//        const int campaignQuantity = 10;
//        const int orderQuantity = 4;
//        var campaign = CreateSingleBucketCampaign(campaignQuantity);
//        var bucket = campaign.StockBuckets.Single();
//        var stockItem = CreateCampaignStock(campaignQuantity);
//        var reservation = CreateReservation(campaign, orderQuantity);
//        Assert.IsTrue(bucket.TryReserve(orderQuantity));

//        // Act
//        var firstTransition = reservation.TryRelease();
//        if (firstTransition)
//            bucket.Release(orderQuantity, campaignClosed: false);

//        var secondTransition = reservation.TryRelease();
//        if (secondTransition)
//            bucket.Release(orderQuantity, campaignClosed: false);

//        // Assert
//        Assert.IsTrue(firstTransition);
//        Assert.IsFalse(secondTransition);
//        Assert.AreEqual(ReservationStatus.Released, reservation.Status);
//        Assert.AreEqual(1L, reservation.Version);
//        Assert.AreEqual(0, bucket.ReservedQuantity);
//        Assert.AreEqual(0, bucket.SoldQuantity);
//        Assert.AreEqual(0, bucket.ReleasedQuantity);
//        Assert.AreEqual(campaignQuantity, bucket.AvailableQuantity);
//        Assert.AreEqual(2L, bucket.Version);
//        Assert.AreEqual(20, stockItem.OnHand);
//        Assert.AreEqual(campaignQuantity, stockItem.Reserved);
//        Assert.AreEqual(10, stockItem.Available);
//        Assert.AreEqual(1L, stockItem.Version);
//    }

//    [TestMethod]
//    public void Close_ActiveCampaign_ReleasesOnlyImmediatelyAvailableQuantity()
//    {
//        // Arrange
//        const int campaignQuantity = 10;
//        const int outstandingReservationQuantity = 4;
//        var campaign = CreateSingleBucketCampaign(campaignQuantity);
//        var bucket = campaign.StockBuckets.Single();
//        var stockItem = CreateCampaignStock(campaignQuantity);
//        Assert.IsTrue(bucket.TryReserve(outstandingReservationQuantity));
//        var closedOnUtc = EndsOnUtc.AddSeconds(1);

//        // Act
//        var releasedQuantity = campaign.Close(closedOnUtc);
//        stockItem.Release(releasedQuantity);
//        var duplicateReleaseQuantity = campaign.Close(closedOnUtc.AddMinutes(1));

//        // Assert
//        Assert.AreEqual(campaignQuantity - outstandingReservationQuantity, releasedQuantity);
//        Assert.AreEqual(0, duplicateReleaseQuantity);
//        Assert.AreEqual(FlashSaleCampaignStatus.Closed, campaign.Status);
//        Assert.AreEqual(closedOnUtc, campaign.ClosedOnUtc);
//        Assert.AreEqual(1L, campaign.Version);
//        Assert.AreEqual(outstandingReservationQuantity, bucket.ReservedQuantity);
//        Assert.AreEqual(0, bucket.SoldQuantity);
//        Assert.AreEqual(campaignQuantity - outstandingReservationQuantity, bucket.ReleasedQuantity);
//        Assert.AreEqual(0, bucket.AvailableQuantity);
//        Assert.AreEqual(2L, bucket.Version);
//        Assert.AreEqual(20, stockItem.OnHand);
//        Assert.AreEqual(outstandingReservationQuantity, stockItem.Reserved);
//        Assert.AreEqual(20 - outstandingReservationQuantity, stockItem.Available);
//        Assert.AreEqual(2L, stockItem.Version);
//    }

//    [TestMethod]
//    public void Release_ClosedCampaign_ReleasesLateReservationToGlobalStock()
//    {
//        // Arrange
//        const int campaignQuantity = 10;
//        const int orderQuantity = 4;
//        var campaign = CreateSingleBucketCampaign(campaignQuantity);
//        var bucket = campaign.StockBuckets.Single();
//        var stockItem = CreateCampaignStock(campaignQuantity);
//        var reservation = CreateReservation(campaign, orderQuantity);
//        Assert.IsTrue(bucket.TryReserve(orderQuantity));
//        var immediatelyAvailable = campaign.Close(EndsOnUtc.AddSeconds(1));
//        stockItem.Release(immediatelyAvailable);

//        // Act
//        var firstTransition = reservation.TryRelease();
//        if (firstTransition)
//        {
//            bucket.Release(orderQuantity, campaignClosed: true);
//            stockItem.Release(orderQuantity);
//        }

//        var secondTransition = reservation.TryRelease();
//        if (secondTransition)
//        {
//            bucket.Release(orderQuantity, campaignClosed: true);
//            stockItem.Release(orderQuantity);
//        }

//        // Assert
//        Assert.IsTrue(firstTransition);
//        Assert.IsFalse(secondTransition);
//        Assert.AreEqual(ReservationStatus.Released, reservation.Status);
//        Assert.AreEqual(0, bucket.ReservedQuantity);
//        Assert.AreEqual(0, bucket.SoldQuantity);
//        Assert.AreEqual(campaignQuantity, bucket.ReleasedQuantity);
//        Assert.AreEqual(0, bucket.AvailableQuantity);
//        Assert.AreEqual(3L, bucket.Version);
//        Assert.AreEqual(20, stockItem.OnHand);
//        Assert.AreEqual(0, stockItem.Reserved);
//        Assert.AreEqual(20, stockItem.Available);
//        Assert.AreEqual(3L, stockItem.Version);
//    }

//    private static FlashSaleCampaign CreateSingleBucketCampaign(int totalQuantity) =>
//        CreateCampaign(
//            Guid.NewGuid(),
//            totalQuantity,
//            FlashSaleStockStrategy.SingleRow,
//            bucketCount: 1);

//    private static FlashSaleCampaign CreateCampaign(
//        Guid campaignId,
//        int totalQuantity,
//        FlashSaleStockStrategy strategy,
//        int bucketCount) =>
//        FlashSaleCampaign.Create(
//            campaignId,
//            "SKU-FLASH-001",
//            totalQuantity,
//            StartsOnUtc,
//            EndsOnUtc,
//            purchaseLimit: Math.Min(2, totalQuantity),
//            strategy,
//            bucketCount,
//            CreatedOnUtc);

//    private static StockItem CreateCampaignStock(int campaignQuantity)
//    {
//        var stockItem = new StockItem(Guid.NewGuid(), "SKU-FLASH-001", onHand: 20);
//        stockItem.Reserve(campaignQuantity);
//        return stockItem;
//    }

//    private static InventoryReservation CreateReservation(FlashSaleCampaign campaign, int quantity) =>
//        InventoryReservation.CreateFlashSale(
//            Guid.NewGuid(),
//            Guid.NewGuid(),
//            "buyer-001",
//            campaign.Id,
//            stockBucketId: 42,
//            campaign.Sku,
//            quantity,
//            CreatedOnUtc);
//}
