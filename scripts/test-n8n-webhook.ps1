# 直接打 n8n 的 inwave/analyze webhook，驗證階段 3 步驟 1 的工作流。
# 不經過 C#，所以 C# 出問題時可以用它分辨是哪一端壞掉。
#
# 用法：
#   .\scripts\test-n8n-webhook.ps1 -Image .\scripts\fixtures\test.jpg
#   .\scripts\test-n8n-webhook.ps1 -Image <照片> -Mood 夜色 -Filter 冷夜 -Brightness 130
#
# 不給 -Mood / -Filter 時整個欄位不送（契約如此，不是送空字串）。
# token 從專案根目錄的 .env 讀 N8N_WEBHOOK_TOKEN，不會出現在指令列或輸出。
param(
  [Parameter(Mandatory = $true)][string]$Image,
  [string]$Mood,
  [string]$Filter,
  [int]$Brightness = 100,
  [int]$Contrast = 100,
  [int]$Saturation = 100
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

# token 從 .env 讀，不落在指令列也不印出來
$token = ((Get-Content "$repo\.env" | Where-Object { $_ -match '^N8N_WEBHOOK_TOKEN=' }) -split '=', 2)[1].Trim()
if (-not $token) { throw '.env 沒有 N8N_WEBHOOK_TOKEN' }

$bytes = [System.IO.File]::ReadAllBytes($Image)
$mime = switch ([System.IO.Path]::GetExtension($Image).ToLower()) {
  '.png' { 'image/png' } '.webp' { 'image/webp' } default { 'image/jpeg' }
}

$body = @{
  imageBase64 = [Convert]::ToBase64String($bytes)
  mimeType    = $mime
  sliders     = @{ brightness = $Brightness; contrast = $Contrast; saturation = $Saturation }
  style       = 'default'
}
# 契約：沒選情緒就整個欄位不送（不是送空字串）
if ($Mood)   { $body.mood = $Mood }
if ($Filter) { $body.filter = $Filter }

"送出： $([int]($bytes.Length/1KB)) KB $mime｜mood=$(if($Mood){$Mood}else{'<不送>'})｜filter=$(if($Filter){$Filter}else{'<不送>'})｜sliders=$Brightness/$Contrast/$Saturation"

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$r = Invoke-WebRequest -Uri 'http://localhost:5678/webhook/inwave/analyze' `
  -Method POST -TimeoutSec 180 -SkipHttpErrorCheck `
  -Headers @{ 'X-InWave-Token' = $token } `
  -ContentType 'application/json' `
  -Body ($body | ConvertTo-Json -Depth 5 -Compress)
$sw.Stop()

"HTTP $($r.StatusCode)　耗時 $([int]$sw.Elapsed.TotalSeconds) 秒"
$r.Content
