# 秒杀业务测试实施计划

## 测试工程与命令

- 工程：`tests/MSA.FlashSaleTests/MSA.FlashSaleTests.csproj`
- 窄范围：`dotnet test tests/MSA.FlashSaleTests/MSA.FlashSaleTests.csproj`
- 解决方案发现：`dotnet test MicroservicesArchitecture.slnx`
- 最终构建：`dotnet build MicroservicesArchitecture.slnx --no-incremental`

## 需求到测试映射

| 用户需求 | 计划测试 |
| --- | --- |
| “少库存秒杀场景” | `Create_SmallStock_UsesSingleBucketAndPreservesQuantity`；`TryReserve_ExactRemainingQuantity_SellsOutWithoutGoingNegative` |
| “大库存秒杀场景” | `Create_BucketedStock_DistributesEveryUnitAcrossBuckets` |
| “防止重复下单” | `InventoryModel_FlashSaleReservation_HasUniqueCampaignUserIndex`；`OrderModel_FlashSaleOrder_HasUniqueCampaignUserIndex`；`OrderModel_Idempotency_HasUniqueUserKeyIndex` |
| “库存超卖” | `TryReserve_MoreThanRemaining_IsRejectedWithoutChangingCounters`；`InventoryModel_StockCounters_HaveNonNegativeCheckConstraints` |
| “一些核心业务难点场景” | `Commit_ReservedQuantity_MovesReservationToSoldExactlyOnce`；`Release_OpenCampaign_ReturnsQuantityToCampaignExactlyOnce`；`Close_ActiveCampaign_ReleasesOnlyImmediatelyAvailableQuantity`；`Release_ClosedCampaign_ReleasesLateReservationToGlobalStock` |
| “可以对数据库设计进行按需修改” | 两个 DbContext 的模型元数据测试及 Inventory/Ordering 独立迁移文件；解决方案级构建通过 |

## 实施阶段

1. 先实现领域模型和数量守恒规则，再写对应领域测试。
2. 实现 PostgreSQL 原子预占、用户级去重与消息处理分支，再验证 EF 唯一索引和检查约束。
3. 接入 API、活动过期回收、Outbox/Inbox 事件链及迁移。
4. 运行窄范围测试，复核断言质量与可幸存变更，最后执行解决方案级测试和非增量构建。

