#!/bin/sh
# 证书变化后让 nginx 自动 reload（nginx 自己不会重新读证书文件）。
#
# 证书服务（msa-certificate）续订 / 轮换 TLS 证书后，把 fullchain.pem、privkey.pem 写进共享的证书目录
# （先写临时文件再改名，任何时刻读到的都是完整文件）。这个脚本在 nginx 容器里每隔一段时间比较一次目录里
# 全部 .pem 的校验和，变了就先 nginx -t 校验配置，通过再 nginx -s reload（平滑重载，不断开现有连接）。
# 不需要把 docker.sock 交给任何容器。
#
# 用法：挂到官方 nginx 镜像的 /docker-entrypoint.d/ 下（必须有可执行权限），镜像启动 nginx 前会执行它，
# 它把检测循环放到后台就返回：
#   -v ./docker/nginx/cert-reload.sh:/docker-entrypoint.d/40-msa-cert-reload.sh:ro
#   -v ${MSA_TLS_DEPLOY_DIR}:/etc/nginx/certs:ro
# 可选环境变量：
#   NGINX_CERT_DIR              证书目录，默认 /etc/nginx/certs
#   NGINX_CERT_RELOAD_INTERVAL  检查间隔（秒），默认 60
set -eu

cert_dir="${NGINX_CERT_DIR:-/etc/nginx/certs}"
interval="${NGINX_CERT_RELOAD_INTERVAL:-60}"

log() {
    echo "$(date '+%Y/%m/%d %H:%M:%S') [msa-cert-reload] $*"
}

# 目录里全部 .pem 的校验和：按路径排序后连同文件名一起算，增删文件、改内容都会变。
fingerprint() {
    find "$cert_dir" -type f -name '*.pem' 2>/dev/null | sort | while IFS= read -r file; do
        echo "$file"
        cat "$file"
    done | cksum
}

watch() {
    current="$(fingerprint)"
    while sleep "$interval"; do
        latest="$(fingerprint)"
        [ "$latest" = "$current" ] && continue

        # 配置校验没通过就不更新基准，下一轮继续重试；日志里能看到失败原因。
        if nginx -t >/dev/null 2>&1; then
            nginx -s reload && current="$latest" && log "certificates changed in $cert_dir, nginx reloaded"
        else
            log "certificates changed in $cert_dir but 'nginx -t' failed, not reloading:"
            nginx -t 2>&1 | sed 's/^/    /' || true
        fi
    done
}

log "watching $cert_dir every ${interval}s"
watch &
