# Monitor Stack

## 服务
- OpenTelemetry Collector：接收各服务的 OTLP，链路做尾部采样后分发
- Prometheus：指标
- Tempo：链路（一次请求经过网关、BFF、各服务、数据库、消息队列的完整调用树）
- Loki：日志
- Grafana：统一查看，链路 ↔ 日志可以互相跳转；链路能跳到所在服务的 HTTP 指标，指标上的 exemplar 点能跳回链路

Prometheus 抓 Collector 时 `honor_labels: true`：服务名（`service.name`，即程序集名，如 `MSA.OrderService`）在 `job` 标签上。
RabbitMQ 另抓 `/metrics/detailed` 的队列指标（`rabbitmq_detailed_queue_*`，带 `queue` 标签）。

## 网络
与 MSA 服务共用 `dotnet-infra_backend`（Collector 收数据、Prometheus 抓 RabbitMQ）和
`dotnet-infra_frontend`（Grafana 被入口代理转发），监控组件之间用自己的 `monitor` 网络。
前两个网络由基础设施（dotnet-infra）创建；单独部署这台机器时先手动创建：

```bash
docker network create dotnet-infra_backend
docker network create dotnet-infra_frontend
```

## 启动

```bash
cp .env.example .env
docker compose pull
docker compose up -d
```

## .NET 服务接入

不用改服务：docker/.env 里 `MSA_OTLP_ENDPOINT=http://monitor-otel-collector:4317`，
Collector 固定了这个容器名，并加入了服务所在的 `dotnet-infra_backend`。

## 数据流

```text
.NET（全量上报，不在 SDK 里采样）
  |
  v
OTel Collector（memory_limiter → 链路尾部采样 → batch）
  |-- Metrics --> Prometheus   保留 7d / 2GB
  |-- Traces  --> Tempo        保留 72h，只存错误、慢请求（>500ms）和 20% 的正常请求
  `-- Logs    --> Loki         保留 168h（compactor 删除）

Prometheus / Tempo / Loki
            |
            v
          Grafana
```

## 资源与保留

默认值按 2C4G 小机器设置，全部在 `.env` 里调（见 `.env.example`）：

| 组件 | 内存上限 | 保留 | 主要调节项 |
|---|---|---|---|
| Collector | 256m | — | `TRACE_SAMPLING_PERCENT`、`TRACE_SLOW_MS` |
| Prometheus | 512m | 7d 或 2GB | `PROMETHEUS_RETENTION`、`PROMETHEUS_RETENTION_SIZE` |
| Tempo | 384m | 72h | `TEMPO_RETENTION` |
| Loki | 384m | 168h | `LOKI_RETENTION`（24h 的整数倍） |
| Grafana | 512m | — | 13.x 空闲约 200MiB，不要再压回 256m |

所有 Go 组件都设置了 GOMEMLIMIT（Prometheus 3 与 Tempo 按容器上限自动设置），
内存快到上限时先加紧 GC，而不是直接被 OOM 杀掉。

Grafana 只绑本机 `127.0.0.1:3000`；远程临时查看用 `ssh -L 3000:127.0.0.1:3000 <主机>`，正式访问走入口代理。
