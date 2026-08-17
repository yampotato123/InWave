<#
.SYNOPSIS
    端對端驗證:情緒可不選(設計文件 §3.2.1)。

.DESCRIPTION
    驗證「不選情緒交給 AI 判讀」這條路的伺服器端行為,以及「有選情緒」沒有被改壞。
    只打 HTTP,不碰資料庫,也不需要 AI 或 YouTube 金鑰。

.EXAMPLE
    # 1. 先用拋棄式資料庫把站台跑起來(不要碰 mymusicbuddy.db)
    $env:ConnectionStrings__DefaultConnection = "Data Source=$env:TEMP\e2e.db"
    dotnet run --urls http://localhost:5130

    # 2. 另開一個視窗
    .\scripts\e2e-mood.ps1

.NOTES
    - 本檔存為 UTF-8 **with BOM**。無 BOM 的 .ps1 在 Windows PowerShell 5.1 會被當成
      Big5 解碼,中文註解會讓 parser 直接噴「字串遺漏結尾字元」。
    - 跑完會在 wwwroot/uploads/ 留下 3 個上傳檔(每次 New-Work 一個),結束時自動清掉。
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5130'
)

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path $PSScriptRoot -Parent
$img        = Join-Path $PSScriptRoot 'fixtures\test.jpg'
$uploadsDir = Join-Path $repoRoot 'wwwroot\uploads'

if (-not (Test-Path $img)) { throw "找不到測試圖:$img" }

try   { $null = Invoke-WebRequest "$BaseUrl/Works/Create" -UseBasicParsing -TimeoutSec 8 }
catch { throw "站台沒有在 $BaseUrl 回應。先跑 dotnet run --urls $BaseUrl(見本檔 .EXAMPLE)" }

$startedAt = Get-Date
$pass = 0; $fail = 0

function Check($name, $cond, $detail) {
    if ($cond) { $script:pass++; "  [PASS] $name" }
    else       { $script:fail++; "  [FAIL] $name`n         $detail" }
}

# Razor 會把中文屬性值編碼成 &#x6696;(正確且安全的行為),比對前一律 decode,
# 否則會誤判成 bug。見 PROGRESS.md「踩過的坑」第 2 條。
function D($s) { [System.Net.WebUtility]::HtmlDecode($s) }

$sess  = New-Object Microsoft.PowerShell.Commands.WebRequestSession

"=== A. Create 頁 ==="
$create = Invoke-WebRequest "$BaseUrl/Works/Create" -WebSession $sess -UseBasicParsing
$h = D $create.Content
Check "有『讓 AI 判斷』選項"     ($h -match 'id="mood-auto"')             "找不到 mood-auto"
Check "『讓 AI 判斷』是預設選取" ($h -match 'id="mood-auto"[^>]*checked')  "mood-auto 沒有 checked"
Check "八種情緒仍在"            ($h -match '夜色' -and $h -match '慶祝')  "情緒 pill 不見了"
Check "說明文字有出現"          ($h -match '交給 AI 則由照片內容決定')     "缺說明"

$token = ([regex]'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Match($create.Content).Groups[1].Value
Check "拿到防偽權杖"            ($token.Length -gt 20)                    "token=$token"

# multipart 要自己組:文字欄位用 UTF-8,檔案用原始位元組。
# 整包用 iso-8859-1 轉字串會把中文吃掉,伺服器收到亂碼卻仍被驗證擋下,
# 於是「擋下了」看起來像通過——假通過比失敗更危險,所以這裡不能便宜行事。
function New-Work([string]$mood) {
    $bnd = "----b$([guid]::NewGuid().ToString('N'))"
    $LF  = "`r`n"
    $u8  = [Text.Encoding]::UTF8
    $buf = New-Object 'System.Collections.Generic.List[byte]'

    $buf.AddRange($u8.GetBytes("--$bnd$LF"))
    $buf.AddRange($u8.GetBytes("Content-Disposition: form-data; name=`"__RequestVerificationToken`"$LF$LF$token$LF"))
    $buf.AddRange($u8.GetBytes("--$bnd$LF"))
    $buf.AddRange($u8.GetBytes("Content-Disposition: form-data; name=`"moodName`"$LF$LF$mood$LF"))
    $buf.AddRange($u8.GetBytes("--$bnd$LF"))
    $buf.AddRange($u8.GetBytes("Content-Disposition: form-data; name=`"photoFile`"; filename=`"t.jpg`"$LF"))
    $buf.AddRange($u8.GetBytes("Content-Type: image/jpeg$LF$LF"))
    $buf.AddRange([IO.File]::ReadAllBytes($img))
    $buf.AddRange($u8.GetBytes("$LF--$bnd--$LF"))

    try {
        $r = Invoke-WebRequest "$BaseUrl/Works/Create" -Method Post -WebSession $sess `
             -ContentType "multipart/form-data; boundary=$bnd" `
             -Body $buf.ToArray() -MaximumRedirection 0 -UseBasicParsing
        return @{ Status = [int]$r.StatusCode; Location = "$($r.Headers.Location)"; Body = $r.Content }
    } catch {
        $resp = $_.Exception.Response
        if ($resp -and [int]$resp.StatusCode -eq 302) {
            return @{ Status = 302; Location = $resp.Headers.Location.ToString(); Body = '' }
        }
        throw
    }
}

"`n=== B. 不選情緒送出 ==="
$r1 = New-Work ''
Check "回 302 而不是退回表單" ($r1.Status -eq 302)                      "status=$($r1.Status) — 驗證可能還在擋"
Check "導向 Edit"            ($r1.Location -match '/Works/Edit/(\d+)')  "location=$($r1.Location)"
$id1 = ([regex]'/Works/Edit/(\d+)').Match($r1.Location).Groups[1].Value

$edit = D (Invoke-WebRequest "$BaseUrl/Works/Edit/$id1" -WebSession $sess -UseBasicParsing).Content
Check "Edit 顯示『待 AI 判讀』" ($edit -match 'Step 02 · 情緒 待 AI 判讀') "Edit 標題不對"
Check "BASE_KEYWORD 為 null"    ($edit -match 'const BASE_KEYWORD = null') "沒有送 null"
Check "整頁不出現 chill music"  ($edit -notmatch 'chill music')           "fallback 關鍵字被送到前端"

$rec1 = D (Invoke-WebRequest "$BaseUrl/Works/Recommend?photoId=$id1" -WebSession $sess -UseBasicParsing).Content
Check "Recommend 顯示『待 AI 判讀』" ($rec1 -match 'Step 03 · 待 AI 判讀')      "Recommend 標題不對"
Check "歌單預設名為『作品・』"       ($rec1 -match 'value="作品・\d{2}/\d{2}"') "歌單名沒處理孤兒間隔號"

"`n=== C. 有選情緒(回歸,不可壞) ==="
$r2 = New-Work '夜色'
Check "回 302" ($r2.Status -eq 302) "status=$($r2.Status)"
$id2 = ([regex]'/Works/Edit/(\d+)').Match($r2.Location).Groups[1].Value
$edit2 = D (Invoke-WebRequest "$BaseUrl/Works/Edit/$id2" -WebSession $sess -UseBasicParsing).Content
Check "Edit 顯示『夜色』"    ($edit2 -match 'Step 02 · 情緒 夜色')                          "標題不對"
Check "關鍵字仍是夜色的"      ($edit2 -match 'const BASE_KEYWORD = "late night chill music"') "BASE_KEYWORD 壞了"
$rec2 = D (Invoke-WebRequest "$BaseUrl/Works/Recommend?photoId=$id2" -WebSession $sess -UseBasicParsing).Content
Check "歌單預設名為『夜色・』" ($rec2 -match 'value="夜色・\d{2}/\d{2}"') "歌單名不對"

"`n=== D. 亂送情緒值(不可放行) ==="
$r3 = New-Work '不存在的情緒'
Check "被擋下,不是 302"     ($r3.Status -ne 302)                    "竟然放行了 status=$($r3.Status)"
Check "錯誤訊息是情緒不存在" ((D $r3.Body) -match '選到不存在的情緒') "擋下來了但理由不對,可能是別的驗證擋的"

# 清掉這次跑出來的上傳檔,不要在 uploads/ 累積孤兒檔
if (Test-Path $uploadsDir) {
    Get-ChildItem $uploadsDir -File |
        Where-Object { $_.CreationTime -ge $startedAt } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

"`n========================================"
"通過 $pass 項,失敗 $fail 項"
if ($fail -gt 0) { exit 1 }
