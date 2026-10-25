# CLAUDE.md

.NET 10 微服务电商（11 个业务服务 + YARP 网关 + 两个 BFF + 三个 React 前端）。这里只记**跨会话沉淀下来、看代码不容易得出的决策和坑**；架构总览和既有规范不在这里重复，先读：

- [README.md](README.md)：架构图、核心设计、本地运行
- [.agents/skills/msa-architecture/SKILL.md](.agents/skills/msa-architecture/SKILL.md)：分层、路由、表命名、DbContext/MediatR 注册约定（**必须遵守**）
- 各服务 `src/*/.agents/skills/*/SKILL.md`、[src/BFFs/SKILL.md](src/BFFs/SKILL.md)：服务内约定
- [src/MSA.Web/Front/SKILL.md](src/MSA.Web/Front/SKILL.md)、[src/MSA.Web/Identity/SKILL.md](src/MSA.Web/Identity/SKILL.md)：前端视觉规范
- [docs/core-commerce-refactoring.md](docs/core-commerce-refactoring.md)、[docs/search-service.md](docs/search-service.md)、[docker/README.md](docker/README.md)

## 常用命令

```bash
dotnet build MicroservicesShop.slnx
dotnet test MicroservicesShop.slnx
dotnet ef migrations add <Name> --project src/MSA.<X>Service
src/MSA.Web/dev.bat            # 商城 :5173 / 登录页 :5174 / 后台 :5175
```

前端改完跑 `npm run check`（类型检查）。`src/MSA.Web/*/dist/` 不入库。

## 协作约定

- 用中文回复。**不要自行 commit**，用户要求时才提交。
- 分支：长期 `dev` 分支，通过 PR 合并到 `master`；`dev` 不能在合并后被删除。
- 用户自己部署到测试服务器验证，不要在本机长时间挂起整套服务。
- 迁移只生成，不对 `192.168.1.50` 上的库执行，除非用户明确要求。

## 交易链路

- **支付成功即确认订单**（`Confirm` + 发 `CommitInventory`、`OrderConfirmed`）；`InventoryCommitted` 只是让 Saga 结束，超时会重发 `CommitInventory`。不要再写成"收到 InventoryCommitted 才确认"。
- **下单前的库存 gRPC 预检只查不占**，作用是体验；防超卖的正确性在库存服务处理 `OrderSubmitted` 时按 SKU 加 `pg_advisory_xact_lock` 后预占。预检失败（库存服务不可用）放行。
- **gRPC 只用于需要实时结果的读**：目录确认最终价、库存可售预检、支付实时状态、券权威状态、收货地址归属、支付前反查订单金额。每个调用都要有明确降级。**跨服务的写和状态流转只走事件 + Saga**，不要改成同步调用。新调用方：Identity 加 `MSA_Svc_*` client_credentials 应用 → `AddMsaServiceAuthentication` + `AddMsaGrpcClient<T>`。
- 消息消费失败：`{队列}.retry`（TTL 回流，默认 5 次 / 10 秒）→ 用尽进 DLQ，BM「消息运维」页可重放。**对不上当前状态的过期回执记 warning 后 Ack，不要抛异常**，否则会卡死在 DLQ。
- Inbox 去重记录保留期（30 天）必须远长于消息最大重投时间；Outbox/Saga 等后台循环整轮 try/catch，不能让异常冒泡停掉 Host。

## 促销、券、售后

- 定价按"每行剩余金额"逐个促销计算，优惠按比例分摊回行（`OrderLine` 记分摊额）；满额门槛只看**适用商品**、且按**前序促销减过后**的金额判断。
- `StackPolicy.Exclusive` 的含义是"优先级更低的不再计算"，不是"不与任何促销叠加"。订单侧不认识的优惠类型只让该活动不生效，不抛异常。
- 券明文不落库（只存 SHA-256 + 掩码）；分桶配额的拓扑一旦被订单侧收到就固定，不能重建（会清零计数）。
- 售后单在订单服务里。退款金额由后端按行实付分摊计算，后台不能手改金额；部分退货不扣回满减优惠（淘宝口径）。
- 枚举数值**不复用、不重编号**（例：`PromotionStatus` 的 1 是已废弃的 Scheduled，保留空位）。

## 持久化

- PostgreSQL 特性一律走 `MSA.Persistence.Postgres`：`ForUpdate()` / `ForUpdateSkipLocked()` / `InsertOnConflictDoNothingAsync` / `UpsertAsync` / `AcquireTransactionLock(s)Async`。`src` 下不写手写 SQL。多键咨询锁会排序后加锁，避免死锁。
- 用户输入拼进 `LIKE` 前用 `PostgresLike` 转义。`pg_trgm` 只在 CatalogDb、InventoryDb 启用，关键词少于 3 个字符用不上索引。
- CHECK 约束：枚举列由 `EFCore.CheckConstraints`（`UseEnumCheckConstraints()`，只在关系型 provider 下启用）自动生成，**不要再手写枚举范围**；审计时间、规范化编码、币种、SHA-256、jsonb、时间窗口这类重复写法用 `CheckSql`。跨字段业务规则保留原生 SQL，但数字写成 `{(int)Enum.X}`。改写时保持约束名不变，生成迁移后确认只有预期差异。
- 迁移默认不在启动时执行：`Database:MigrateOnStartup`（Docker 为 `MSA_MIGRATE_ON_STARTUP`）只在首次建库或有新迁移时临时打开。生产倾向 `dotnet ef migrations script --idempotent`。
- 有设计时工厂的 DbContext（如 `CertificateDbContext`），迁移历史表的 schema 必须在工厂和 `Program.cs` 共用同一个配置方法（`ConfigureNpgsql`），否则 `dotnet ef` 会把历史表建到 `public`。

## 缓存（MSA.Redis）

- 失效靠启动时声明"实体 → 缓存键"规则，EF 拦截器在**事务提交后**统一删除；业务代码不手动删缓存。
- 并发回写旧值用"代际"校验（Lua 原子比对），不用延迟双删。single-flight 只在进程内生效。
- Redis 不可用时读路径降级查库；库存、价格这类数据不开本地一级缓存。

## 搜索

- ES 是可丢弃、可重建的投影，数据库是权威来源；变更经 Outbox → 搜索服务 gRPC 回读 → 带版本号写入。
- 直接手写 REST/JSON，不用 `Elastic.Clients.Elasticsearch`（客户端大版本必须跟服务端走）；代价是单地址，多节点要在前面放负载均衡。
- 生产问题用 `生产问题 #N` 注释标注，并在 `docs/search-service.md` 的对照表里登记，新问题接着编号。
- 改索引 mapping 时提升索引结构版本号，服务启动时会自动不停机重建。搜索服务部署之前发出的同步消息会丢，需要在后台「搜索索引」页做一次全量重建。
- 默认走容器内网明文 HTTP，TLS 配置写好但保持注释。

## 认证与安全

- **公网 Issuer 和内部地址分开**：`iss` 校验、`authorize` / `end-session` 用公网 issuer；JWKS、discovery、token、服务令牌走 `MSA_IDENTITY_INTERNAL_ADDRESS`（`http://msa-identity:8080`）。资源服务统一用 `SetMsaIssuer`。
- BFF 的 OpenIddict 客户端必须**从内部地址拉 discovery 再改写端点**，不要手写 `OpenIddictConfiguration`：手写版本曾先后导致 `iss` 校验失败、`ResponseTypesSupported` 为空、拿不到 JWKS 验签三次登录 400。
- Identity 在非 Development 环境 `DisableTransportSecurityRequirement()`：TLS 终止在 nginx，identity 不映射端口。若 identity 将来直接暴露公网，要撤掉这一项。
- 新增 API scope 需要三处都到位：Identity 启动种子、`docker/config/identity/appsettings.json` 里客户端的权限（部署机挂载的这份）、用户重新登录。
- 请求体加密只在浏览器 ⇄ BFF 这一段（ECDH P-256 + HKDF + AES-256-GCM），BFF 到网关是内网明文。`UseBffBodyEncryption()` 前后各做一次路由匹配，否则 BFF 自己的 JSON 端点会返回 415。SSE、大文件端点标 `[SkipBodyEncryption]`。
- BM 后台权限由 BFF.BM 自己的 PBAC 管，不看 Identity 的 `admin` 角色。新增后台接口要在 BFF.BM 加 PBAC 路由迁移。`Pbac:BootstrapAdminUserIds` 只放行权限管理，用于首次部署给自己建成员。
- 短信验证码**登录即注册**：未注册的号码验证通过后自动建号（无邮箱、无密码），所以邮箱可空——`RequireUniqueEmail` 关掉，改由 `OptionalUniqueEmailValidator` 只查唯一，不要再打开它（会强制邮箱必填）。短信验证码由 Identity 自己生成和核验，阿里云只负责发送，不接 `CheckSmsVerifyCode`。
- 手机号**换绑**：绑定一个已属于其他账户的号码时照常发码，`ConfirmPhoneNumber` 验证通过后把号码从原账户移过来；原账户没有邮箱也没有密码就一并停用。不做跨服务的账户合并，原账户的订单等数据留在原 Id 上。
- 证书：`MSA.CertificateService` 独占写 CertificateDb，私钥按使用方 KEK 加密；Identity 和 BFF 直接只读库（避免和 Identity 签发令牌形成循环依赖）。证书库里没有当前证书时回退到 env 里的 pfx。迁移现有部署时**导入原 pfx，不要生成新证书**，否则已签发令牌全部失效。

## 配置

- 环境变量进配置有两条路：值完整的用 compose 的 `Section__Key`；需要和 appsettings 里的片段拼接的（`INFRA_*`、`MSA_SERVICE_AUTHORITY`、`MSA_OTLP_ENDPOINT`、`MSA_GRPC_<服务>_ADDRESS`）由 `MsaEnvironment` 合并。完整映射见 `MsaEnvironment.cs` 顶部注释和 `docker/README.md`「配置分层」。
- 客户端密钥只从 `MSA_CLIENT_SECRET_<ClientId 大写>` 环境变量读（`MsaEnvironment.ClientSecret`），appsettings 里的值无效。
- 本地：`msa.env` 是入库的默认值，个人地址和口令写 `msa.local.env`（不入库）；测试用 `testsettings.local.json`。`msa.env` 和 `docker/.env.example` 不要求字段一致。
- 服务公共部分走 `AddMsaServiceDefaults()` / `UseMsaServiceDefaults()`，集成事件处理器用 `AddIntegrationEventHandlersFromAssemblyContaining<T>()` 扫描注册，不要在 `Program.cs` 里重复写。`UseHttpsRedirection` 默认关（`Hosting:UseHttpsRedirection`）。

## 部署注意

- 新服务镜像：在 `docker/images.txt` 加一行，并在 `docker/Dockerfile` 末尾加同名 target。
- `MSA_PULL_POLICY=missing`：只执行 `up -d` 不会拉新镜像，要先 `docker compose pull`。
- 目标是 2C4G 小机器：服务设 `mem_limit` + `DOTNET_gcServer=0`；Npgsql 连接池上限 15，共约 17 个池，所以 PG `max_connections` 要设 300。
- 挂载进容器的 pfx 需要 644（容器以 UID 1654 运行），`cert-passwords.env` 设 600。

## 已知未做（截至 2026-10-22）

- 商城还不能按秒杀价下单：购物车和结算没有传 `offerId`，目前只有秒杀角标。
- 注册没有人机验证（发短信前有本地算术图形验证码，只是减速带）；`RequireConfirmedEmail = false`。
- 促销暂停后到真正停止生效，有一段 Outbox 投递延迟（秒级），下单时不实时确认促销状态。
- 没有引入 GraphQL：BFF 本身就是聚合层，需要聚合时按页面加 REST 聚合端点（参考 `OrderOverviewEndpoints`）。
