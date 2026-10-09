# 商品搜索服务（MSA.SearchService）

Elasticsearch + IK 中文分词的商品搜索。目录服务（Catalog）是商品数据的权威，搜索索引只是它的一份**投影**：可以随时丢掉重建，不保存任何别处没有的数据。

## 架构

```
后台改商品 ──▶ Catalog ──(同一事务)──▶ catalog_outbox_messages
                 ▲                              │ Outbox 发布器
                 │ gRPC CatalogSearchFeed        ▼
                 │ GetProducts / ExportProducts  RabbitMQ: CatalogSearchInvalidated(productIds / categoryIds)
                 │                              │ 队列 msa.search（重试队列 + 死信 msa.search.dlq）
                 └──────────── SearchService ◀──┘
                                    │ bulk（external_gte 版本号）
                                    ▼
                          Elasticsearch：别名 catalog-products ──▶ catalog-products-<时间戳>
                                    ▲
浏览器 ──▶ BFF ──▶ 网关 /api/search/** ──┘   （503 时前台回退到 /api/catalog/products 的数据库搜索）
```

| 组件 | 位置 | 作用 |
|---|---|---|
| `SearchInvalidationInterceptor` | Catalog/Infrastructure | SaveChanges 时把变了的商品 / 改名的分类写进 Outbox（与业务同一事务） |
| `CatalogSearchFeedGrpcService` | Catalog/Grpc | 给搜索服务读商品快照；「是否可搜」的规则在这里判定 |
| `CatalogSearchInvalidatedHandler` | SearchService | 消费消息 → gRPC 回读最新快照 → 写索引 |
| `ProductIndexWriter` | SearchService | bulk 写入、逐条检查结果、重建期间双写 |
| `IndexRebuilder` / `SearchIndexBootstrapper` | SearchService | 零停机全量重建；首次启动、别名丢失时自动建 |
| `ProductSearchQuery` / `ProductSearchService` | SearchService | 查询 DSL、分面、高亮、熔断 |

接口（经网关 `/api` 前缀）：

| 方法 | 路径 | 权限 | 说明 |
|---|---|---|---|
| GET | `/search/products?q=&categoryId=&brandId=&minPrice=&maxPrice=&sort=&skip=&take=` | 匿名 | `sort`：`relevance`（默认）/ `newest` / `price_asc` / `price_desc` |
| GET | `/search/suggestions?q=` | 匿名 | 搜索框联想，失败时返回 `[]` |
| GET | `/search/admin/status` | `search.admin.read` | 别名指向、文档数、重建状态、最近错误 |
| POST | `/search/admin/rebuild?force=false` | `search.admin.write` | 后台全量重建；心跳过期的锁会自动接管，`force=true` 连仍在心跳的锁也清掉（只在确认对方已经不在时用） |
| GET / POST | `/search/admin/messaging/*` | `search.admin.read` / `write` | 与其他服务一致的消息运维接口；搜索服务只有死信（Outbox、Saga 两个接口恒为空） |

## 数据库检索（pg_trgm）

ES 负责前台搜索；后台商品、库存按名称或 SKU 片段检索，以及 ES 不可用时的前台降级，仍然直接查数据库。这些 `ILIKE '%关键词%'` 查询靠 pg_trgm 的 GIN 三元组索引：

| 库 | 索引 |
|---|---|
| CatalogDb | `catalog_products.Name`、`catalog_product_variants.Sku` |
| InventoryDb | `inventory_stock_items.Sku` |

- 迁移 `AddTrigramSearchIndexes`（两个服务各一个）先 `CREATE EXTENSION IF NOT EXISTS pg_trgm`，再用 `CREATE INDEX CONCURRENTLY` 建索引：不锁写入，但要在事务外执行，失败会留下 INVALID 索引，需要手动 `DROP INDEX` 后重跑。
- 扩展按库启用，用到的两个库各执行一次。`pg_trgm` 在 PostgreSQL 13+ 是受信任扩展，库的所有者即可创建。
- 中文能拆三元组的前提是库的 `LC_CTYPE` 把中文算作字母：`en_US.utf8`、`zh_CN.utf8` 可以，`C` 不行。
- 关键词至少 3 个字符才能用上索引；1–2 个字符的短词仍是全表扫描，这类搜索交给 ES。
- 用户输入里的 `%`、`_` 会先转义（`PostgresLike.Contains`），否则搜「_」会匹配任意字符。

## 品牌

- 目录新增品牌（`catalog_brands`，生命周期与分类相同：草稿 → 启用 → 归档）。商品的品牌可选，只能挂启用的品牌；还有在售商品的品牌不能归档。
- 后台接口：`/catalog/admin/brands`（列表、新建、改名、启用、归档），给商品换品牌用 `PUT /catalog/admin/products/{id}/brand`。这个接口和详情保存分开，旧客户端保存名称、描述时不会把品牌清空。前台接口：`GET /catalog/brands`（启用的品牌）。
- 搜索：
  - 文档带 `brandId` / `brandName`，品牌名参与相关度（权重仅次于商品名）。
  - 支持 `brandId` 筛选和品牌分面：分类分面只套品牌条件，品牌分面只套分类条件，选了一个仍能横向切换另一个。
  - 品牌改名后，经同步消息刷新它下面的商品。
- 部署：
  - 先执行目录迁移 `AddBrands`（给 50 万行的商品表加列建索引，建索引期间商品写入会阻塞几秒），以及后台 BFF 迁移 `AddBrandRoutes`。
  - 搜索服务启动后会因为映射版本升到 2 自动重建一次索引（见 #20）。
- 降级到数据库搜索时（ES 不可用）不支持品牌筛选，前台的品牌条件此时不生效。

## 后台管理（BM）

- 「商品目录」新增「品牌」页签；新建商品和商品工作台里可以选品牌（PBAC 模块 `catalog.brands`，换品牌归 `catalog.products.edit`）。

- **搜索索引**页（`/search`，PBAC 模块 `system.search`）：
  - 显示当前索引、文档数，并与目录已发布商品数对照（相差过大说明同步有积压或丢失）。
  - 显示重建进度（重建中每 3 秒刷新）和最近错误。
  - 提供全量重建和强制重建按钮。
  - 「搜索预览」按前台完全相同的规则查询，可以核对相关度、高亮和分类计数。
- **消息运维**页增加「搜索」：ES 长时间不可用时，同步消息在重试用尽后进死信，可以在这里查看和重放。
- 权限：
  - BM BFF 迁移 `AddSearchIndexRoutes` 新增模块 `system.search`（查询 / 编辑），系统管理员策略自动获得；搜索服务的死信接口归 `system.messaging`。
  - 认证中心给 BM 客户端加了 `search.admin.read` / `search.admin.write` scope。**已登录的 BM 会话要重新登录一次**才能拿到新 scope，否则搜索服务返回 403。
  - 其他角色需要的话，在「权限」页把 `system.search.*` 授给对应策略。

## 生产问题清单

代码注释里的「生产问题 #N」与下表一一对应，`grep -rn "生产问题 #"` 可以直接找到实现处。

| # | 问题 | 不处理会怎样 | 这里的做法 |
|---|---|---|---|
| 1 | 双写不一致 | 写库成功、通知 ES 失败（或反过来），两边永久不一致 | 不在请求里写 ES；SaveChanges 拦截器把「谁变了」写进**同一事务**的 Outbox，可靠投递 |
| 2 | 消息乱序、重复、并发写 | 旧快照后到，把新数据覆盖掉 | 消息只带 id，处理时总是回读最新快照；写入带商品乐观并发版本号（`version_type=external_gte`），旧版本被 ES 拒绝（409 视为成功） |
| 3 | 不可信输入 | `query_string` 支持通配符 / 正则，一个 `*a*` 就能拖垮集群 | 用户输入只进 `multi_match` / `term` 的值；长度 ≤80、页大小 ≤48、翻页窗口 ≤1 万全部封顶 |
| 4 | 重建停机、重建期间丢变更 | 删索引重建时前台无结果；重建中途的改动不在新索引里 | 新索引 + 别名原子切换；重建期间新索引挂 `-building` 别名，增量写入同时写新旧两边；ES 锁索引保证全集群只有一个重建 |
| 5 | 深分页 | `from+size` 超 1 万直接报错；越深越慢 | `max_result_window=10000`，接口层同样封顶；命中数超过 1 万只返回下界（显示「10000+」） |
| 6 | 定时上架 | 到点上架时数据库不发生写入，不会有同步消息，商品永远搜不到（或提前露出） | 生效时间在未来的商品照样写进索引，查询时按 `effectiveOnUtc < now/m` 过滤 |
| 7 | 翻页抖动 | 同分文档顺序不稳定，第二页重复或漏掉第一页的商品 | 所有排序最后用唯一的 `id` 兜底；不用 `_id` 排序（需要加载 fielddata，内存大户） |
| 8 | 高亮 XSS | 商品名由后台人员输入，`<em>` 高亮配合 `innerHTML` 渲染就是存储型 XSS | 高亮标记用 STX/ETX 控制字符（入库前从文本剔除），前端切成 React 节点，永远不用 innerHTML |
| 9 | 映射爆炸 | 动态映射会按写入的字段自动长出新字段，类型全靠猜 | `dynamic: strict`，未声明字段直接报错 |
| 10 | 同义词更新要重建 | 同义词写在索引设置里，改一个词要重建索引 | ES Synonyms API + `updateable` 查询分析器，改 appsettings 后重启服务即自动生效 |
| 11 | 传输安全与凭据 | 端口对外暴露时口令在局域网明文传输；跨机器时流量可被窃听；超级用户凭据泄露等于整个集群失守；为了连自签证书而关掉证书校验等于谁都能冒充 ES | 默认容器内网明文（与本仓库其他内部流量同一模型）：不映射端口、只加入 `dotnet-infra_backend`；用只能操作搜索索引的最小权限 API Key。跨机器部署时打开 compose 里注释好的 TLS，客户端用 `CaCertificatePath` 把信任锚换成自签 CA（`CertificateChainPolicy`），主机名、有效期照常校验，不关校验 |
| 12 | 单例里攥着 HttpClient | 拿不到 DNS 变化，ES 节点换 IP 后一直连旧地址 | 每次从 `IHttpClientFactory` 取客户端；连接池 5 分钟换新连接 |
| 13 | 全量导入慢 | 边写边刷新、边复制副本，50 万文档导入慢数倍 | 建索引时副本 0、`refresh_interval=-1`，灌完再恢复并 refresh |
| 14 | 全角半角、零宽字符 | 「ｉＰｈｏｎｅ」搜不到「iPhone」；复制粘贴带进来的零宽字符让词对不上 | 写入与查询都做 NFKC 规范化，并去掉控制 / 零宽字符 |
| 15 | 分面计数塌缩 | 选中一个分类后其他分类计数都变 0，用户没法横向切换 | 分类条件放在 `post_filter`，聚合不受它影响 |
| 16 | 同步链路没有监控 | 消费卡住、重建失败没人知道，搜索结果悄悄过期 | `/search/admin/status`；告警看 RabbitMQ `msa.search` 队列积压与 `msa.search.dlq` 死信数 |
| 17 | ES 故障拖垮服务 | 每个请求都等超时，线程池占满，连带服务不可用；恢复期被积压请求打死 | 失败后熔断 30 秒，期间直接 503；前台收到 503 回退到目录的数据库搜索；索引任务指数退避 |
| 18 | bulk 部分失败 | HTTP 200 里也可能有单条失败，不检查就悄悄丢数据 | 逐条检查：409 / 删除 404 成功；429 / 5xx 整条消息重试；其余 4xx 记错误日志后跳过，不让一条坏数据堵死队列 |
| 21 | 重建进程被杀、锁成孤儿 | 容器重启、发版、OOM 直接杀掉进程，清理代码不执行：重建锁和挂着 building 别名的半成品索引留在 ES 里，之后谁都不敢重建，增量写入还在往半成品里双写 | 锁带心跳（每 30 秒刷新），超过 2 分钟没心跳视为持有者已死，其他副本或重启后的自己自动接管；接管时先删掉半成品索引；状态接口读 ES 里的锁，后台能看到「某副本重建中 / 已中断」 |
| 20 | 映射升级 | 加字段（如品牌）、改分词后，strict 映射的旧索引会逐条拒绝带新字段的文档；不重建的话新字段永远查不到 | 映射里记 `_meta.schemaVersion`（`ProductSearchIndex.SchemaVersion`），服务启动发现当前索引版本更旧就自动零停机重建；重建期间旧索引拒掉的写入只记一条汇总警告，新索引经 building 别名照样收到 |
| 19 | 写入时自动建索引 | 别名不存在时 ES 会按文档建一个同名的动态映射索引，没有 IK 分词，而且之后别名再也建不出来 | 写入带 `require_alias=true`；ES 侧 `action.auto_create_index: ".*"` 只放行系统索引 |

## 现在还没部署 ES 时

`msa.env` 里的 `INFRA_ELASTICSEARCH_URL` 是占位地址。这时：

- 搜索服务正常启动，`/search/products` 返回 503，**前台自动回退**到目录服务原来的数据库搜索（没有分面、排序和价格筛选，前端会自动隐藏这些控件）；
- `SearchIndexBootstrapper` 按指数退避（最长 5 分钟）一直重试，ES 一就绪就自动完成首次建索引；
- 目录照常往 Outbox 写同步消息，消息在 `msa.search` 队列里按 30 秒间隔重试，60 次（约 30 分钟）后进死信队列。ES 长期不在时死信会积累，部署 ES 后**首次建索引是全量的**，死信可以直接清空。

## 部署 Elasticsearch

1. 构建带 IK 的镜像并启动，步骤见 [docker/elasticsearch/README.md](../docker/elasticsearch/README.md)：
   - 生产默认只在容器内网访问；本地开发叠加 `compose.dev.yaml` 把 9200 映射出来。
   - IK 插件版本必须与 ES 完全一致（`ES_VERSION`，默认 9.1.5），可选版本见 <https://release.infinilabs.com/analysis-ik/stable/>。
   - Linux 宿主机需要 `sysctl -w vm.max_map_count=262144`（写进 `/etc/sysctl.conf` 持久化）。
2. 给搜索服务建一个最小权限的 API Key（命令在同一个 README 里），填进 `msa.env`（本地）或 `docker/.env`（容器）的 `INFRA_ELASTICSEARCH_API_KEY`，地址填 `INFRA_ELASTICSEARCH_URL`。
3. 启动（或重启）搜索服务，日志出现 `Search index catalog-products-… rebuilt with N products` 即完成。之后可用 `/search/admin/status` 核对文档数。

改了映射、分词器（`IndexTokenizer` / `SearchTokenizer`）、IK 自定义词典，或者第一次加同义词时，调用一次 `POST /api/search/admin/rebuild`。

## 造数（50 万商品）

`tools/MSA.CatalogSeeder` 直接用二进制 COPY 写目录库。它会生成 37 个品类、50 万个商品、一百多万个规格以及每个规格的库存：

- 单规格商品约 35%，2–12 个规格的商品约 65%。规格由颜色、存储、尺码等销售属性组合而成，升级档自动加价。
- 约 92% 的商品已发布（其中 1% 定时上架），另有少量已下架和草稿，用来验证可见性规则。
- 库存直接写进库存服务的 `InventoryDb`（默认沿用 `msa.env` 的地址口令，库名换成 InventoryDb）。约 8% 缺货、12% 紧张（1–9 件），其余按价位分布，便宜的备货多、贵的备货少。每个有货的规格还有一条「入库」流水，和后台「新增库存」写出的数据一致。用 `--no-inventory` 可以跳过。
- 同时写入购物车（`BasketDb`）和订单（`OrderDb`）的商品投影：加购、报价、下单都只认本地投影，不写的话商品前台能看到、却加不进购物车。正常情况下这两份投影由目录的 `ProductVariantChanged` 事件维护；导入时逐规格补发一百多万条事件要十几个小时，所以直接写入，内容与事件处理器写出的一致，之后后台改商品发出的事件会正常覆盖。
- 品牌来自品类词表里的品牌词（67 个，编码 `SEED-B001` 起，直接是启用状态），每个商品挂的就是它名字开头的那个品牌。
- 商品名按品类词表生成，例如「曜石 Vista 76 Pro+ 电竞智能手机」「青衿 春夏重磅修身圆领T恤 男女同款」，不会出现跨品类乱拼。名字保证不重复：词表组合不够时会像真实商品一样带上型号（例如「野径 加厚银胶天幕 TE-4071」）。

```bash
dotnet run --project tools/MSA.CatalogSeeder -- --dry-run 20
```

```bash
dotnet run --project tools/MSA.CatalogSeeder -c Release
```

```bash
dotnet run --project tools/MSA.CatalogSeeder -c Release -- --reset
```

- 第一条：只打印样例，不连数据库，先看看商品名是否合意。
- 速度：生成是单线程的（每秒两万个以上），写库按 `--parallel`（默认 4）并行，每批里目录、库存、两份投影四个库也同时写；连接设置了 `synchronous_commit=off`，提交不等 WAL 落盘。结束时会打印各库的累计耗时，最大的那项就是瓶颈。数据库服务器核数多、磁盘快可以调大 `--parallel`。
- 第二条：导入 50 万商品，地址口令读仓库根目录的 `msa.env`。
- 第三条：删掉上次导入的商品、库存（含流水）、购物车 / 订单投影后重新导入（用 `--products 0` 则只删除）。导入的 SKU 都以 `SD-` 开头，清理只按这个前缀，不碰后台手工建的数据。

**图片**：不按商品生成。每个品类准备一小池图片（默认 8 张），商品从池里随机挑 1–4 张，总共只有约 300 个文件。

- `--images svg`（默认）：在本地按品类配色、图标和品类名画 SVG 占位图，离线、零成本。
- `--images pexels --pexels-key <KEY>`：从 Pexels 下载真实照片（<https://www.pexels.com/api/> 免费申请 key，可免费商用、无需署名）。按品类英文关键词搜索，每个品类只发一个请求。
- 默认上传到对象存储的公共桶 `shop-public`，与后台上传商品图同一套 URL 规则，前台和后台都能显示。没有对象存储时可以写到前台静态目录（只有前台能显示）：

  ```bash
  dotnet run --project tools/MSA.CatalogSeeder -c Release -- --image-dir src/MSA.Web/Front/public
  ```

**搜索同步**：每批商品和对应的 `CatalogSearchInvalidated` 消息在同一个事务里写进目录库，由目录的 Outbox 发布器投递给搜索服务，和后台改商品走的是同一条链路。索引还不存在时，搜索服务启动会直接全量重建。

**不会补的部分**：

- 审计日志、Redis 缓存（列表缓存 30 秒内自然过期）。
