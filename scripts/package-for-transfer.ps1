# 把「沒進版控但換機器需要」的東西打包成一個 zip。
#
# 程式碼不必打包——它在 git 上,新機器 clone 就有。這支只處理:
#   1. user-secrets 的兩把金鑰(YouTube / n8n webhook token)
#   2. .env(容器用的金鑰退路)
#   3. n8n-data/(**憑證與加密金鑰**,沒有它 n8n 不知道你的 OpenAI 金鑰)
#   4. 測試資料(選配,-IncludeTestData)
#
# ⚠️ 產出的 zip **內含明文可用的金鑰**。n8n 的憑證雖然加密,但解密金鑰
#    就在同一個資料夾(n8n-data/config)——等於沒加密。
#    傳輸方式:隨身碟或直接接線。**不要**丟雲端硬碟、不要用 email。
#
# 用法:
#   .\scripts\package-for-transfer.ps1
#   .\scripts\package-for-transfer.ps1 -IncludeTestData      # 連照片與資料庫一起帶
[CmdletBinding()]
param(
    [string]$OutDir = [Environment]::GetFolderPath('Desktop'),
    [switch]$IncludeTestData
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$userSecretsId = ([regex]'<UserSecretsId>([^<]+)</UserSecretsId>').Match(
    (Get-Content (Join-Path $repo 'InWave.csproj') -Raw)).Groups[1].Value
if (-not $userSecretsId) { throw '在 InWave.csproj 找不到 UserSecretsId' }

# 產出不能放在 repo 裡,否則下次 git add -A 會把金鑰提交上去
$full = [IO.Path]::GetFullPath($OutDir)
if ($full.StartsWith([IO.Path]::GetFullPath($repo), 'OrdinalIgnoreCase')) {
    throw "輸出目錄不可以在 repo 內($full)——那會讓金鑰有機會被 commit。"
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$stage = Join-Path $env:TEMP "inwave-transfer-$stamp"
New-Item -ItemType Directory -Force -Path $stage | Out-Null

function Take($src, $dstName, $label) {
    if (-not (Test-Path $src)) { "  跳過 $label(來源不存在)"; return }
    $dst = Join-Path $stage $dstName
    Copy-Item $src $dst -Recurse -Force
    $sz = (Get-ChildItem $dst -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    if (-not $sz) { $sz = (Get-Item $dst).Length }
    "  收 {0,-16} {1,7} MB   {2}" -f $dstName, [math]::Round($sz / 1MB, 2), $label
}

"打包中…"
Take (Join-Path $env:APPDATA "Microsoft\UserSecrets\$userSecretsId\secrets.json") 'secrets.json' 'user-secrets 的金鑰'
Take (Join-Path $repo '.env')       'env.txt'      '容器金鑰退路(還原時改名回 .env)'
Take (Join-Path $repo 'n8n-data')   'n8n-data'     'n8n 憑證與工作流(含加密金鑰)'

if ($IncludeTestData) {
    Take (Join-Path $repo 'docker-data')      'docker-data'      '容器的 SQLite'
    Take (Join-Path $repo 'mymusicbuddy.db')  'mymusicbuddy.db'  '本機 F5 的 SQLite'
    Take (Join-Path $repo 'wwwroot\uploads')  'uploads'          '上傳的照片'
}
else {
    "  略過測試資料(要帶的話加 -IncludeTestData)"
}

# 還原腳本與說明一起放進去
Copy-Item (Join-Path $PSScriptRoot 'restore-on-new-machine.ps1') $stage -Force
@"
# 在新機器上還原

## 事前準備
- .NET 10 SDK
- Docker Desktop
- git

## 步驟

``````powershell
git clone https://github.com/yampotato123/InWave.git
cd InWave

# 把這個 zip 解開到任意位置,然後:
..\inwave-transfer-$stamp\restore-on-new-machine.ps1 -PackageDir ..\inwave-transfer-$stamp
``````

還原腳本**不會覆蓋任何已存在的檔案** —— 目的地已經有東西時它會停下來報告,
要覆蓋得自己刪掉舊的。這是刻意的:搬移工具的預設值應該是「不動目的地」。

## 還原之後

``````powershell
dotnet build InWave.slnx
docker compose up -d --build
.\scripts\smoke-test.ps1
``````

## 可能會踩到的兩件事

**1. SQLite Error 8: attempt to write a readonly database**

容器以非 root(UID 1654)執行,而從別台機器複製過來的 .db 擁有者不對。修法:

``````powershell
docker run --rm -u 0 -v "`${PWD}\docker-data:/x" -v "`${PWD}\wwwroot\uploads:/y" ``
  mcr.microsoft.com/dotnet/aspnet:10.0 chown -R 1654:1654 /x /y
``````

**2. n8n 的工作流沒有啟用**

n8n-data 帶過來的話工作流與憑證都在,但保險起見確認一次:

``````powershell
docker exec n8n n8n list:workflow
.\scripts\test-n8n-webhook.ps1 -Image .\wwwroot\uploads\<某張照片>.jpg
``````

沒帶 n8n-data 的話,工作流要重新匯入(定義在 git 裡),**憑證則必須手動重建**
(OpenAI 金鑰與 Header Auth),因為那是 n8n 加密保存的。

---
打包時間:$stamp
"@ | Set-Content (Join-Path $stage 'RESTORE.md') -Encoding utf8

$zip = Join-Path $full "InWave-transfer-$stamp.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item $stage -Recurse -Force

$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
""
"完成:$zip  ($mb MB)"
""
"⚠️  這個檔案內含可直接使用的 API 金鑰。"
"    用隨身碟或直接傳輸,不要放雲端硬碟、不要 email。"
"    到了新機器還原完之後,建議把它刪掉。"
