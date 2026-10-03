# Docker Compose

只编排 MSA 的 11 个 .NET 服务；PostgreSQL、Redis、RabbitMQ、OTel Collector 由外部的 `dotnet-infra` 项目提供，
所有服务加入它的 `dotnet-infra_backend` 网络（外部网络，需要先存在）。

服务**不映射端口、只开明文 HTTP**，只在 Docker 网络内用容器名互相调用；对外由入口代理（如 nginx，不在本编排里）
按域名终止 TLS 后转发，它需要满足的条件见下文「入口代理的要求」。

## 目录

```
docker/
├── compose.yaml                 # 入口：只做 include，按域分组
├── base/dotnet-service.yaml     # 公共部分：环境、Issuer、OTLP、转发头；dotnet-infra-service 再加基础设施变量（各服务 extends 它）
├── services/
│   ├── edge/        gateway.yaml  bff-web.yaml  bff-bm.yaml
│   ├── identity/    identity.yaml user.yaml
│   ├── commerce/    catalog.yaml  inventory.yaml basket.yaml order.yaml payment.yaml
│   └── platform/    notification.yaml
├── config/                      # 结构化配置覆盖，挂载为 /app/appsettings.<环境>.json
│   ├── gateway/appsettings.json     # YARP 集群地址
│   └── identity/appsettings.json    # OpenIddict 客户端注册（回调地址、scope；密钥来自 .env）
├── images.txt                   # 镜像清单：镜像名 ↔ Dockerfile，构建脚本与 compose 共用
├── scripts/
│   ├── build-images.ps1 | .sh          # 构建并推送镜像到 Harbor（全部或指定几个）
│   ├── new-openiddict-certs.ps1 | .sh  # 生成认证中心与两个 BFF 的 OpenIddict 密钥证书
│   ├── new-bodyenc-certs.ps1 | .sh     # 生成两个 BFF 的 body 加密密钥证书（浏览器 ⇄ BFF）
│   └── check-env-sync.ps1 | .sh        # 核对 .env 和 .env.example 的变量名是否一致
├── nginx/msa.conf               # 入口站点参考配置（三个前端 + 回调 + 对象存储），不在编排里，放进 nginx 的 conf.d/
├── .env.example                 # 模板；复制成 .env（.env 与 certs/ 不入库）
├── certs/                       # OpenIddict 密钥、body 加密密钥与 cert-passwords.env（脚本生成）
└── README.md
```

每个服务一个文件，只写它自己的东西：镜像、监听端点、连接串、它要调用的下游地址。
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
Copy-Item .env.example .env                       # 填域名、基础设施地址、客户端密钥；MSA_IMAGE_TAG 选版本
./scripts/check-env-sync.ps1                      # 核对 .env 和 .env.example 的变量名是否一致（改了一个别忘另一个）
./scripts/new-openiddict-certs.ps1                # 认证中心与两个 BFF 的 OpenIddict 密钥（已有则保留）
./scripts/new-bodyenc-certs.ps1                   # 两个 BFF 的 body 加密密钥（已有则保留）
# 按部署修改 config/identity/appsettings.json 里两个 BFF 的回调地址
docker compose pull                               # 从 Harbor 拉镜像
docker compose up -d                              # 全部
docker compose up -d msa-catalog msa-inventory    # 只起几个
docker compose logs -f msa-order
```

首次运行要建库时把 `.env` 里的 `MSA_MIGRATE_ON_STARTUP` 设为 `true`。
跟踪 `latest` 时把 `MSA_PULL_POLICY` 设为 `always`，每次 `up` 都会拉最新镜像；固定版本用默认的 `missing`。

## 服务与端点

| 服务 | 容器名 | HTTP（REST、健康检查） | gRPC（HTTP/2） | 对外（经入口代理） |
|---|---|---|---|---|
| API 网关 | msa-gateway | 8080 | — | 否 |
| 认证中心 | msa-identity | 8080 | — | `https://identity.${MSA_DOMAIN}`（与登录页同域） |
| 用户 | msa-user | 8080 | 8081 | 否 |
| 目录 | msa-catalog | 8080 | 8081 | 否 |
| 库存 | msa-inventory | 8080 | 8081 | 否 |
| 购物车 | msa-basket | 8080 | — | 否 |
| 订单 | msa-order | 8080 | 8081 | 否 |
| 支付 | msa-payment | 8080 | 8081 | 只有 `/payment/callbacks/` |
| 通知 | msa-notification | 8080 | — | 否 |
| 商城 BFF | msa-bff-web | 8080 | — | `https://shop.${MSA_DOMAIN}` 下的 `/api`、`/bff`（与商城前端同域） |
| 后台 BFF | msa-bff-bm | 8080 | — | `https://admin.${MSA_DOMAIN}` 下的 `/api`、`/bff`（与后台前端同域） |

- **服务之间**：网关转发、BFF 转发到网关都是 `http://<容器名>:8080`；gRPC 是 `http://<容器名>:8081`。
  明文 HTTP/2 需要单独的 Http2 端点（Kestrel 不会在明文端口上协商 HTTP/2），所以提供 gRPC 的服务多开一个 8081。
- **认证中心是唯一的例外**：浏览器登录跳转必须走 `MSA_ISSUER`（`https://identity.${MSA_DOMAIN}/`），
  OpenIddict 要求它是 HTTPS 且与令牌里的 `iss` 完全一致。但各服务校验令牌拉 JWKS、取服务令牌（client_credentials）
  是后端对后端，不经浏览器，直接用容器内地址 `MSA_IDENTITY_INTERNAL_ADDRESS`（`http://msa-identity:8080`），
  不用绕入口代理出去再回来。

## 配置分层

| 内容 | 放在哪 | 为什么 |
|---|---|---|
| PostgreSQL / Redis / RabbitMQ / 对象存储的地址与口令、服务令牌授权地址、客户端密钥 | `.env` 里的 `INFRA_*`、`MSA_SERVICE_AUTHORITY`、`MSA_CLIENT_SECRET_*`，原名透传给容器 | 与本地开发的 `msa.env`（仓库根目录）**同名同义**，应用只有一套读取流程，见下文 |
| Issuer、下游地址等其他扁平配置 | 各服务 yaml 的 `environment`（值来自 `.env`） | 一个键一个值，按部署替换最方便 |
| 网关的 YARP 集群、认证中心的 OpenIddict 客户端 | `config/<服务>/appsettings.json`，挂载为 `/app/appsettings.<环境>.json` | 结构化（嵌套对象、数组），拆成 `A__0__B__1` 的环境变量既难读又容易错位 |
| OpenIddict 密钥证书路径与口令 | 环境变量 `MSA_OPENIDDICT_CERT_PATH` / `MSA_OPENIDDICT_CERT_PASSWORD` | 部署机密，应用只从环境变量读取 |

ASP.NET Core 的叠加顺序是：镜像里的 `appsettings.json` → 挂载的 `appsettings.<环境>.json` → 环境变量（后者覆盖前者）。
挂载文件只写要改的部分即可（网关只改了集群地址，路由与健康检查沿用镜像里的定义）；
**数组按下标合并**，所以认证中心的客户端列表要写完整，不要只写其中几项。

### 基础设施与密钥：本地和 Docker 同一套变量

各服务启动时调用 `MSA.Common.MsaEnvironment`，把下面这些环境变量合并进已有的配置键。镜像里的 `appsettings.json`
只保留属于服务自己的部分（`Database=CatalogDb`、`defaultDatabase=2`、队列名、`ClientId`），地址与口令一律来自变量：

| 变量 | 合并进 |
|---|---|
| `INFRA_POSTGRES_HOST` / `PORT` / `USER` / `PASSWORD` | `ConnectionStrings:Postgresql`（认证中心是 `DefaultConnection`） |
| `INFRA_REDIS_HOST` / `PORT` / `PASSWORD` | `ConnectionStrings:Redis`（口令不能含逗号） |
| `INFRA_RABBITMQ_HOST` / `PORT` / `USER` / `PASSWORD` / `VHOST` | `RabbitMQ:*` |
| `INFRA_OSS_ENDPOINT` / `ACCESS_KEY` / `SECRET_KEY` | `ObjectStorage:*`（后台 BFF） |
| `MSA_SERVICE_AUTHORITY` | `ServiceAuthentication:Authority`（取服务令牌，值是容器内地址） |
| `MSA_OTLP_ENDPOINT` | `OpenTelemetry:OtlpEndpoint`（全部 11 个服务，含网关与两个 BFF） |
| `MSA_GRPC_<服务>_ADDRESS` | `GrpcClients:<服务>:Address`（按被调用的服务命名，例如 `MSA_GRPC_CATALOG_ADDRESS`；Docker 里是 `http://msa-catalog:8081`） |

- **客户端密钥不进配置**：`MSA_CLIENT_SECRET_<ClientId 大写>` 由用到的地方按 ClientId 直接读环境变量（`MsaEnvironment.ClientSecret`）——
  `ServiceAuthenticationOptions.ClientSecret`、认证中心注册客户端、BFF 的 OpenIddict 客户端；appsettings 里写了 `ClientSecret` 也不生效。
- **本地开发**：仓库根目录的 `msa.env`（从服务目录逐级向上查找）在启动时写进进程环境变量，已设置的不覆盖。
- **Docker**：`base/dotnet-service.yaml` 的 `dotnet-infra-service` 把 `INFRA_*` 透传给除网关外的所有服务；
  `MSA_SERVICE_AUTHORITY` 与客户端密钥只注入需要它的容器（认证中心拿全部，其他服务只拿自己的）。`msa.env` 不进镜像（`.dockerignore`）。
- 服务声明了连接串却拿不到主机地址时直接启动失败并提示缺哪个变量；没声明的部分（比如网关没有数据库）不会被合并。

## 入口代理的要求

入口代理（nginx 等）不在本编排里，单独部署；`nginx/msa.conf` 是按下面要求写好的参考配置。为了让这些服务正常工作，它需要满足：

- **转发规则**：nginx 加入 `dotnet-infra_backend` 网络，静态页面自己提供，接口按域名转发（`nginx/msa.conf`）。
  每个前端和它调用的后端**必须同域**，且每个域名只放行下面这些路径：

  | 域名 | nginx 放行的路径 | 转发到 |
  |---|---|---|
  | `shop.${MSA_DOMAIN}` | `/api/`、`/bff/`（其他路径是 `Front/dist`） | `msa-bff-web:8080` |
  | `admin.${MSA_DOMAIN}` | `/api/`、`/bff/`（其他路径是 `BM/dist`） | `msa-bff-bm:8080` |
  | `identity.${MSA_DOMAIN}` | `/connect/`、`/.well-known/`、`/api/Auth/`（其他路径是 `Identity/dist`） | `msa-gateway:8080` → 认证中心（`config/gateway` 的 `edge-identity-*` 路由，`/api/Auth` 去掉 `/api`） |
  | `payment.${MSA_DOMAIN}` | `/payment/callbacks/` | `msa-payment:8080` |
  | `oss.${MSA_DOMAIN}` | 全部 | `base-rustfs:9000`（S3 签名包含 Host） |

  所有转发都保留原始 Host。认证中心的域名只放行这三个前缀：网关里还有不限域名的内部服务路由，不能从外部直接打到。

  为什么必须同域：BFF 的会话与防伪 Cookie 只属于提供它的域名，且 BFF 没有开 CORS，前端跨域调用拿不到会话；
  认证中心的登录 Cookie 由登录页（`/api/Auth/login`）写入，`/connect/authorize` 要在同一个域名下才读得到它。
- **带转发头**：必须带 `X-Forwarded-Proto`（以及 `X-Forwarded-For` / `X-Forwarded-Host`）。服务端已开启
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED`，靠它认出原始请求是 HTTPS：OpenIddict 只接受 HTTPS、
  BFF 会话 Cookie 带 Secure、登录回调地址也按它生成。缺了它登录会失败。
- **容器不需要访问 `MSA_ISSUER`**：校验令牌拉 JWKS、取服务令牌都走容器内地址 `MSA_IDENTITY_INTERNAL_ADDRESS`，
  不依赖入口代理、也不要求容器能解析公网域名；`MSA_ISSUER` 只用来比对令牌里的 `iss` 和拼浏览器跳转地址。
  `INFRA_OSS_ENDPOINT`（`https://oss.${MSA_DOMAIN}`）是例外：后台 BFF 签出的地址给浏览器用，它自己也用这个地址读写对象，
  所以容器仍要能访问到这个公网地址（内网 DNS 或 `extra_hosts`）。
- **SSE 不缓冲**：秒杀余量推流 `/bff/flash-sales/` 是长连接，要关掉响应缓冲（nginx：`proxy_buffering off`）
  并放长读超时（nginx：`proxy_read_timeout`）。

## 前端打包

三个前端都是 Vite 静态站点，在各自目录执行 `npm ci && npm run build`，产物在 `dist/`，交给入口代理托管（不进本编排）。
打包时读取的变量会被写死进产物，换域名要重新打包：

| 前端 | 变量（`.env.production`） | 说明 |
|---|---|---|
| `Front`（商城） | 无需设置 | `VITE_API_BASE_URL` 留空即走同源 `/bff`、`/api`；不要填跨域地址 |
| `BM`（后台） | `VITE_STOREFRONT_URL` | 领券活动分享链接指向的商城地址 |
| `Identity`（登录页） | `VITE_STORE_URL` | 登录 / 注册后没有 returnUrl 时跳回的商城地址 |

`.env.production` 里是 `msa.test.com` 的值，与 `.env` 的 `MSA_DOMAIN` 保持一致。开发时（`npm run dev` / `dev.bat`）不读这些文件，
仍由 Vite 代理把 `/bff`、`/api` 转到本机的 BFF / 网关。

## OpenIddict 密钥证书

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

## Body 加密密钥证书

`scripts/new-bodyenc-certs.ps1`（或 `.sh`）生成两个 BFF 各自的 body 加密密钥（浏览器 ⇄ BFF 的请求/响应体，
ECDH P-256，跟上面的 OpenIddict 证书是完全独立的两套密钥，口令写进同一份 `cert-passwords.env`）：

| 文件 | 用途 | 挂给谁 |
|---|---|---|
| `bff-web-bodyenc.pfx` | 商城 BFF：浏览器 ⇄ BFF 的 body 加密 | 只有 msa-bff-web |
| `bff-bm-bodyenc.pfx` | 后台 BFF：同上 | 只有 msa-bff-bm |

- ECDH P-256（KeyUsage=KeyAgreement，不是签名用途）、2 年有效期，不参与 TLS，自签名即可。
- 应用只从环境变量 `MSA_BODYENC_CERT_PATH` / `MSA_BODYENC_CERT_PASSWORD` 读取（跟 OpenIddict 那两个变量名不同，不会互相覆盖）。
- 已有的证书默认保留：换掉会让所有浏览器里缓存的服务端公钥失效（下一次请求解密失败），
  轮换后要等前端重新拉一次 `/bff/security/public-key`（默认每次页面刷新都会拉，不用特别处理，
  但正在进行中的一次请求可能会失败一次，刷新重试即可）。

## 可观测性

所有服务通过 `MSA.Observability` 把链路、指标、日志用 OTLP（gRPC）发往 `MSA_OTLP_ENDPOINT`。
可以填 Collector、Aspire Dashboard 或 Jaeger 的地址。基础设施的 Collector 如果也在 `dotnet-infra_backend` 网络里，直接写它的容器名，例如 `http://otel-collector:4317`。

## 已知限制

- 没有写健康检查与 `depends_on`：aspnet 镜像里没有 curl，服务之间的调用本身有重试，启动顺序不影响最终可用。
- 默认以 Production 运行：认证中心与两个 BFF 用 certs/ 里固定的 OpenIddict 密钥，重启不会让令牌和登录失效。
  改回 Development 时它们会在容器里临时生成密钥（重启即失效），OpenAPI 与 gRPC 详细错误也会打开。
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED` 信任任意来源的转发头：服务不映射端口、只有 Docker 网络内可达，这是成立的；
  如果以后把某个服务端口直接暴露出去，要改成只信任入口代理的地址。
- 镜像用 `sdk:10.0` 自带的 SDK 构建（`.dockerignore` 排除了锁定本机预览版 SDK 的 `global.json`）。
