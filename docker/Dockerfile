# 所有 .NET 服务共用的镜像构建文件。构建上下文是仓库根目录，用 --target <镜像名> 选服务（镜像名见 docker/images.txt）：
#   docker build -f docker/Dockerfile --target order-service -t harbor.local/msa-shop/order-service:dev .
# 平时用 docker/scripts/build-images.* 构建与推送。
#
# 分层缓存，越靠前越少变：
#   skeleton / restore  只依赖项目文件（*.csproj、*.props、*.slnx、NuGet.config）：依赖没变时整层命中缓存，不再 restore
#   publish             整个解决方案只编译一次，发布全部服务：一次构建多个镜像时共用这一层，公共库不再重复编译
#   <镜像名>             运行时镜像只复制自己的发布产物
# 需要 BuildKit（Docker 23+ 默认启用）：restore 用到了 RUN --mount=type=cache。
ARG DOTNET_VERSION=10.0

# ---- 项目文件骨架 -------------------------------------------------------------------------------
# 源码改动会让这一步重跑，但删剩下的文件不变，COPY --from 到下一阶段时内容校验一致，restore 层照样命中缓存。
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS skeleton
WORKDIR /src
COPY . .
RUN find . -type f ! \( -name '*.csproj' -o -name '*.props' -o -name '*.targets' -o -name '*.slnx' -o -name 'NuGet.config' \) -delete \
 && find . -type d -empty -delete

# ---- restore：整个解决方案一次，所有镜像共用 ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS restore
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    NUGET_HTTP_CACHE_PATH=/nuget-http-cache
WORKDIR /src
COPY --from=skeleton /src .
# 包下载缓存挂成 BuildKit 缓存（不进镜像层）：依赖改了需要重新 restore 时，没变的包不用再下载。
RUN --mount=type=cache,id=msa-nuget-http,target=/nuget-http-cache \
    dotnet restore MicroservicesShop.slnx --configfile NuGet.config

# ---- publish：编译一次，发布 images.txt 里的全部服务 --------------------------------------------------
FROM restore AS publish
ARG BUILD_CONFIGURATION=Release
COPY src/ src/
COPY docker/images.txt /tmp/images.txt
# 第一个服务编译公共库，后面的服务复用已经编译好的库（增量编译），每个库只编译一次。
# 代码风格与分析器在本地 / CI 构建里检查，打镜像时关掉，省掉一大半编译时间。
RUN set -e; \
    tr -d '\r' < /tmp/images.txt | grep -v '^[[:space:]]*#' | grep -v '^[[:space:]]*$' > /tmp/services; \
    while read -r name project; do \
      echo "==> publish $name ($project)"; \
      dotnet publish "$project" -c "$BUILD_CONFIGURATION" -o "/app/$name" --no-restore \
        -p:UseAppHost=false -p:RunAnalyzers=false -p:EnforceCodeStyleInBuild=false; \
    done < /tmp/services

# ---- 运行时 ----------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
USER $APP_UID
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# 每个服务一个目标，名字与 images.txt 的镜像名一致。
FROM runtime AS api-gateway
COPY --from=publish /app/api-gateway .
ENTRYPOINT ["dotnet", "MSA.ApiGateway.dll"]

FROM runtime AS identity-service
COPY --from=publish /app/identity-service .
ENTRYPOINT ["dotnet", "MSA.IdentityService.dll"]

FROM runtime AS user-service
COPY --from=publish /app/user-service .
ENTRYPOINT ["dotnet", "MSA.UserService.dll"]

FROM runtime AS catalog-service
COPY --from=publish /app/catalog-service .
ENTRYPOINT ["dotnet", "MSA.CatalogService.dll"]

FROM runtime AS inventory-service
COPY --from=publish /app/inventory-service .
ENTRYPOINT ["dotnet", "MSA.InventoryService.dll"]

FROM runtime AS basket-service
COPY --from=publish /app/basket-service .
ENTRYPOINT ["dotnet", "MSA.BasketService.dll"]

FROM runtime AS order-service
COPY --from=publish /app/order-service .
ENTRYPOINT ["dotnet", "MSA.OrderService.dll"]

FROM runtime AS payment-service
COPY --from=publish /app/payment-service .
ENTRYPOINT ["dotnet", "MSA.PaymentService.dll"]

FROM runtime AS notification-service
COPY --from=publish /app/notification-service .
ENTRYPOINT ["dotnet", "MSA.NotificationService.dll"]

FROM runtime AS certificate-service
COPY --from=publish /app/certificate-service .
ENTRYPOINT ["dotnet", "MSA.CertificateService.dll"]

FROM runtime AS search-service
COPY --from=publish /app/search-service .
ENTRYPOINT ["dotnet", "MSA.SearchService.dll"]

FROM runtime AS bff-web
COPY --from=publish /app/bff-web .
ENTRYPOINT ["dotnet", "MSA.BFF.Web.dll"]

FROM runtime AS bff-bm
COPY --from=publish /app/bff-bm .
ENTRYPOINT ["dotnet", "MSA.BFF.BM.dll"]
