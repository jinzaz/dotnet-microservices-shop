#!/usr/bin/env bash
# 构建 docker/images.txt 里的镜像并推送到 Harbor（默认 harbor.local/msa-shop/<镜像名>:<tag>），与 build-images.ps1 等价。
# 构建与推送独立于 docker compose：compose 只负责从仓库拉取运行。推送前先 docker login harbor.local。
#
# 用法：./build-images.sh [all | 镜像名 ...] [--registry R] [--tag T] [--platform P] [--no-latest] [--no-push]
#   不传镜像名时列出清单交互选择（全部或编号）；all 构建全部。
#   默认标签 = 当前 git 提交的短哈希（工作区有改动时加 -dirty），同时推 latest。
#   所有镜像共用 docker/Dockerfile（--target <镜像名>）：restore 与编译只在第一个镜像做一次，后面的镜像直接命中缓存。
set -euo pipefail
export DOCKER_BUILDKIT=1

registry=harbor.local/msa-shop; tag=""; platform=linux/amd64; latest=true; push=true; only=()
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
    for t in "${tags[@]}"; do docker push "$t"; done
  fi
  built+=("${tags[@]}")
done

echo
echo "完成 ${#selected[@]} 个镜像$([ "$push" = true ] || echo '（未推送）')："
printf '  %s\n' "${built[@]}"
echo "运行指定版本：在 docker/.env 里设 MSA_IMAGE_TAG=$tag，然后 docker compose pull && docker compose up -d"
