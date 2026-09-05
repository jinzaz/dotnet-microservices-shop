# 秒杀业务测试研究

## 范围与仓库约束

- 工作区：`E:\C#.NET\Applications\1_Repos\MicroservicesArchitecture`
- 构建入口：`MicroservicesArchitecture.slnx`
- 项目系统：SDK-style，目标框架由 `Directory.Build.props` 统一为 `net10.0`
- 包管理：`Directory.Packages.props` 中央包版本管理
- 当前测试现状：Roslyn 静态配对分析发现 112 个生产源文件、0 个测试文件、0 个已配对文件
- 静态分析说明：该结果只说明源码符号没有对应测试引用，不代表真实行覆盖率或分支覆盖率

## 本次目标清单

1. “少库存秒杀场景”：单库存分片必须正确处理刚好售罄和库存不足边界。
2. “大库存秒杀场景”：多库存分片必须完整分配总库存，且分片数与数量保持合法。
3. “防止重复下单”：Ordering 必须具有用户 + 活动唯一约束，同时保留用户 + 幂等键约束；Inventory 必须具有用户 + 活动预占唯一约束。
4. “库存超卖”：库存分片的预占、售出、释放总和不能超过分配量，数据库模型必须包含对应检查约束。
5. “一些核心业务难点场景”：支付成功提交、支付失败释放、活动关闭回收未售库存必须保持状态与数量守恒，重复消息不应重复改变库存。
6. “可以对数据库设计进行按需修改”：Inventory 与 Ordering 各自拥有迁移，不共享 DbContext 或业务表。

## 目标生产文件

- `src/MSA.InventoryService/Domain/FlashSaleCampaign.cs`
- `src/MSA.InventoryService/Domain/InventoryEntities.cs`
- `src/MSA.InventoryService/Application/FlashSales/*`
- `src/MSA.InventoryService/Infrastructure/InventoryDbContext.cs`
- `src/MSA.InventoryService/Infrastructure/FlashSaleStockStore.cs`
- `src/MSA.InventoryService/Integration/InventoryEventHandlers.cs`
- `src/MSA.OrderService/Domain/Order.cs`
- `src/MSA.OrderService/Application/CreateFlashSaleOrder/*`
- `src/MSA.OrderService/Infrastructure/OrderDbContext.cs`
- `src/MSA.OrderService/Infrastructure/PostgresOrderSubmissionLock.cs`
- `src/MSA.OrderService/Integration/OrderWorkflowHandlers.cs`
- `src/MSA.Contracts/CommerceEvents.cs`

## 测试基础设施选择

- 新建一个边界明确的 `tests/MSA.FlashSaleTests`，同时引用 Inventory 与 Ordering 项目。
- 使用本机已缓存的 MSTest 4.0.2；测试类密封、AAA 结构、方法名采用 `Method_Scenario_ExpectedResult`。
- 单元测试不访问 PostgreSQL、Redis、RabbitMQ、网络端口或墙上时钟。
- SQL 原子性由 PostgreSQL 条件更新、行锁、唯一索引和检查约束共同保证；单元测试验证领域状态机及 EF 模型元数据，最终构建验证迁移与模型可编译。

