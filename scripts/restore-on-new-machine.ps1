# 在新機器上還原 package-for-transfer.ps1 打的包。
#
# **預設不動目的地**:任何一個目標檔案已經存在就停下來報告,不覆蓋。
# 這是刻意的——搬移工具最常見的事故是「目的地不是空的」,而覆蓋掉的東西通常救不回來。
# 真的要覆蓋,自己先刪掉舊的再跑一次。
#
# 用法(在 repo 根目錄執行):
#   .\scripts\restore-on-new-machine.ps1 -PackageDir C:\path\to\inwave-transfer-YYYYMMDD-HHMM
#   .\scripts\restore-on-new-machine.ps1 -PackageDir ... -WhatIfOnly    # 只看會做什麼
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageDir,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $PackageDir)) { throw "找不到 $PackageDir" }

# repo 位置用「執行時的目錄」判斷,不能用 $PSScriptRoot ——
# 這支腳本會被複製進傳輸包裡,那時它並不在 repo 底下。
$repo = (Get-Location).Path
if (-not (Test-Path (Join-Path $repo 'InWave.csproj'))) {
    throw "請在 repo 根目錄執行(目前在 $repo,找不到 InWave.csproj)。`n" +
          "  例:cd C:\path\to\InWave  然後再跑這支腳本。"
}

$userSecretsId = ([regex]'<UserSecretsId>([^<]+)</UserSecretsId>').Match(
    (Get-Content (Join-Path $repo 'InWave.csproj') -Raw)).Groups[1].Value
$secretsDir = Join-Path $env:APPDATA "Microsoft\UserSecrets\$userSecretsId"

# 來源 → 目的地。順序無所謂,每一項獨立判斷。
$plan = @(
    @{ From = 'secrets.json';     To = (Join-Path $secretsDir 'secrets.json'); Label = 'user-secrets 金鑰' }
    @{ From = 'env.txt';          To = (Join-Path $repo '.env');               Label = '.env' }
    @{ From = 'n8n-data';         To = (Join-Path $repo 'n8n-data');           Label = 'n8n 憑證與工作流' }
    @{ From = 'docker-data';      To = (Join-Path $repo 'docker-data');        Label = '容器 SQLite' }
    @{ From = 'mymusicbuddy.db';  To = (Join-Path $repo 'mymusicbuddy.db');    Label = '本機 SQLite' }
    @{ From = 'uploads';          To = (Join-Path $repo 'wwwroot\uploads');    Label = '上傳的照片' }
)

$todo = @(); $skipMissing = @(); $blocked = @()
foreach ($p in $plan) {
    $src = Join-Path $PackageDir $p.From
    if (-not (Test-Path $src)) { $skipMissing += $p.Label; continue }

    # docker-data 與 uploads 在 repo 裡本來就有(.gitkeep),那不算「已存在的資料」
    $exists = Test-Path $p.To
    if ($exists -and (Get-Item $p.To).PSIsContainer) {
        $hasRealFiles = @(Get-ChildItem $p.To -Recurse -File -ErrorAction SilentlyContinue |
                          Where-Object { $_.Name -ne '.gitkeep' }).Count -gt 0
        $exists = $hasRealFiles
    }

    if ($exists) { $blocked += $p } else { $todo += $p }
}

"=== 會還原 ==="
if ($todo) { $todo | ForEach-Object { "  $($_.Label)  →  $($_.To)" } } else { "  (無)" }

if ($skipMissing) {
    "`n=== 包裡沒有,略過 ==="
    $skipMissing | ForEach-Object { "  $_" }
}

if ($blocked) {
    "`n=== 目的地已有東西,**不覆蓋** ==="
    $blocked | ForEach-Object { "  $($_.Label)  →  $($_.To)" }
    "  要覆蓋的話請自己先刪掉,再跑一次。"
}

if ($WhatIfOnly) { "`n(-WhatIfOnly:沒有實際複製任何東西)"; return }
if (-not $todo)  { "`n沒有要還原的項目。"; return }

""
foreach ($p in $todo) {
    $parent = Split-Path $p.To -Parent
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    Copy-Item (Join-Path $PackageDir $p.From) $p.To -Recurse -Force
    "已還原 $($p.Label)"
}

@"

還原完成。接下來:

  dotnet build InWave.slnx
  docker compose up -d --build

  # 容器以非 root(1654)執行,從別台機器帶過來的檔案擁有者不對,會噴
  # SQLite Error 8: attempt to write a readonly database
  docker run --rm -u 0 -v "`${PWD}\docker-data:/x" -v "`${PWD}\wwwroot\uploads:/y" ``
    mcr.microsoft.com/dotnet/aspnet:10.0 chown -R 1654:1654 /x /y

  .\scripts\smoke-test.ps1
"@
