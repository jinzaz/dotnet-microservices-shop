<#
.SYNOPSIS
  构建 docker/images.txt 里的镜像并推送到镜像仓库（<仓库>/<镜像名>:<tag>）。

.DESCRIPTION
  构建与推送独立于 docker compose：compose 只负责从 MSA_IMAGE_REGISTRY 拉取运行。
  先选推送到哪个仓库（-Registry 可写预设名字 harbor / tencent 或完整地址，不传则交互选择），再选镜像。
  每个镜像推两个标签：-Tag 指定的版本（默认当前 git 提交的短哈希）与 latest（-NoLatest 可关闭）。
  推送前先登录对应仓库：
    docker login harbor.local                                    # Harbor 走 HTTP 时要把它配成 insecure registry
    docker login ccr.ccs.tencentyun.com --username=<账号ID>      # 腾讯云容器镜像服务

.EXAMPLE
  ./build-images.ps1                                 # 交互选择仓库，再选镜像
  ./build-images.ps1 all -Registry tencent           # 全部镜像推到腾讯云，标签 = git 短哈希 + latest
  ./build-images.ps1 catalog-service order-service -Registry harbor -Tag 1.4.0
  ./build-images.ps1 all -NoPush                     # 只构建，不推送
#>
param(
    # 要构建的镜像：all 表示全部；不传则列出清单交互选择。
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]] $Only = @(),
    # 预设名字（harbor / tencent）或完整地址；不传则交互选择。
    [string] $Registry,
    [string] $Tag,
    [string] $Platform = 'linux/amd64',
    [switch] $NoLatest,
    [switch] $NoPush
)

$ErrorActionPreference = 'Stop'
# 所有镜像共用 docker/Dockerfile（--target <镜像名>）：restore 与编译只在第一个镜像做一次，后面的镜像直接命中缓存。
$env:DOCKER_BUILDKIT = '1'
$dockerfile = Join-Path $PSScriptRoot '..\Dockerfile'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$manifest = Join-Path $PSScriptRoot '..\images.txt'

if (-not $Tag) {
    $Tag = (git -C $repository rev-parse --short HEAD).Trim()
    # 工作区有未提交的改动时加 -dirty，避免同一个标签对应两份不同的代码。
    if (git -C $repository status --porcelain) { $Tag = "$Tag-dirty" }
}

# 预设仓库。腾讯云的地址是 ccr.ccs.tencentyun.com/<命名空间>，命名空间按控制台里建的改。
$registries = @(
    [pscustomobject]@{ Key = 'harbor';  Address = 'harbor.local/msa-shop';           Description = '内网 Harbor（HTTP，需配置 insecure registry）' }
    [pscustomobject]@{ Key = 'tencent'; Address = 'ccr.ccs.tencentyun.com/msa-shop'; Description = '腾讯云容器镜像服务' }
)
if ($Registry) {
    $preset = $registries | Where-Object Key -eq $Registry
    if ($preset) { $Registry = $preset.Address }
}
else {
    Write-Host '推送到哪个镜像仓库：'
    for ($index = 0; $index -lt $registries.Count; $index++) {
        Write-Host ('  {0}. {1,-36} {2}' -f ($index + 1), $registries[$index].Address, $registries[$index].Description)
    }
    $answer = (Read-Host '输入编号，或直接输入完整地址（回车 = 1）').Trim()
    if (-not $answer) { $answer = '1' }
    if ($answer -match '^\d+$') {
        $number = [int]$answer
        if ($number -lt 1 -or $number -gt $registries.Count) { throw "编号超出范围：$answer" }
        $Registry = $registries[$number - 1].Address
    }
    else { $Registry = $answer }
}
$Registry = $Registry.TrimEnd('/')
Write-Host "镜像仓库：$Registry"

$images = @(Get-Content $manifest -Encoding UTF8 |
    Where-Object { $_ -and -not $_.TrimStart().StartsWith('#') } |
    ForEach-Object { $name, $project = -split $_; [pscustomobject]@{ Name = $name; Project = $project } })
if ($Only.Count -eq 0) {
    Write-Host '可构建的镜像：'
    for ($index = 0; $index -lt $images.Count; $index++) {
        Write-Host ('  {0,2}. {1}' -f ($index + 1), $images[$index].Name)
    }
    $answer = (Read-Host '输入 all 构建全部，或输入编号 / 镜像名（多个用空格或逗号分隔）').Trim()
    if (-not $answer) { Write-Host '未选择，已退出。'; return }
    $Only = $answer -split '[\s,]+' | Where-Object { $_ } | ForEach-Object {
        if ($_ -match '^\d+$') {
            $number = [int]$_
            if ($number -lt 1 -or $number -gt $images.Count) { throw "编号超出范围：$_" }
            $images[$number - 1].Name
        }
        else { $_ }
    }
}
if ($Only -notcontains 'all') {
    $unknown = $Only | Where-Object { $_ -notin $images.Name }
    if ($unknown) { throw "images.txt 里没有这些镜像：$($unknown -join ', ')" }
    $images = @($images | Where-Object { $_.Name -in $Only })
}

$built = @()
foreach ($image in $images) {
    $reference = "$Registry/$($image.Name)"
    $tags = @("${reference}:$Tag") + $(if ($NoLatest) { @() } else { @("${reference}:latest") })
    Write-Host "==> 构建 $($image.Name)（$($tags -join ', ')）" -ForegroundColor Cyan

    $arguments = @('build', '--file', $dockerfile, '--target', $image.Name, '--platform', $Platform)
    $tags | ForEach-Object { $arguments += @('--tag', $_) }
    $arguments += $repository
    & docker @arguments
    if ($LASTEXITCODE -ne 0) { throw "构建 $($image.Name) 失败。" }

    if (-not $NoPush) {
        foreach ($reference in $tags) {
            & docker push $reference
            if ($LASTEXITCODE -ne 0) { throw "推送 $reference 失败（是否已 docker login $($Registry.Split('/')[0])，仓库里的项目 / 命名空间是否存在？）。" }
        }
    }
    $built += $tags
}

Write-Host ''
Write-Host "完成 $($images.Count) 个镜像$(if ($NoPush) { '（未推送）' })：" -ForegroundColor Green
$built | ForEach-Object { Write-Host "  $_" }
Write-Host "运行指定版本：在 docker/.env 里设 MSA_IMAGE_REGISTRY=$Registry、MSA_IMAGE_TAG=$Tag，然后 docker compose pull && docker compose up -d"
