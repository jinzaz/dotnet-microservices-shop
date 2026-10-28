# 下单链路压测

从本机压测部署好的环境，请求路径和真实用户一样：nginx → BFF.Web → 网关 → 订单服务，再经 Outbox / RabbitMQ 走库存预占、余额支付和订单确认。

| 文件 | 作用 |
|---|---|
| `prepare.mjs` | 批量注册用户，模拟浏览器走 BFF 登录，把会话 Cookie 和防伪 token 写进 `sessions.json` |
| `order.js` | k6 压测脚本：普通下单、秒杀、幂等三个场景 |
| `verify.sql` | 压测后核对订单状态、端到端耗时、有没有超卖、幂等是否生效 |

## 为什么能直接用 Cookie 压 BFF

- token 存在 BFF 的 Redis 会话里，脚本只需要拿到会话 Cookie。会话有效期 8 小时，滑动续期，access token 由 BFF 自动刷新。
- 请求体加密是可选的：不带 `X-Msa-Ekey` 头的请求直接透传。所以压测请求是明文 JSON，**测出的数字不包含加密开销**，写结论时要注明。

## 准备（每个环境做一次）

1. **测试服务器上**：
   - 打开 `MSA_BALANCE_WELCOME_ENABLED=true`，新用户注册后自动获得余额，下单用余额支付。确认 `MSA_BALANCE_WELCOME_AMOUNT` 够这轮压测下单用。
   - 认证中心按 IP 的注册、登录限流要临时调大（`Throttling__Registration__PermitLimit`、`Throttling__Authentication__PermitLimit`），压完改回来。
   - 准备压测商品：普通场景用几个库存充足的 SKU；秒杀场景单独准备一个 SKU，记下它的初始库存（比如 100）。
2. **本机安装 k6**：

   ```powershell
   winget install k6 --source winget
   ```

3. **造用户、登录、导出会话**（需要 Node 18 以上）。以下命令都是 PowerShell 写法，先进入本目录：

   ```powershell
   cd tests\load
   $env:SHOP_URL = 'https://shop.example.com'
   $env:IDENTITY_URL = 'https://identity.example.com'
   $env:LT_PASSWORD = '<符合密码规则的口令>'   # 至少 10 位，包含大小写字母、数字和符号
   $env:LT_USERS = '5'                        # 先用 5 个用户试跑，成功后再改成实际需要的数量
   node prepare.mjs
   ```

   - `$env:` 设置的变量在当前 PowerShell 窗口里一直有效，后面的命令不用重复设置；关掉窗口就没了。
   - 用户邮箱是 `lt000001@loadtest.local` 这种格式。注册接口对已存在的邮箱也返回成功，所以可以重跑。只想重新登录就设置 `$env:LT_SKIP_REGISTER = '1'`，用完执行 `Remove-Item Env:LT_SKIP_REGISTER`。
   - 自签名证书：`$env:NODE_TLS_REJECT_UNAUTHORIZED = '0'`。
   - `sessions.json` 里是能直接登录的会话，已经加进 `.gitignore`，不要提交。
   - 用 Git Bash 的话，可以写成一行：`SHOP_URL=... IDENTITY_URL=... LT_PASSWORD='...' node prepare.mjs`。

## 用户数要够

下单接口按用户限流：突发 5 次，之后每秒 2 次。普通场景轮流使用用户，**目标 QPS 不能超过用户数 × 2**，否则会测到 429。比如压 300 QPS，至少要 150 个用户，留点余量就准备 500 个。

## 跑压测

压测时间段记下来，Grafana 按这段时间看服务端指标，`verify.sql` 也要用到。

PowerShell 里带逗号的参数要加引号，否则会被拆成多个参数。

```powershell
$env:K6_WEB_DASHBOARD = 'true'

# 普通下单：阶梯加压，STAGES 是「时长:每秒请求数」
$env:K6_WEB_DASHBOARD_EXPORT = 'report-order.html'
k6 run -e SHOP_URL=https://shop.example.com -e 'SKUS=SKU-A,SKU-B' -e 'STAGES=1m:50,2m:100,2m:200,1m:0' order.js

# 秒杀：FLASH_VUS 个用户同时各抢 1 件（默认 2000，不超过会话数），库存不足的 409 算预期结果
$env:K6_WEB_DASHBOARD_EXPORT = 'report-flash.html'
k6 run -e SHOP_URL=https://shop.example.com -e SCENARIO=flash -e FLASH_SKU=SKU-FLASH order.js

# 幂等：同一个用户用同一个 Idempotency-Key 并发提交 5 次，应当返回同一个 orderId
k6 run -e SHOP_URL=https://shop.example.com -e SCENARIO=idempotency -e SKUS=SKU-A order.js
```

- 压测过程中可以打开 `http://localhost:5665` 看实时曲线，结束后生成离线 HTML 报告（`report-*.html`，不入库）。
- 秒杀按活动价下单就加 `-e OFFER_ID=<活动 Id>`，这时库存从活动的分配池里扣，`verify.sql` 第 6 条要改查 `inventory_allocations`。
- 自签名证书加 `-e INSECURE=1`。

## 压完核对

下单接口返回 202 只代表"已受理"，结果要等异步链路跑完（通常几秒）再查库：

```powershell
psql "<订单库连接串>" -v since='2026-10-28 15:00:00+08' -v sku=SKU-FLASH -f verify.sql
```

`verify.sql` 前 5 条查订单库，第 6 条查库存库，分开连接执行。重点看三件事：

- **端到端耗时**：建单到确认的 P99，比接口响应时间更能说明问题。
- **零超卖**：秒杀 SKU 的成交件数 ≤ 初始库存，并且和库存库里扣减的数量一致。
- **幂等**：同一个 Idempotency-Key 只有一个订单。

## 写结论时注明

- 压测环境：服务器配置（比如 2C4G 单机 Docker 部署）、压测机到服务器的网络。
- 请求经过 BFF，但不含请求体加密。
- 下单接口的耗时只是"受理"耗时，端到端耗时用 `verify.sql` 第 2 条的结果。
