# Elasticsearch + IK

商品搜索服务（`msa-search`）用的 Elasticsearch，带 IK 中文分词插件。和 PostgreSQL、RabbitMQ 一样属于基础设施，不在 `docker/compose.yaml` 里，单独部署。

- `Dockerfile`：官方镜像 + IK 插件，两者版本号必须完全一致（`ES_VERSION`）。
- `compose.yaml`：单节点，加入 `dotnet-infra_backend` 网络，容器名 `elasticsearch`，**不映射端口**。
- `compose.dev.yaml`：本地开发用的叠加文件，把 9200 映射到宿主机。

## 启动

把本目录的 `.env.example` 复制为 `.env`（已在 .gitignore 里，不入库），填上自己定的 `ELASTIC_PASSWORD`。它只在第一次启动（数据卷为空）时写入 ES，之后再改 `.env` 不生效，改口令见 `.env.example` 里的说明。

生产环境，只允许 `dotnet-infra_backend` 里的容器访问：

```bash
docker compose up -d --build
```

本地开发，本机 IDE 里跑的搜索服务要通过 `192.168.1.50:9200` 访问：

```bash
docker compose -f compose.yaml -f compose.dev.yaml up -d --build
```

Linux 宿主机需要先执行 `sysctl -w vm.max_map_count=262144`（写进 `/etc/sysctl.conf` 持久化）。

## 给搜索服务建最小权限的 API Key

不要让搜索服务使用 `elastic` 超级用户。下面这把 Key 只能操作搜索索引（`catalog-products*`，包括重建锁索引 `catalog-products_rebuild_lock`）和同义词集合：

```bash
docker exec elasticsearch curl -sS -u "elastic:$ELASTIC_PASSWORD" -H 'Content-Type: application/json' -X POST http://localhost:9200/_security/api_key -d '{"name":"msa-search","role_descriptors":{"msa_search":{"cluster":["monitor","manage_search_synonyms"],"indices":[{"names":["catalog-products*"],"privileges":["all"]}]}}}'
```

开启了 TLS（见下一节）时，地址换成 `https://`，并用 CA 校验证书：`curl -sS --cacert config/certs/ca/ca.crt ... https://localhost:9200/...`。
还用 `http://` 时 ES 日志报 `received plaintext http traffic on an https channel`；不带 `--cacert` 时报 `unknown_ca`。

- 命令里的 `$ELASTIC_PASSWORD` 读的是当前 shell 的环境变量：先 `export ELASTIC_PASSWORD=...`，或者直接替换成口令。
- `-sS`：不显示进度条，但出错时打印原因。只用 `-s` 时连接被拒、证书不受信任都没有任何输出。
- 把返回结果里的 `encoded` 填进 `INFRA_ELASTICSEARCH_API_KEY`（本地是 `msa.env`，容器是 `docker/.env`）。填了 API Key 后，用户名口令就不再使用。
- `monitor`：搜索服务需要它查询集群健康状态（重建时等分片就绪）。
- `manage_search_synonyms`：启动时写入同义词集合需要它。
- 吊销：`DELETE /_security/api_key`，请求体为 `{"name":"msa-search"}`。

## 开启 TLS（跨机器部署时）

默认走容器内网明文，和本仓库其他内部流量用同一套安全模型：网络隔离 + 最小权限凭据。ES 与搜索服务不在同一台宿主机、流量要经过物理网络时，再开启 TLS。

1. 在 `compose.yaml` 里打开注释掉的 TLS 配置：
   - `setup` 服务及 `depends_on`；
   - 四行 `xpack.security.http.ssl.*`，同时删掉 `xpack.security.http.ssl.enabled: "false"`；
   - 证书卷 `es-certs`（服务的挂载与 `volumes` 下的声明）；
   - TLS 版本的健康检查。

   跨机器访问时，把 `setup` 里证书的 `dns` / `ip` 改成搜索服务实际连接用的域名或 IP，证书里的主机名必须和它一致。
2. 启动后把 CA 公钥证书拷出来，只拷 `ca.crt`，不拷 `ca.key`：

   ```bash
   docker cp elasticsearch:/usr/share/elasticsearch/config/certs/ca/ca.crt ../certs/es-http-ca.crt
   ```

3. 修改搜索服务的配置：
   - **容器部署**：打开 `services/commerce/search.yaml` 里的证书挂载，并在 `docker/.env` 里设置 `INFRA_ELASTICSEARCH_URL=https://...` 和 `INFRA_ELASTICSEARCH_CA_CERT=/certs/es-http-ca.crt`。
   - **本地开发**：在 `msa.env` 里把 `INFRA_ELASTICSEARCH_CA_CERT` 指向这个文件。

   CA 路径写错时，搜索服务会在启动时直接报错退出。
4. 多节点集群开启认证后，节点之间的 9300 传输层**必须**开 TLS，否则过不了 ES 的启动检查。`compose.yaml` 里也写好了对应的 `xpack.security.transport.ssl.*` 配置，同样是注释状态。

更多设计说明见 [docs/search-service.md](../../docs/search-service.md)。
