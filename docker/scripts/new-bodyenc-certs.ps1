<#
.SYNOPSIS
  生成两个 BFF 的 body 消息体加密密钥证书到 docker/certs/。

.DESCRIPTION
  bff-web-bodyenc.pfx   商城 BFF：浏览器 ⇄ BFF 的请求/响应体加密（ECDH P-256，用于密钥协商，不是 TLS）
  bff-bm-bodyenc.pfx    后台 BFF：同上
  cert-passwords.env    上面两个 pfx 的随机口令，和 OpenIddict 那三个共用同一份文件

  这些证书只是 ECDH 密钥协商的载体，跟 TLS、跟 OpenIddict 的签名/加密证书是两套完全独立的密钥——
  一处泄露不会波及另一处。用 KeyUsage=KeyAgreement（不是 DigitalSignature），
  跟 OpenIddict 证书的 RSA 签名/加密用途区分开。
  已有的证书默认不覆盖——换掉会让所有使用中的客户端（浏览器里缓存的服务端公钥）解密失败，
  要换就得同时让前端重新拉一次 /bff/security/public-key；确实要轮换时加 -Force。

.EXAMPLE
  ./new-bodyenc-certs.ps1
  ./new-bodyenc-certs.ps1 -Force -Only bff-web
#>
param(
    [ValidateSet('bff-web', 'bff-bm')]
    [string[]] $Only = @('bff-web', 'bff-bm'),
    [int] $ValidityYears = 2,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$certs = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\certs'))
New-Item -ItemType Directory -Force -Path $certs | Out-Null
$passwordFile = Join-Path $certs 'cert-passwords.env'
$store = 'Cert:\CurrentUser\My'

$applications = [ordered]@{
    'bff-web' = @{ Subject = 'CN=MSA BFF Web Body Encryption'; Password = 'MSA_BFF_WEB_BODYENC_CERT_PASSWORD' }
    'bff-bm'  = @{ Subject = 'CN=MSA BFF BM Body Encryption'; Password = 'MSA_BFF_BM_BODYENC_CERT_PASSWORD' }
}

# 已有口令按原样保留，只替换被重新生成的那几项（跟 OpenIddict 那三个共用一份文件，不能整份覆盖）。
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
    ([Convert]::ToBase64String($bytes) -replace '[^A-Za-z0-9]', '').Substring(0, 32)
}

foreach ($name in $Only) {
    $app = $applications[$name]
    $path = Join-Path $certs "$name-bodyenc.pfx"
    if ((Test-Path $path) -and -not $Force) {
        Write-Host "保留已有的 $name-bodyenc.pfx（轮换请加 -Force）。"
        continue
    }

    # -KeyAlgorithm ECDH_P256 是关键：普通 ECDSA 密钥自签名后 .NET 的
    # X509Certificate2.GetECDiffieHellmanPrivateKey() 读不出来（会返回 null），
    # 必须一开始就用 ECDH 算法创建密钥。KeyUsage 用 KeyAgreement，不是签名用途。
    $certificate = New-SelfSignedCertificate -Type Custom `
        -Subject $app.Subject `
        -KeyAlgorithm ECDH_P256 `
        -KeyUsage KeyAgreement `
        -TextExtension @('2.5.29.19={critical}{text}ca=0') `
        -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears($ValidityYears) `
        -CertStoreLocation $store
    try {
        $passwords[$app.Password] = New-Password
        Export-PfxCertificate -Cert $certificate -FilePath $path `
            -Password (ConvertTo-SecureString $passwords[$app.Password] -AsPlainText -Force) `
            -CryptoAlgorithmOption AES256_SHA256 | Out-Null
        Write-Host "已生成 $name-bodyenc.pfx（到期 $($certificate.NotAfter.ToString('yyyy-MM-dd'))）。"
    }
    finally {
        Remove-Item (Join-Path $store $certificate.Thumbprint)
    }
}

$lines = @('# 由 scripts/new-openiddict-certs 和 scripts/new-bodyenc-certs 生成：密钥证书的随机口令。不入库，与 certs/ 下的证书一起保管。')
$lines += $passwords.Keys | ForEach-Object { "$_='$($passwords[$_])'" }
[IO.File]::WriteAllText($passwordFile, ($lines -join "`n") + "`n")
Write-Host "输出目录：$certs"
