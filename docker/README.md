# Docker Compose

只编排 MSA 的 11 个 .NET 服务；PostgreSQL、Redis、RabbitMQ、OTel Collector 由外部的 `dotnet-infra` 项目提供，
所有服务加入它的 `dotnet-infra_backend` 网络（外部网络，需要先存在）。

## 目录

```
docker/
├── compose.yaml                 # 入口：只做 include，按域分组
├── base/dotnet-service.yaml     # 公共部分：TLS 证书、Issuer、OTLP、环境（各服务 extends 它）
├── services/
│   ├── edge/        gateway.yaml  bff-web.yaml  bff-bm.yaml
│   ├── identity/    identity.yaml user.yaml
│   ├── commerce/    catalog.yaml  inventory.yaml basket.yaml order.yaml payment.yaml
│   └── platform/    notification.yaml
├── images.txt                   # 镜像清单：镜像名 ↔ Dockerfile，构建脚本与 compose 共用
├── scripts/
│   ├── build-images.ps1 | .sh          # 构建并推送镜像到 Harbor（全部或指定几个）
│   └── new-openiddict-certs.ps1 | .sh  # 生成认证中心与两个 BFF 的 OpenIddict 密钥证书
├── .env.example                 # 模板；复制成 .env（.env 与 certs/ 不入库）
├── certs/                       # tls.pfx（按域名申请）+ OpenIddict 密钥与 cert-passwords.env（脚本生成）
└── README.md
```

每个服务一个文件，只写它自己的东西：镜像、端口、域名别名、连接串、它要调用的下游地址。
compose 只从 Harbor 拉镜像运行，不负责构建。

## 构建并推送镜像

镜像推到 `harbor.local/msa-shop/<镜像名>:<标签>`，镜像名见 `images.txt`。每个镜像推两个标签：
版本标签（默认当前 git 提交的短哈希，工作区有未提交改动时加 `-dirty`）和 `latest`。

```powershell
docker login harbor.local                       # 一次即可
./scripts/build-images.ps1                      # 列出清单，交互选择：all 或编号（如 4,7）/ 镜像名
./scripts/build-images.ps1 all                  # 全部
./scripts/build-images.ps1 catalog-service order-service -Tag 1.4.0   # 指定几个、指定版本
./scripts/build-images.ps1 all -NoPush          # 只构建不推送；-NoLatest 不推 latest
```

`build-images.sh` 参数相同（`--tag`、`--no-push`、`--no-latest`、`--registry`、`--platform`）。默认构建 `linux/amd64`。

### Harbor 走 HTTP（harbor.local:80）

Docker 默认只用 HTTPS 访问仓库。推送和拉取的机器都要把它加进 Docker 的 insecure registries，
Docker Desktop 在 Settings → Docker Engine，Linux 在 `/etc/docker/daemon.json`，改完重启 Docker：

```json
{ "insecure-registries": ["harbor.local"] }
```

`harbor.local` 本身要能解析（hosts 文件或内网 DNS），Harbor 里要先建好 `msa-shop` 项目。

## 启动

```powershell
cd docker
Copy-Item .env.example .env                       # 填域名、TLS 证书口令、基础设施地址；MSA_IMAGE_TAG 选版本
Copy-Item <申请到的证书>.pfx certs/tls.pfx         # 通配证书 *.<MSA_DOMAIN>
./scripts/new-openiddict-certs.ps1                # 认证中心与两个 BFF 的 OpenIddict 密钥（已有则保留）
docker compose pull                               # 从 Harbor 拉镜像
docker compose up -d                              # 全部
docker compose up -d msa-catalog msa-inventory    # 只起几个
docker compose logs -f msa-order
```

首次运行要建库时把 `.env` 里的 `MSA_MIGRATE_ON_STARTUP` 设为 `true`。
跟踪 `latest` 时把 `MSA_PULL_POLICY` 设为 `always`，每次 `up` 都会拉最新镜像；固定版本用默认的 `missing`。

## 端口与域名

每个服务的域名是 `<名字>.${MSA_DOMAIN}`，端口在容器内外一致（与本机 `launchSettings` 相同）：

| 服务 | 容器名 | 域名 | 端口 |
|---|---|---|---|
| API 网关 | msa-gateway | gateway.${MSA_DOMAIN} | 10000 |
| 订单 | msa-order | order.${MSA_DOMAIN} | 10001 |
| 认证中心 | msa-identity | identity.${MSA_DOMAIN} | 10002 |
| 目录 | msa-catalog | catalog.${MSA_DOMAIN} | 10003 |
| 库存 | msa-inventory | inventory.${MSA_DOMAIN} | 10004 |
| 支付 | msa-payment | payment.${MSA_DOMAIN} | 10005 |
| 通知 | msa-notification | notification.${MSA_DOMAIN} | 10006 |
| 购物车 | msa-basket | basket.${MSA_DOMAIN} | 10007 |
| 商城 BFF | msa-bff-web | bff-web.${MSA_DOMAIN} | 10008 |
| 后台 BFF | msa-bff-bm | bff-bm.${MSA_DOMAIN} | 10009 |
| 用户 | msa-user | user.${MSA_DOMAIN} | 10010 |

**同一个域名，容器内外都能用。** 每个服务在 `dotnet-infra_backend` 上挂了自己域名的网络别名：
容器之间的调用（gRPC、网关转发、取服务令牌、校验令牌时拉认证中心的发现文档）直接解析到对应容器，
浏览器则经 DNS 解析到部署机。所以 OIDC 要求的「浏览器跳去登录的地址」与「服务校验令牌的 Issuer」
天然是同一个 `https://identity.${MSA_DOMAIN}:10002/`，一张域名证书同时满足浏览器与容器。

DNS 只需要把浏览器直接访问的名字指向部署机：`identity`、`bff-web`、`bff-bm`（以及前端站点）。
其余服务只在容器之间互访，不需要对外解析。

认证中心里两个 BFF 的登录 / 注销回调地址由 compose 按域名覆盖；前端站点地址见 `.env` 的
`MSA_STORE_FRONTEND_URL` / `MSA_ADMIN_FRONTEND_URL` / `MSA_IDENTITY_UI_URL`。

## 证书

### TLS：按域名向 CA 申请

申请一张覆盖 `*.${MSA_DOMAIN}` 的通配证书（或包含上表全部域名的多域名证书），导出为 pfx 放到 `certs/tls.pfx`，
口令填 `.env` 的 `MSA_TLS_CERT_PASSWORD`。所有服务共用这一张；公网 CA 签发的证书在容器与浏览器里天然受信任，
不需要额外配置信任链。

CA 给的是 PEM（例如 fullchain.pem + privkey.pem）时先转成 pfx：

```bash
openssl pkcs12 -export -in fullchain.pem -inkey privkey.pem -out certs/tls.pfx -passout pass:<口令>
```

续期只需替换 `certs/tls.pfx` 后 `docker compose restart`。

### OpenIddict 密钥：脚本生成

`scripts/new-openiddict-certs.ps1`（或 `.sh`）生成三份互相独立的密钥证书。它们只是签名 / 加密密钥的载体，
不参与 TLS，自签名即可：

| 文件 | 用途 | 挂给谁 |
|---|---|---|
| `identity-openiddict.pfx` | 认证中心签名 / 加密访问令牌与身份令牌 | 只有 msa-identity |
| `bff-web-openiddict.pfx` | 商城 BFF 签名 / 加密 OIDC state token | 只有 msa-bff-web |
| `bff-bm-openiddict.pfx` | 后台 BFF 签名 / 加密 OIDC state token | 只有 msa-bff-bm |
| `cert-passwords.env` | 上面三个 pfx 的随机口令（32 位） | 不挂载，只在 Compose 插值时读取 |

- RSA 3072、2 年有效期、同时具备 DigitalSignature 与 KeyEncipherment，与代码的用法一致（一份证书同时做签名与加密）。
- 应用只从环境变量 `MSA_OPENIDDICT_CERT_PATH` / `MSA_OPENIDDICT_CERT_PASSWORD` 读取它们（不读 appsettings.json），compose 只给对应的那个容器注入。
- 每个应用一份独立密钥：认证中心的令牌与 BFF 的 state token 互不共用，一处泄露不波及其他。
- 已有的证书默认保留：换掉认证中心的密钥，已签发的令牌全部失效；换掉 BFF 的，进行中的登录会失败。
  确实要轮换时加 `-Force`，可配合 `-Only identity` 只换一个（bash 版：`--force identity`）。

## 可观测性

所有服务通过 `MSA.Observability` 把链路、指标、日志用 OTLP（gRPC）发往 `MSA_OTLP_ENDPOINT`。
可以填 Collector、Aspire Dashboard 或 Jaeger 的地址。基础设施的 Collector 如果也在 `dotnet-infra_backend` 网络里，直接写它的容器名，例如 `http://otel-collector:4317`。

## 已知限制

- 没有写健康检查与 `depends_on`：aspnet 镜像里没有 curl，服务之间的调用本身有重试，启动顺序不影响最终可用。
- 默认以 Production 运行：认证中心与两个 BFF 用 certs/ 里固定的 OpenIddict 密钥，重启不会让令牌和登录失效。
  改回 Development 时它们会在容器里临时生成密钥（重启即失效），OpenAPI 与 gRPC 详细错误也会打开。
- 支付渠道回调地址是 `https://payment.${MSA_DOMAIN}:10005/payment/callbacks`，需要公网可达才能收到渠道通知。
- 镜像用 `sdk:10.0` 自带的 SDK 构建（`.dockerignore` 排除了锁定本机预览版 SDK 的 `global.json`）。
