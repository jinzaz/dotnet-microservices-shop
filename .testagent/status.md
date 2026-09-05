# 秒杀测试完成状态

## 执行结果

- 命令：`dotnet test tests/MSA.FlashSaleTests/MSA.FlashSaleTests.csproj`
- 结果：Passed 16，Failed 0，Skipped 0
- 测试工程已注册到 `MicroservicesArchitecture.slnx`

## 断言质量复核

- 共 16 个测试；无断言测试 0，只有空值/存在性断言的测试 0，自引用断言 0。
- 断言覆盖相等性、布尔正反例、精确异常、集合结构、空值、状态迁移与副作用。
- 数量守恒测试同时钉住 `Available/Reserved/Sold/Released/OnHand/Version`，不是只验证“调用成功”。
- 负向或幂等结果包括超额预占拒绝、售罄后继续预占拒绝、重复提交/释放/关闭/取消不再改变状态。

## 伪变异/行为缺口复核

结论：**Mixed**。领域计数器、边界、唯一索引与检查约束已有较强回归保护；真实 PostgreSQL 的条件更新、`FOR UPDATE SKIP LOCKED`、advisory lock，以及 Outbox/Inbox 乱序补偿仍需要生产等价数据库集成测试和并发压测。

| 风险 | 调用者可见结果 | 现有证据 | 结论 |
| --- | --- | --- | --- |
| 单行库存超卖 | 刚好售罄后不能再预占，计数不为负 | `TryReserve_ExactRemainingQuantity_SellsOutWithoutGoingNegative` | Likely killed（领域层） |
| 分片库存分配 | 所有库存完整分配且每片可容纳购买上限 | `Create_BucketedStock_DistributesEveryUnitAcrossBuckets`、`Create_BucketedStockWithBucketBelowPurchaseLimit_ThrowsArgumentOutOfRangeException` | Likely killed（领域层） |
| 重复提交/释放 | 第二次事件不重复改变库存 | `Commit_ReservedQuantity_MovesReservationToSoldExactlyOnce`、`Release_OpenCampaign_ReturnsQuantityToCampaignExactlyOnce` | Likely killed（领域层） |
| 数据库重复下单 | 用户+活动、用户+幂等键只能存在一行 | 三个唯一索引元数据测试 | Likely killed（模型层） |
| 原子 SQL 并发 | 并发请求不得突破 PostgreSQL 条件更新 | 未连接真实 PostgreSQL 执行并发请求 | No coverage（需集成/压测） |
| 消息乱序补偿 | 订单先取消、预占后到时必须释放 | 已实现处理分支，但当前单元测试未启动真实消息/数据库事务 | Candidate survivor (unverified) |

本次复核发现并修复了“购买上限大于分片容量”边界，并新增领域、验证器和数据库约束测试。未执行临时源码变异，也未连接或修改外部数据库。

## 需求证据

| Requirement | Evidence |
| --- | --- |
| “少库存秒杀场景” | `Create_SmallStock_UsesSingleBucketAndPreservesQuantity`、`TryReserve_ExactRemainingQuantity_SellsOutWithoutGoingNegative` |
| “大库存秒杀场景” | `Create_BucketedStock_DistributesEveryUnitAcrossBuckets`、`Create_BucketedStockWithBucketBelowPurchaseLimit_ThrowsArgumentOutOfRangeException`、`Validate_ExplicitTooManyBucketsForPurchaseLimit_ReturnsValidationFailure` |
| “防止重复下单” | `InventoryModel_FlashSaleReservation_HasUniqueCampaignUserIndex`、`OrderModel_FlashSaleOrder_HasUniqueCampaignUserIndex`、`OrderModel_Idempotency_HasUniqueUserKeyIndex` |
| “库存超卖” | `TryReserve_MoreThanRemaining_IsRejectedWithoutChangingCounters`、`InventoryModel_StockCounters_HaveNonNegativeCheckConstraints`、`InventoryModel_CampaignBucketCount_RequiresEveryBucketToFitPurchaseLimit` |
| “一些核心业务难点场景” | 提交/释放/关闭/晚到释放/重复取消等状态迁移测试 |
| “可以对数据库设计进行按需修改” | Inventory 与 Ordering 独立迁移、唯一索引及检查约束的 EF 元数据断言 |
