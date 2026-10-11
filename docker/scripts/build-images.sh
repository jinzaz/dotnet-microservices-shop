#!/usr/bin/env bash
# 构建 docker/images.txt 里的镜像并推送到镜像仓库（<仓库>/<镜像名>:<tag>），与 build-images.ps1 等价。
# 构建与推送独立于 docker compose：compose 只负责从 MSA_IMAGE_REGISTRY 拉取运行。推送前先 docker login 对应仓库。
#
# 用法：./build-images.sh [all | 镜像名 ...] [--registry R] [--tag T] [--platform P] [--no-latest] [--no-push]
#   --registry 可以写下面预设的名字（harbor / tencent），也可以写完整地址；不传时交互选择，非交互环境默认 harbor。
#   不传镜像名时列出清单交互选择（全部或编号）；all 构建全部。
#   默认标签 = 当前 git 提交的短哈希（工作区有改动时加 -dirty），同时推 latest。
#   所有镜像共用 docker/Dockerfile（--target <镜像名>）：restore 与编译只在第一个镜像做一次，后面的镜像直接命中缓存。
set -euo pipefail
export DOCKER_BUILDKIT=1

# 预设仓库：名字 地址 说明。腾讯云的地址是 ccr.ccs.tencentyun.com/<命名空间>，命名空间按控制台里建的改。
registries=(
  "harbor  harbor.local/msa-shop            内网 Harbor（HTTP，需配置 insecure registry）"
  "tencent ccr.ccs.tencentyun.com/msa-shop  腾讯云容器镜像服务（docker login ccr.ccs.tencentyun.com --username=<账号ID>）"
)

registry=""; tag=""; platform=linux/amd64; latest=true; push=true; only=()
while [ $# -gt 0 ]; do
  case "$1" in
    --registry) registry="$2"; shift ;;
    --tag) tag="$2"; shift ;;
    --platform) platform="$2"; shift ;;
    --no-latest) latest=false ;;
    --no-push) push=false ;;
    -*) echo "unknown option: $1" >&2; exit 2 ;;
    *) only+=("$1") ;;
  esac
  shift
done

repository="$(cd "$(dirname "$0")/../.." && pwd)"
manifest="$repository/docker/images.txt"
if [ -z "$tag" ]; then
  tag="$(git -C "$repository" rev-parse --short HEAD)"
  [ -n "$(git -C "$repository" status --porcelain)" ] && tag="$tag-dirty"
fi

# --registry 写的是预设名字时换成地址；没写时交互选择（非交互环境用第一个预设）。
preset_address() {
  for entry in "${registries[@]}"; do
    read -r key address _ <<< "$entry"
    [ "$key" = "$1" ] && { echo "$address"; return 0; }
  done
  return 1
}
if [ -n "$registry" ]; then
  registry="$(preset_address "$registry" || echo "$registry")"
elif [ -t 0 ]; then
  echo "推送到哪个镜像仓库："
  for i in "${!registries[@]}"; do
    read -r _ address description <<< "${registries[$i]}"
    printf '  %d. %-36s %s\n' $((i + 1)) "$address" "$description"
  done
  read -r -p "输入编号，或直接输入完整地址（回车 = 1）：" answer
  answer="${answer:-1}"
  if [[ "$answer" =~ ^[0-9]+$ ]]; then
    [ "$answer" -ge 1 ] && [ "$answer" -le ${#registries[@]} ] || { echo "编号超出范围：$answer" >&2; exit 1; }
    read -r _ registry _ <<< "${registries[$((answer - 1))]}"
  else
    registry="$answer"
  fi
else
  read -r _ registry _ <<< "${registries[0]}"
fi
registry="${registry%/}"
echo "镜像仓库：$registry"

catalog=()
while read -r name project; do
  case "$name" in ''|'#'*) continue ;; esac
  catalog+=("$name $project")
done < <(tr -d '\r' < "$manifest")

if [ ${#only[@]} -eq 0 ]; then
  [ -t 0 ] || { echo "非交互环境请指定镜像名或 all。" >&2; exit 2; }
  echo "可构建的镜像："
  for i in "${!catalog[@]}"; do printf '  %2d. %s\n' $((i + 1)) "${catalog[$i]%% *}"; done
  read -r -p "输入 all 构建全部，或输入编号 / 镜像名（多个用空格或逗号分隔）：" answer
  [ -n "$answer" ] || { echo "未选择，已退出。"; exit 0; }
  for choice in ${answer//,/ }; do
    if [[ "$choice" =~ ^[0-9]+$ ]]; then
      [ "$choice" -ge 1 ] && [ "$choice" -le ${#catalog[@]} ] || { echo "编号超出范围：$choice" >&2; exit 1; }
      only+=("${catalog[$((choice - 1))]%% *}")
    else
      only+=("$choice")
    fi
  done
fi

selected=()
if [[ " ${only[*]} " == *" all "* ]]; then
  selected=("${catalog[@]}")
else
  for want in "${only[@]}"; do
    match=""
    for entry in "${catalog[@]}"; do [ "${entry%% *}" = "$want" ] && match="$entry"; done
    [ -n "$match" ] || { echo "images.txt 里没有镜像：$want" >&2; exit 1; }
    selected+=("$match")
  done
fi

built=()
for entry in "${selected[@]}"; do
  read -r name _ <<< "$entry"
  tags=("$registry/$name:$tag")
  [ "$latest" = true ] && tags+=("$registry/$name:latest")
  echo "==> 构建 $name（${tags[*]}）"
  args=(build --file "$repository/docker/Dockerfile" --target "$name" --platform "$platform")
  for t in "${tags[@]}"; do args+=(--tag "$t"); done
  docker "${args[@]}" "$repository"
  if [ "$push" = true ]; then
    for t in "${tags[@]}"; do
      docker push "$t" || { echo "推送 $t 失败：是否已 docker login ${registry%%/*}，仓库里的项目 / 命名空间 ${registry#*/} 是否存在？" >&2; exit 1; }
    done
  fi
  built+=("${tags[@]}")
done

echo
echo "完成 ${#selected[@]} 个镜像$([ "$push" = true ] || echo '（未推送）')："
printf '  %s\n' "${built[@]}"
echo "运行指定版本：在 docker/.env 里设 MSA_IMAGE_REGISTRY=$registry、MSA_IMAGE_TAG=$tag，然后 docker compose pull && docker compose up -d"
