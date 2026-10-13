# Monitor Stack

## 服务
- OpenTelemetry Collector
- Prometheus
- Tempo
- Loki
- Grafana

## 网络
本套监控使用：
- `shop-monitor`
- `shop-front`（仅 Grafana 用于被 Nginx 反代）

先创建：

```bash
docker network create shop-monitor
docker network create shop-front
```

如果已存在则无需重复创建。

## 启动

```bash
cp .env.example .env
docker compose pull
docker compose up -d
```

## .NET 服务接入

业务服务加入 `shop-monitor`：

```yaml
networks:
  monitor:
    external: true
    name: shop-monitor
```

服务配置 OTLP：

```text
http://otel-collector:4317
```

## 数据流

```text
.NET
  |
  v
OTel Collector
  |-- Metrics --> Prometheus
  |-- Traces  --> Tempo
  `-- Logs    --> Loki

Prometheus / Tempo / Loki
            |
            v
          Grafana
```
