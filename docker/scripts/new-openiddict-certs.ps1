<#
.SYNOPSIS
  生成认证中心与两个 BFF 的 OpenIddict 密钥证书到 docker/certs/。

.DESCRIPTION
  identity-openiddict.pfx   认证中心签名 / 加密访问令牌、身份令牌（RSA 3072，2 年）
  bff-web-openiddict.pfx    商城 BFF 签名 / 加密 OIDC state token
  bff-bm-openiddict.pfx     后台 BFF 签名 / 加密 OIDC state token
  cert-passwords.env        上面三个 pfx 的随机口令，docker compose 插值时读取

  这些只是密钥载体，不参与 TLS 也不需要 CA 签发，自签名即可；HTTPS 证书请按域名向 CA 申请（见 README）。
  每个应用一份独立密钥：认证中心的令牌与 BFF 的 state token 互不共用，一处泄露不波及其他。
  已有的证书默认不覆盖 —— 换掉认证中心的密钥会让已签发的令牌全部失效，换掉 BFF 的会让进行中的登录失败；
  确实要轮换时加 -Force（可以配合 -Only 只换某一个）。

.EXAMPLE
  ./new-openiddict-certs.ps1
  ./new-openiddict-certs.ps1 -Force -Only identity
#>
param(
    [ValidateSet('identity', 'bff-web', 'bff-bm')]
    [string[]] $Only = @('identity', 'bff-web', 'bff-bm'),
    [int] $ValidityYears = 2,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$certs = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\certs'))
New-Item -ItemType Directory -Force -Path $certs | Out-Null
$passwordFile = Join-Path $certs 'cert-passwords.env'
$store = 'Cert:\CurrentUser\My'

$applications = [ordered]@{
    'identity' = @{ Subject = 'CN=MSA Identity OpenIddict Server'; Password = 'MSA_IDENTITY_OPENIDDICT_CERT_PASSWORD' }
    'bff-web'  = @{ Subject = 'CN=MSA BFF Web OpenIddict Client'; Password = 'MSA_BFF_WEB_OPENIDDICT_CERT_PASSWORD' }
    'bff-bm'   = @{ Subject = 'CN=MSA BFF BM OpenIddict Client'; Password = 'MSA_BFF_BM_OPENIDDICT_CERT_PASSWORD' }
}

# 已有口令按原样保留，只替换被重新生成的那几项。
$passwords = [ordered]@{}
if (Test-Path $passwordFile) {
    foreach ($line in Get-Content $passwordFile -Encoding UTF8) {
        if ($line -match "^\s*([A-Z0-9_]+)='(.*)'\s*$") { $passwords[$Matches[1]] = $Matches[2] }
    }
}

function New-Password {
    # 兼容 Windows PowerShell 5.1（.NET Framework 没有静态的 GetBytes(int)）。
    $bytes = New-Object byte[] 30
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    # 只保留字母数字，免得在 .env 插值里需要转义。
    ([Convert]::ToBase64String($bytes) -replace '[^A-Za-z0-9]', '').Substring(0, 32)
}

foreach ($name in $Only) {
    $app = $applications[$name]
    $path = Join-Path $certs "$name-openiddict.pfx"
    if ((Test-Path $path) -and -not $Force) {
        Write-Host "保留已有的 $name-openiddict.pfx（轮换请加 -Force）。"
        continue
    }

    # 一份证书同时承担签名（DigitalSignature）与加密（KeyEncipherment），与代码里 Certificates:Path 的用法一致。
    $certificate = New-SelfSignedCertificate -Type Custom `
        -Subject $app.Subject `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature, KeyEncipherment `
        -TextExtension @('2.5.29.19={critical}{text}ca=0') `
        -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears($ValidityYears) `
        -CertStoreLocation $store
    try {
        $passwords[$app.Password] = New-Password
        Export-PfxCertificate -Cert $certificate -FilePath $path `
            -Password (ConvertTo-SecureString $passwords[$app.Password] -AsPlainText -Force) `
            -CryptoAlgorithmOption AES256_SHA256 | Out-Null
        Write-Host "已生成 $name-openiddict.pfx（到期 $($certificate.NotAfter.ToString('yyyy-MM-dd'))）。"
    }
    finally {
        # 私钥已导出到加密的 pfx，个人存储里的副本不再需要。
        Remove-Item (Join-Path $store $certificate.Thumbprint)
    }
}

$lines = @('# 由 scripts/new-openiddict-certs 生成：OpenIddict 密钥证书的随机口令。不入库，与 certs/ 下的证书一起保管。')
$lines += $passwords.Keys | ForEach-Object { "$_='$($passwords[$_])'" }
[IO.File]::WriteAllText($passwordFile, ($lines -join "`n") + "`n")
Write-Host "输出目录：$certs"
