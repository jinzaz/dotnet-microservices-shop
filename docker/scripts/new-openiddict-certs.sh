#!/usr/bin/env bash
# 生成认证中心与两个 BFF 的 OpenIddict 密钥证书到 docker/certs/（与 new-openiddict-certs.ps1 产物一致）：
#   identity-openiddict.pfx  认证中心签名 / 加密令牌（RSA 3072，2 年）
#   bff-web-openiddict.pfx   商城 BFF 签名 / 加密 OIDC state token
#   bff-bm-openiddict.pfx    后台 BFF 签名 / 加密 OIDC state token
#   cert-passwords.env       上面三个 pfx 的随机口令，docker compose 插值时读取
# 这些只是密钥载体，自签名即可；HTTPS 证书请按域名向 CA 申请（见 README）。
#
# 用法：./new-openiddict-certs.sh [--force] [identity|bff-web|bff-bm ...]
#   已有证书默认保留；--force 轮换（认证中心换密钥会让已签发的令牌失效，BFF 换密钥会让进行中的登录失败）。
set -euo pipefail
# Git Bash（MSYS）会把 -subj "/CN=..." 当成路径改写；只对以 /CN= 开头的参数关掉转换。
export MSYS2_ARG_CONV_EXCL="/CN="

force=false; only=()
while [ $# -gt 0 ]; do
  case "$1" in
    --force) force=true ;;
    identity|bff-web|bff-bm) only+=("$1") ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
  shift
done
[ ${#only[@]} -gt 0 ] || only=(identity bff-web bff-bm)

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
  [identity]="MSA Identity OpenIddict Server"
  [bff-web]="MSA BFF Web OpenIddict Client"
  [bff-bm]="MSA BFF BM OpenIddict Client")
declare -A variable=(
  [identity]=MSA_IDENTITY_OPENIDDICT_CERT_PASSWORD
  [bff-web]=MSA_BFF_WEB_OPENIDDICT_CERT_PASSWORD
  [bff-bm]=MSA_BFF_BM_OPENIDDICT_CERT_PASSWORD)

for name in "${only[@]}"; do
  path="$certs/$name-openiddict.pfx"
  if [ -f "$path" ] && [ "$force" != true ]; then
    echo "保留已有的 $name-openiddict.pfx（轮换请加 --force）。"; continue
  fi
  password="$(openssl rand -base64 45 | tr -dc 'A-Za-z0-9' | head -c 32)"
  openssl req -x509 -newkey rsa:3072 -sha256 -nodes -days 730 -subj "/CN=${subject[$name]}" \
    -keyout "$work/$name.key" -out "$work/$name.crt" \
    -addext "basicConstraints=critical,CA:FALSE" \
    -addext "keyUsage=critical,digitalSignature,keyEncipherment" 2>"$work/openssl.log" \
    || { cat "$work/openssl.log" >&2; exit 1; }
  openssl pkcs12 -export -inkey "$work/$name.key" -in "$work/$name.crt" -out "$path" -passout "pass:$password"
  pw["${variable[$name]}"]="$password"
  echo "已生成 $name-openiddict.pfx。"
done

{
  echo "# 由 scripts/new-openiddict-certs 和 scripts/new-bodyenc-certs 生成：密钥证书的随机口令。不入库，与 certs/ 下的证书一起保管。"
  # 整份写回（含 bodyenc 那两项），开头已把旧文件全部读进 pw，不能只写自己的三项。
  for key in "${!pw[@]}"; do
    echo "$key='${pw[$key]}'"
  done
} > "$passwords"
# pfx 本身是密码加密的 PKCS12，光有读权限拿不到私钥；容器里跑的是镜像自带的非 root 用户（跟宿主机生成
# 证书的用户不是同一个 UID），644 让它能读到文件。真正敏感的密码在 cert-passwords.env，保持 600：
# 那个文件是 docker compose 在宿主机上读的，不会进容器。
chmod 600 "$passwords" 2>/dev/null || true
chmod 644 "$certs"/*-openiddict.pfx 2>/dev/null || true
echo "输出目录：$certs"
