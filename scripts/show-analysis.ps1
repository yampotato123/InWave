# 攤開一張照片的完整判讀過程:使用者輸入 → AI 判讀 → 送去 YouTube 的查詢 → 解析到的影片。
#
# 為什麼需要:這些資訊散在三個地方(PhotoAnalysis.RawJson、SearchCaches、log),
# 想知道「AI 到底看到什麼、為什麼推這幾首」得翻半天。
#
# 用法:
#   .\scripts\show-analysis.ps1              # 容器那份資料庫,最近一張判讀過的照片
#   .\scripts\show-analysis.ps1 -PhotoId 12
#   .\scripts\show-analysis.ps1 -All         # 全部,適合一次看十張的品質評估
#   .\scripts\show-analysis.ps1 -Local       # 改讀**本機 F5(5120)**那份
#
# **兩份資料庫是獨立的**:
#   本機 F5 (5120)  → 專案根目錄的 mymusicbuddy.db
#   容器    (5121)  → docker-data/mymusicbuddy.db
# 在哪邊測的就要看哪一份,這是容器化時刻意的分離(埠也不同,可以同時跑)。
#
# 一律唯讀複製出來再查,不動原檔。
[CmdletBinding()]
param(
    [int]$PhotoId = 0,
    [switch]$All,
    [switch]$Local,                    # 讀本機 F5 那份,而不是容器那份
    [string]$AppContainer = 'inwave',
    [string]$SqliteContainer = 'n8n'   # 借它的 node + sqlite3 當讀取器
)

$ErrorActionPreference = 'Stop'
$tmp = Join-Path $env:TEMP "inwave-show-$(Get-Random)"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

try {
    # 三個檔(.db / -shm / -wal)一起複製:只拿 .db 會讀到半套,
    # 剛寫入還在 WAL 裡的資料會看不到——正在跑的站台尤其明顯。
    New-Item -ItemType Directory -Force -Path "$tmp\db" | Out-Null

    if ($Local) {
        $repo = Split-Path $PSScriptRoot -Parent
        $src = Join-Path $repo 'mymusicbuddy.db'
        if (-not (Test-Path $src)) {
            throw "找不到 $src。本機 F5 至少要跑過一次(或 dotnet ef database update)才會有這個檔。"
        }
        Get-ChildItem $repo -Filter 'mymusicbuddy.db*' | Copy-Item -Destination "$tmp\db"
        Write-Host "資料來源:本機 F5(5120)的 $src`n" -ForegroundColor Cyan
    }
    else {
        docker exec $AppContainer sh -c 'mkdir -p /tmp/showq && cp /app/docker-data/mymusicbuddy.db* /tmp/showq/' | Out-Null
        docker cp "${AppContainer}:/tmp/showq/." "$tmp\db" | Out-Null
        Write-Host "資料來源:容器(5121)的 docker-data/mymusicbuddy.db`n" -ForegroundColor Cyan
    }

    docker exec $SqliteContainer sh -c 'mkdir -p /tmp/showq' | Out-Null
    docker cp "$tmp\db\." "${SqliteContainer}:/tmp/showq/" | Out-Null

    $filter = if ($All) { '' } elseif ($PhotoId -gt 0) { "and p.Id = $PhotoId" } else { '' }
    $limit = if ($All) { 50 } elseif ($PhotoId -gt 0) { 1 } else { 1 }

    $js = @"
const sqlite3 = require('/usr/local/lib/node_modules/n8n/node_modules/sqlite3');
const db = new sqlite3.Database('/tmp/showq/mymusicbuddy.db', sqlite3.OPEN_READONLY);

const sql = ``
  select p.Id, p.OriginalPath, p.PlaylistName,
         m.MoodName, m.IsAiFilled,
         e.FilterName, e.Brightness, e.Contrast, e.Saturation,
         e.Temperature, e.Sharpness, e.Softness, e.CurvePoints,
         a.RawJson, a.AnalyzedAt, a.ModelUsed
  from Photos p
  left join MoodProfiles m on m.PhotoId = p.Id
  left join PhotoEdits   e on e.PhotoId = p.Id
  join      PhotoAnalyses a on a.PhotoId = p.Id
  where 1=1 $filter
  order by a.AnalyzedAt desc
  limit $limit``;

db.all(sql, (err, rows) => {
  if (err) { console.error('ERR ' + err.message); return; }
  if (!rows.length) { console.log('(找不到有判讀紀錄的照片)'); return; }

  db.all('select Query, JsonResult from SearchCaches', (e2, caches) => {
    const cacheMap = new Map((caches || []).map(c => [c.Query, c.JsonResult]));

    for (const r of rows) {
      let a = {};
      try { a = JSON.parse(r.RawJson); } catch { a = {}; }

      console.log('');
      console.log('='.repeat(72));
      console.log('照片 #' + r.Id + '   ' + r.OriginalPath);
      console.log('判讀於 ' + r.AnalyzedAt + '   模型 ' + (r.ModelUsed || '(未標註)'));
      console.log('='.repeat(72));

      console.log('');
      console.log('── 使用者給了什麼(這些會進 prompt) ──');
      const mood = r.MoodName || '';
      console.log('  情緒      ' + (mood ? mood + (r.IsAiFilled ? '  ← AI 判讀後回填的,沒進 prompt' : '  ← 使用者自己選的,有進 prompt')
                                          : '(未指定,整個欄位不送)'));
      console.log('  歌單名稱  ' + (r.PlaylistName || '(未取名,整個欄位不送)'));
      console.log('  濾鏡      ' + (r.FilterName || '(未套用,整個欄位不送)'));
      const sl = [['亮度',r.Brightness],['對比',r.Contrast],['飽和',r.Saturation]]
        .filter(([k,v]) => v !== 100).map(([k,v]) => k + '=' + v);
      console.log('  滑桿      ' + (sl.length ? sl.join('  ') + '  ← 只有偏離 100 的才進 prompt' : '全部預設(整段不送)'));

      console.log('');
      console.log('── AI 判讀出什麼 ──');
      console.log('  場景      ' + (a.scene || '-'));
      console.log('  moodPick  ' + (a.moodPick || '-') + '   ← 封閉八選一,用來回填與書背著色');
      console.log('  mood[]    ' + ((a.mood || []).join('、') || '-') + '   ← 自由詞彙,只供顯示');
      console.log('  keywords  ' + ((a.keywords || []).join(' / ') || '-') + '   ← 備案,五首全找不到才會用');

      console.log('');
      console.log('── 拿去 YouTube 查的是這些(指名搜尋,不是關鍵字) ──');
      for (const s of (a.songs || [])) {
        const key = ('song:' + s.artist + ' - ' + s.title).toLowerCase();
        const hit = cacheMap.get(key);
        let found = '(尚未查詢)';
        if (hit !== undefined) {
          try {
            const arr = JSON.parse(hit);
            found = arr.length ? (arr[0].Title + '   [' + arr[0].Artist + ']') : '✗ 找不到(已快取,不會重複燒配額)';
          } catch { found = '(快取內容無法解析)'; }
        }
        console.log('  查詢  ' + s.artist + ' ' + s.title);
        console.log('  理由  ' + (s.why || '-'));
        console.log('  結果  ' + found);
        console.log('');
      }
    }
  });
});
"@

    $jsPath = Join-Path $tmp 'show.js'
    [System.IO.File]::WriteAllText($jsPath, $js, [System.Text.UTF8Encoding]::new($false))
    docker cp $jsPath "${SqliteContainer}:/tmp/show.js" | Out-Null
    docker exec $SqliteContainer node /tmp/show.js
}
finally {
    docker exec -u 0 $SqliteContainer sh -c 'rm -rf /tmp/showq /tmp/show.js' 2>&1 | Out-Null
    docker exec $AppContainer sh -c 'rm -rf /tmp/showq' 2>&1 | Out-Null
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
