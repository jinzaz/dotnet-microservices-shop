#!/usr/bin/env bash
# 生成两个 BFF 的 body 消息体加密密钥证书到 docker/certs/（与 new-bodyenc-certs.ps1 产物一致）：
#   bff-web-bodyenc.pfx   商城 BFF：浏览器 ⇄ BFF 的请求/响应体加密（ECDH P-256，密钥协商，不是 TLS）
#   bff-bm-bodyenc.pfx    后台 BFF：同上
#   cert-passwords.env    上面两个 pfx 的随机口令，和 OpenIddict 那三个共用同一份文件
#
# 这些证书只是 ECDH 密钥协商的载体，跟 TLS、跟 OpenIddict 的签名/加密证书是两套完全独立的密钥。
# KeyUsage 用 keyAgreement，不是签名用途——openssl 的 EC 密钥本身不区分 ECDSA/ECDH，
# 靠这个扩展标注用途即可，.NET 加载时能正确识别成 ECDH。
#
# 用法：./new-bodyenc-certs.sh [--force] [bff-web|bff-bm ...]
#   已有证书默认保留；--force 轮换（换掉会让浏览器缓存的服务端公钥失效，要同步让前端重新拉一次公钥接口）。
set -euo pipefail
export MSYS2_ARG_CONV_EXCL="/CN="

force=false; only=()
while [ $# -gt 0 ]; do
  case "$1" in
    --force) force=true ;;
    bff-web|bff-bm) only+=("$1") ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
  shift
done
[ ${#only[@]} -gt 0 ] || only=(bff-web bff-bm)

certs="$(cd "$(dirname "$0")/.." && pwd)/certs"
mkdir -p "$certs"
passwords="$certs/cert-passwords.env"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

declare -A pw=()
if [ -f "$passwords" ]; then
  while IFS= read -r line; do
    if [[ "$line" =~ ^([A-Z0-9_]+)=\'(.*)\'$ ]]; then pw["${BASH_REMATCH[1]}"]="${BASH_REMATCH[2]}"; fi
  done < <(tr -d '\r' < "$passwords")
fi

declare -A subject=(
  [bff-web]="MSA BFF Web Body Encryption"
  [bff-bm]="MSA BFF BM Body Encryption")
declare -A variable=(
  [bff-web]=MSA_BFF_WEB_BODYENC_CERT_PASSWORD
  [bff-bm]=MSA_BFF_BM_BODYENC_CERT_PASSWORD)

for name in "${only[@]}"; do
  path="$certs/$name-bodyenc.pfx"
  if [ -f "$path" ] && [ "$force" != true ]; then
    echo "保留已有的 $name-bodyenc.pfx（轮换请加 --force）。"; continue
  fi
  password="$(openssl rand -base64 45 | tr -dc 'A-Za-z0-9' | head -c 32)"
  openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:prime256v1 -sha256 -nodes -days 730 \
    -subj "/CN=${subject[$name]}" \
    -keyout "$work/$name.key" -out "$work/$name.crt" \
    -addext "basicConstraints=critical,CA:FALSE" \
    -addext "keyUsage=critical,keyAgreement" 2>"$work/openssl.log" \
    || { cat "$work/openssl.log" >&2; exit 1; }
  openssl pkcs12 -export -inkey "$work/$name.key" -in "$work/$name.crt" -out "$path" -passout "pass:$password"
  pw["${variable[$name]}"]="$password"
  echo "已生成 $name-bodyenc.pfx。"
done

# 跟 OpenIddict 那三个共用一份文件：先读旧内容合并，再整份重写，不丢失没被轮换的那几项。
if [ -f "$passwords" ]; then
  while IFS= read -r line; do
    if [[ "$line" =~ ^([A-Z0-9_]+)=\'(.*)\'$ ]] && [ -z "${pw[${BASH_REMATCH[1]}]:-}" ]; then
      pw["${BASH_REMATCH[1]}"]="${BASH_REMATCH[2]}"
    fi
  done < <(tr -d '\r' < "$passwords")
fi

{
  echo "# 由 scripts/new-openiddict-certs 和 scripts/new-bodyenc-certs 生成：密钥证书的随机口令。不入库，与 certs/ 下的证书一起保管。"
  for key in "${!pw[@]}"; do
    echo "$key='${pw[$key]}'"
  done
} > "$passwords"
# pfx 本身是密码加密的 PKCS12，光有读权限拿不到私钥；容器里跑的是镜像自带的非 root 用户（跟宿主机生成
# 证书的用户不是同一个 UID），644 让它能读到文件。真正敏感的密码在 cert-passwords.env，保持 600：
# 那个文件是 docker compose 在宿主机上读的，不会进容器。
chmod 600 "$passwords" 2>/dev/null || true
chmod 644 "$certs"/*-bodyenc.pfx 2>/dev/null || true
echo "输出目录：$certs"
