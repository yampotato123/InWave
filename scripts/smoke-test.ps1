# 端對端煙霧測試:走完整條流程,並確認每一頁都活著。
#
# 這支不取代單元測試——單元測試驗的是解析與挑選邏輯,這支驗的是
# 「把所有東西接起來之後,使用者真的走得完」。兩者抓到的問題不同:
# 單元測試不會發現路由改錯、view 少一個欄位、或容器沒起來。
#
# 用法:
#   .\scripts\smoke-test.ps1                    # 測容器(5121)
#   .\scripts\smoke-test.ps1 -BaseUrl http://localhost:5120   # 測本機 F5
#
# 會建立一份作品(含判讀,會花 AI 與 YouTube 配額),測完不會自動清除——
# 刪除功能本身也在測項裡,所以最後那筆會被刪掉。
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5121',
    [string]$Image = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $Image) {
    $Image = Get-ChildItem (Join-Path $repo 'wwwroot\uploads') -Filter '*.jpg' |
             Where-Object { $_.Length -gt 20KB } | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Image -or -not (Test-Path $Image)) { throw '找不到可用的測試照片,請用 -Image 指定' }

$pass = 0; $fail = 0
function Check($name, $ok, $detail) {
    if ($ok) { $script:pass++; "  [PASS] $name" }
    else     { $script:fail++; "  [FAIL] $name`n         $detail" }
}
function D($s) { [System.Net.WebUtility]::HtmlDecode($s) }

# 每次 POST 都用全新的 session。
#
# 為什麼:PowerShell 的 WebRequestSession 在「POST + 跟隨轉址」之後,
# 防偽 cookie 會與後續頁面上的權杖對不起來,下一個 POST 一律 400(空 body)。
# A/B 實測:乾淨 session → 200;先 POST 過的同一個 session → 400,表單完全相同。
# **這是測試工具的限制,不是應用程式的問題**——真實瀏覽器連續 POST 沒有這個現象。
# 這個站沒有登入,session 只承載防偽 cookie,所以每次重開完全等價。
function Invoke-FormPost([string]$GetUrl, [string]$PostUrl, [scriptblock]$BuildForm) {
    $s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $page = Invoke-WebRequest $GetUrl -WebSession $s -UseBasicParsing -TimeoutSec 300
    $token = ([regex]'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Match($page.Content).Groups[1].Value
    $form = & $BuildForm $page
    $form['__RequestVerificationToken'] = $token
    $resp = Invoke-WebRequest $PostUrl -Method Post -WebSession $s -Body $form `
        -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 120
    [pscustomobject]@{
        Status  = [int]$resp.StatusCode
        FinalUrl = "$($resp.BaseResponse.RequestMessage.RequestUri)"
        Content = [string]$resp.Content
        Page    = $page
    }
}

"=== 目標 $BaseUrl ==="
$sess = New-Object Microsoft.PowerShell.Commands.WebRequestSession

"`n--- A. 每一頁都活著 ---"
foreach ($p in @('/', '/Works', '/Works/Create')) {
    $r = Invoke-WebRequest "$BaseUrl$p" -WebSession $sess -UseBasicParsing -TimeoutSec 30 -SkipHttpErrorCheck
    Check "GET $p" ($r.StatusCode -eq 200) "回 $($r.StatusCode)"
}

"`n--- B. 導覽只有兩個入口,而且名稱一致 ---"
$layout = D (Invoke-WebRequest "$BaseUrl/Works" -WebSession $sess -UseBasicParsing).Content
$navItems = ([regex]'<nav class="iw-nav">([\s\S]*?)</nav>').Match($layout).Groups[1].Value
$navTexts = ([regex]'>([^<>]*[一-鿿][^<>]*)<').Matches($navItems) | ForEach-Object { $_.Groups[1].Value.Trim() } | Where-Object { $_ }
Check "導覽剛好兩項" ($navTexts.Count -eq 2) "實際:$($navTexts -join ' / ')"
Check "沒有殘留「建立新作品」的舊名稱" (-not ($layout -match '建立新作品')) "仍出現在頁面上"

"`n--- C. 上傳 → 修圖 → 推薦 ---"
$create = Invoke-WebRequest "$BaseUrl/Works/Create" -WebSession $sess -UseBasicParsing
$token = ([regex]'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Match($create.Content).Groups[1].Value
Check "拿到防偽權杖" ($token.Length -gt 20) "長度 $($token.Length)"

# multipart 自己組:文字用 UTF-8、檔案用原始位元組,中間不轉字串
$bnd = "----b$([guid]::NewGuid().ToString('N'))"; $LF = "`r`n"; $u8 = [Text.Encoding]::UTF8
$buf = New-Object 'System.Collections.Generic.List[byte]'
$buf.AddRange($u8.GetBytes("--$bnd$LF"))
$buf.AddRange($u8.GetBytes("Content-Disposition: form-data; name=`"__RequestVerificationToken`"$LF$LF$token$LF"))
$buf.AddRange($u8.GetBytes("--$bnd$LF"))
$buf.AddRange($u8.GetBytes("Content-Disposition: form-data; name=`"moodName`"$LF$LF$LF"))
$buf.AddRange($u8.GetBytes("--$bnd$LF"))
$buf.AddRange($u8.GetBytes("Content-Disposition: form-data; name=`"photoFile`"; filename=`"smoke.jpg`"$LF"))
$buf.AddRange($u8.GetBytes("Content-Type: image/jpeg$LF$LF"))
$buf.AddRange([IO.File]::ReadAllBytes($Image))
$buf.AddRange($u8.GetBytes("$LF--$bnd--$LF"))

# 讓它正常跟隨轉址,再從最終網址取 id。
#
# **不要用 -MaximumRedirection 0**:那會讓 PowerShell 丟例外,而丟例外時
# 回應的 Set-Cookie 不會被套進 WebRequestSession —— 防偽 cookie 就此與伺服器
# 脫節,之後所有 POST 都會拿到 400(空 body)。這個坑花了幾輪才查出來,
# 因為單獨測 SavePlaylist 都是好的,只有「先做過一次 POST」的流程才會壞。
$createResp = Invoke-WebRequest "$BaseUrl/Works/Create" -Method Post -WebSession $sess `
    -ContentType "multipart/form-data; boundary=$bnd" -Body $buf.ToArray() -UseBasicParsing -TimeoutSec 60
$loc = "$($createResp.BaseResponse.RequestMessage.RequestUri)"
$photoId = ([regex]'/Works/Edit/(\d+)').Match($loc).Groups[1].Value
Check "上傳後導向修圖頁" ($photoId -ne '') "最終網址=$loc"

$edit = D (Invoke-WebRequest "$BaseUrl/Works/Edit/$photoId" -WebSession $sess -UseBasicParsing).Content
Check "修圖頁有六個滑桿"      (([regex]'class="iw-range"').Matches($edit).Count -eq 6) "實際 $(([regex]'class="iw-range"').Matches($edit).Count) 個"
Check "修圖頁有色調曲線"      ($edit -match 'id="curve"')        "找不到 canvas#curve"
Check "修圖頁有歌單名稱欄位"  ($edit -match 'id="PlaylistName"') "找不到欄位"

$sw = [Diagnostics.Stopwatch]::StartNew()
$rec = Invoke-WebRequest "$BaseUrl/Works/Recommend?photoId=$photoId" -WebSession $sess -UseBasicParsing -TimeoutSec 300
$sw.Stop()
$recH = D $rec.Content
Check "推薦頁回 200"          ($rec.StatusCode -eq 200) "回 $($rec.StatusCode)"
$songCount = ([regex]'class="iw-track"').Matches($recH).Count
Check "有推薦歌曲"            ($songCount -gt 0) "$songCount 首"
$isAi = $recH -match 'AI 判讀:'
"         判讀路徑:$(if ($isAi) { 'AI' } else { '回落到關鍵字(AI 不可用)' })　耗時 $([int]$sw.Elapsed.TotalSeconds) 秒"
if ($isAi) {
    Check "有判讀詳情面板"    ($recH -match 'AI 怎麼看這張照片') "找不到 details"
    Check "歌曲有 AI 理由"    ($recH -match 'opacity:\.85')      "找不到理由文字"
}
Check "名稱欄位沒有預填"      ($recH -match 'name="PlaylistName"[^>]*value=""') "被預填了"

"`n--- D. 快取:第二次進推薦頁不該再打 AI ---"
$sw2 = [Diagnostics.Stopwatch]::StartNew()
$null = Invoke-WebRequest "$BaseUrl/Works/Recommend?photoId=$photoId" -WebSession $sess -UseBasicParsing -TimeoutSec 120
$sw2.Stop()
Check "再訪明顯變快" ($sw2.Elapsed.TotalSeconds -lt [Math]::Max(3, $sw.Elapsed.TotalSeconds / 2)) `
    "首次 $([int]$sw.Elapsed.TotalSeconds)s / 再訪 $([math]::Round($sw2.Elapsed.TotalSeconds,1))s"
"         首次 $([int]$sw.Elapsed.TotalSeconds) 秒 → 再訪 $([math]::Round($sw2.Elapsed.TotalSeconds,2)) 秒"

"`n--- E. 存成歌單(名稱留白,應由伺服器補上建議名) ---"
$save = Invoke-FormPost "$BaseUrl/Works/Recommend?photoId=$photoId" "$BaseUrl/Works/SavePlaylist" {
    param($page)
    $f = @{ PhotoId = $photoId; PlaylistName = '' }
    foreach ($m in ([regex]'name="Songs\[(\d+)\]\.VideoId" value="([^"]*)"').Matches($page.Content)) {
        $idx = $m.Groups[1].Value
        $f["Songs[$idx].VideoId"] = $m.Groups[2].Value
        foreach ($fld in @('Title','Artist','ThumbnailUrl','Why')) {
            $f["Songs[$idx].$fld"] = D ([regex]"name=`"Songs\[$idx\]\.$fld`" value=`"([^`"]*)`"").Match($page.Content).Groups[1].Value
        }
        $f["Songs[$idx].Selected"] = 'true'
    }
    $f
}
$playlistId = ([regex]'/Works/Details/(\d+)').Match($save.FinalUrl).Groups[1].Value
Check "留白名稱也能存檔"  ($playlistId -ne '') "HTTP $($save.Status)　最終網址=$($save.FinalUrl)"

$det = D (Invoke-WebRequest "$BaseUrl/Works/Details/$playlistId" -WebSession $sess -UseBasicParsing).Content
Check "作品頁有曲目"      (([regex]'class="iw-track').Matches($det).Count -gt 0) "沒有曲目"
Check "作品頁有進度條"    ($det -match 'id="seek"') "找不到進度條"

"`n--- F. 收藏庫 ---"
$idx2 = D (Invoke-WebRequest "$BaseUrl/Works" -WebSession $sess -UseBasicParsing).Content
Check "新歌單出現在收藏庫" ($idx2 -match "data-id=`"$playlistId`"") "找不到 data-id=$playlistId"
Check "有刪除按鈕"         ($idx2 -match 'id="deleteBtn"')          "找不到刪除鍵"

"`n--- G. 刪除(順便清掉這次測試留下的資料) ---"
$before = (Get-ChildItem (Join-Path $repo 'wwwroot\uploads') -File).Count
$del = Invoke-FormPost "$BaseUrl/Works" "$BaseUrl/Works/Delete" { param($page) @{ id = $playlistId } }
$delLoc = $del.FinalUrl
Start-Sleep -Seconds 1
$after = (Get-ChildItem (Join-Path $repo 'wwwroot\uploads') -File).Count
Check "刪除後導回收藏庫"   ($delLoc -match '/Works') "Location=$delLoc"
Check "照片檔案一併刪除"   ($after -lt $before)      "刪除前 $before / 後 $after"
$idx3 = D (Invoke-WebRequest "$BaseUrl/Works" -WebSession $sess -UseBasicParsing).Content
Check "歌單已從收藏庫消失" (-not ($idx3 -match "data-id=`"$playlistId`"")) "還在"

"`n========================================"
"通過 $pass 項　失敗 $fail 項"
"========================================"
if ($fail -gt 0) { exit 1 }
