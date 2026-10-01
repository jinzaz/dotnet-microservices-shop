# 构建上下文是仓库根目录：整份源码一起复制，项目之间的引用（MSA.Grpc、MSA.Observability 等）不需要逐个列出。
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
RUN dotnet restore "src/BFFs/MSA.BFF.Web/MSA.BFF.Web.csproj"
RUN dotnet publish "src/BFFs/MSA.BFF.Web/MSA.BFF.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
USER $APP_UID
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MSA.BFF.Web.dll"]
