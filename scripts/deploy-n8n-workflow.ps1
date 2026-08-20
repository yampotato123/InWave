# 把 n8n-workflows/inwave-analyze.json 部署到執行中的 n8n。
#
# 為什麼要有這支:先前的流程是
#     docker cp → n8n import:workflow → docker restart
# CLI 改的是資料庫,而 webhook 的註冊表在**記憶體**,所以非重啟不可。
# 而重啟一次要 43 秒(實測:優雅關機 22s + 初始化與註冊 17s + Docker 4s)。
#
# 改走 REST API 之後,n8n 會當場註冊 webhook,不必重啟。
#
# 用法:
#   .\scripts\build-n8n-workflow.ps1      # 產生 JSON
#   .\scripts\deploy-n8n-workflow.ps1     # 部署並啟用
#
# 前置:在 n8n UI 產一把 API key(Settings → n8n API → Create an API key),
#       把它加進專案根目錄的 .env:
#           N8N_API_KEY=n8n_api_xxxxxxxx
#       .env 已被 .gitignore 擋住,不會進版控。
#
# 沒有 API key 時會自動退回舊的 CLI + 重啟路徑,腳本仍然可用(只是慢)。
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5678',
    [string]$WorkflowId = 'inwaveAnalyze001',
    [string]$ContainerName = 'n8n'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$jsonPath = Join-Path $repo 'n8n-workflows\inwave-analyze.json'
if (-not (Test-Path $jsonPath)) { throw "找不到 $jsonPath,先跑 build-n8n-workflow.ps1" }

function Get-EnvValue([string]$key) {
    $envFile = Join-Path $repo '.env'
    if (-not (Test-Path $envFile)) { return $null }
    $line = Get-Content $envFile | Where-Object { $_ -match "^$key=" } | Select-Object -First 1
    if (-not $line) { return $null }
    $v = ($line -split '=', 2)[1].Trim()
    if ([string]::IsNullOrWhiteSpace($v)) { return $null }
    return $v
}

$apiKey = Get-EnvValue 'N8N_API_KEY'

# ── 沒有 API key:退回 CLI + 重啟 ─────────────────────────────────
if (-not $apiKey) {
    Write-Host '.env 沒有 N8N_API_KEY,改用 CLI + 重啟(較慢,約 45 秒)。' -ForegroundColor Yellow
    docker cp $jsonPath "${ContainerName}:/tmp/wf.json" | Out-Null
    docker exec $ContainerName n8n import:workflow --input=/tmp/wf.json
    docker exec $ContainerName n8n update:workflow --id=$WorkflowId --active=true | Out-Null
    docker restart $ContainerName | Out-Null
    Write-Host '等待 n8n 就緒…'
    $deadline = (Get-Date).AddSeconds(180)
    do {
        Start-Sleep -Seconds 5
        $ready = try {
            (Invoke-WebRequest "$BaseUrl/webhook/inwave/analyze" -Method Post -Body '{}' `
                -ContentType 'application/json' -TimeoutSec 10 -SkipHttpErrorCheck).StatusCode -eq 403
        } catch { $false }
    } while (-not $ready -and (Get-Date) -lt $deadline)
    if ($ready) { Write-Host '完成(CLI 路徑)。' -ForegroundColor Green } else { throw 'n8n 未在時限內就緒' }
    return
}

# ── 有 API key:走 REST API,不重啟 ───────────────────────────────
$headers = @{ 'X-N8N-API-KEY' = $apiKey }
$wf = (Get-Content $jsonPath -Raw | ConvertFrom-Json)[0]

# public API 會拒絕不認得的欄位,只送它接受的那幾個。
# id / active / meta / pinData 都不能出現在 body 裡:
#   id     由網址帶
#   active 由 /activate 端點控制,不是可寫欄位
$payload = @{
    name        = $wf.name
    nodes       = $wf.nodes
    connections = $wf.connections
    settings    = @{ executionOrder = 'v1' }
} | ConvertTo-Json -Depth 40 -Compress

$sw = [System.Diagnostics.Stopwatch]::StartNew()

$existing = Invoke-WebRequest "$BaseUrl/api/v1/workflows/$WorkflowId" -Headers $headers `
    -TimeoutSec 20 -SkipHttpErrorCheck

if ($existing.StatusCode -eq 200) {
    $r = Invoke-WebRequest "$BaseUrl/api/v1/workflows/$WorkflowId" -Method Put -Headers $headers `
        -ContentType 'application/json' -Body $payload -TimeoutSec 30 -SkipHttpErrorCheck
    $action = '更新'
}
elseif ($existing.StatusCode -eq 404) {
    $r = Invoke-WebRequest "$BaseUrl/api/v1/workflows" -Method Post -Headers $headers `
        -ContentType 'application/json' -Body $payload -TimeoutSec 30 -SkipHttpErrorCheck
    $action = '建立'
}
else {
    throw "查詢工作流失敗:HTTP $($existing.StatusCode) $($existing.Content)"
}

if ($r.StatusCode -ge 300) { throw "$action 失敗:HTTP $($r.StatusCode) $($r.Content)" }
$id = ($r.Content | ConvertFrom-Json).id
Write-Host "$action 工作流成功(id=$id)"

# 啟用。已經是啟用狀態時 API 會回 200,重複呼叫沒有副作用。
$act = Invoke-WebRequest "$BaseUrl/api/v1/workflows/$id/activate" -Method Post -Headers $headers `
    -TimeoutSec 30 -SkipHttpErrorCheck
if ($act.StatusCode -ge 300) { throw "啟用失敗:HTTP $($act.StatusCode) $($act.Content)" }

# 驗證 webhook 真的註冊了:不帶 token 應該得到 403(Header Auth 擋下),
# 而不是 404(沒註冊)。這一步是必要的——API 回 200 只代表資料寫了。
$probe = Invoke-WebRequest "$BaseUrl/webhook/inwave/analyze" -Method Post -Body '{}' `
    -ContentType 'application/json' -TimeoutSec 15 -SkipHttpErrorCheck
$sw.Stop()

if ($probe.StatusCode -eq 403) {
    Write-Host ("完成,未重啟。耗時 {0:N1} 秒。" -f $sw.Elapsed.TotalSeconds) -ForegroundColor Green
}
else {
    throw "工作流已寫入但 webhook 未註冊(探測回 $($probe.StatusCode),預期 403)。可能需要重啟 n8n。"
}
