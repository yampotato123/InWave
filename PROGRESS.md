# PROGRESS

最後更新：2026-08-07（工作進行於 2026-08-06）
遠端：https://github.com/yampotato123/InWave　分支 `main`　最新 commit `0f4f5c9`

---

## ✓ 已完成並已推送

### Phase 0　改用 SQLite（commit `c51e073`）

從 SQL Server LocalDB 換成 SQLite。原因：LocalDB 不是上架選項，沒有主機平台會跑它；
換掉之後 clone 下來即可執行，不必安裝或啟動任何資料庫服務。

- `MyMusicBuddy.csproj`：`EntityFrameworkCore.SqlServer` → `EntityFrameworkCore.Sqlite`
- `Program.cs`：`UseSqlServer` → `UseSqlite`
- `appsettings.json`：連線字串 → `Data Source=mymusicbuddy.db`
- 舊 migration 是純 SQL Server 產物，刪除後重生為 SQLite 版本
- `.gitignore` 加 `*.db`、`*.db-shm`、`*.db-wal`

**順帶修掉一個高風險弱點**：EF Core 10.0.10 傳遞相依的 `SQLitePCLRaw` 2.1.11 內含
SQLite < 3.50.2，有 CVE-2025-6965（CVSS 7.2，聚合函式記憶體損毀）。
`.csproj` 明確指定 `SQLitePCLRaw.bundle_e_sqlite3` 2.1.12 覆寫。
已實測 `SELECT sqlite_version()` 回 **3.53.3**，是真的修掉而非壓掉警告。

### Phase 1　canvas 修圖頁（commit `a3f137b`）

流程改為 `Create → Edit → Recommend`。

- `Services/PhotoFilters.cs`（新）：三種濾鏡的唯一定義來源
- `Views/Works/Edit.cshtml`（新）：3 個滑桿 + 3 種濾鏡 + canvas 即時預覽
- `Models/ViewModels/EditViewModel.cs`（新）
- `WorksController`：`Edit` GET/POST；`Create` 改導向 `Edit`；
  **`Recommend` 補上 `.Include(p => p.Edit)`**
- `MoodKeywordMapper.GetKeyword(mood, filter)` 多載
- `wwwroot/css/site.css`：滑桿與 canvas 樣式

濾鏡同時影響外觀與音樂推薦：暖陽→`warm`、冷夜→`night`、褪色→`vintage`，
修飾詞接在情緒關鍵字後面。例：「午後」+「冷夜」→ `warm lo-fi afternoon music night`。

### Phase 1 附帶（commit `16cd2a8`、`0f4f5c9`）

- `MyMusicBuddy.Tests`：xUnit，18 項測試（情緒 × 濾鏡的所有組合、fallback、濾鏡定義約束）
- `docs/superpowers/specs/2026-08-06-photo-editor-design.md`：設計文件
- `MyMusicBuddy.slnx`：方案檔，讓 Visual Studio 18 的測試總管看得到測試專案
- README 更新：建置說明、修圖頁流程、常見問題

### 驗證結果

| 項目 | 結果 |
|---|---|
| 單元測試 | 18 項全過 |
| 伺服器端端對端 | 17 項全過（含錯誤路徑與滑桿夾值） |
| 瀏覽器實測 | 三種濾鏡的 `ctx.filter` 串接正確、匯出真實 JPEG（13KB，檔頭 `FF D8 FF E0`） |
| 建置 | 0 警告 0 錯誤 |
| 套件弱點 | 無 |

---

## → 停在這裡：Phase 2 全站視覺提升

**狀態**：三個方向已備妥，**等使用者選一個**，尚未寫任何程式碼。

### 為什麼要做

現有版面是 YouTube Music 的高完成度仿作（側欄寬度、chips、卡片牆、底部播放列、`#FF0033`）。
問題不是不好看，是**那是別人的設計**——demo 時評審看到的是 YouTube 的視覺。

更關鍵：產品命題「一張照片，一種情緒，一份歌單」在畫面上完全不見。
照片現在只是角落縮圖，整個 app 看起來像「剛好有圖片的音樂播放器」。
**照片應該是介面本身。**

### 三個方向（下次直接拿這份問使用者）

**A. 照片定調（原推薦）**
使用者的照片模糊放大當背景，內容浮在毛玻璃面板上。重點色不是設計師挑的，
而是用 canvas 從那張照片取樣出來——每個作品都有自己的顏色。
- 色票：`#0A0A0B` 底、面板 `rgba(255,255,255,.06)`、重點色 = 執行時取樣
- 字體：Noto Serif TC（巨大情緒詞）+ Inter（介面）
- 優點：重點色來自使用者內容，模仿不來；沿用已有的 canvas，成本低；
  是日後 AI 顏色分析的前置步驟
- 風險：深色照片會取出低對比的色，要做亮度／飽和度校正

**B. 暗房**
攝影暗房語彙：安全燈琥珀色、印樣式縮圖排列、照片有實體邊框與編號、數值用等寬字。
修圖頁做成曝光工作台，滑桿讀數改用相對值（+50 / −10）而不是 150/90。
- 色票：`#121212` 底、`#1B1B1D` 面板、`#E0A040` 安全燈琥珀（唯一重點色）
- 字體：JetBrains Mono（數值）+ Noto Sans TC（內文）
- 優點：工作量最小，版面骨架不用動
- 風險：「近黑＋單一亮色」是目前最泛濫的 AI 樣板；「底片」與「串流音樂」世界觀打架

**C. 展場**
完全反轉成紙白背景，照片當成掛在牆上的作品，大量留白。
介面只有黑白灰——**全篇唯一的顏色來自使用者的照片**。作品頁像展覽圖錄跨頁。
- 色票：`#F4F4F2` 紙白、`#0B0B0B` 字、`#6E6E6E` 次要、**無重點色**
- 字體：Noto Serif TC（標題）+ Inter 小寫大字距（標籤）
- 優點：與 YouTube Music 差最遠；照片真的成為主角
- 風險：YouTube 嵌入播放器永遠是深色，會打架；`site.css` 幾乎重寫，工作量最大

### 執行方式（使用者已同意）

範圍是**全站**，但分兩階段：功能先綠，再單獨做視覺，讓「功能壞了」與「樣式壞了」可以分開查。
Phase 1 已驗證完成，所以視覺這輪可以放手做。

---

## □ 待辦

- [ ] **Phase 2**：使用者選定視覺方向後全站套用
- [ ] **改名成 InWave**（可選）：資料夾、namespace、`_Layout.cshtml` 品牌字樣、README 標題。
      適合一次做完，不適合零散改
- [ ] **清理 `wwwroot/uploads/`**：測試累積的檔案，多數是沒有資料庫紀錄的孤兒檔。
      在 `.gitignore` 內，不影響版控。使用者尚未決定是否清除
- [ ] **設定 YouTube API key**（否則搜尋一直回 `[示範]` 假資料）：
      `dotnet user-secrets set "YouTube:ApiKey" "你的key"`
- [ ] README 「接下來要做的」清單：情緒滑桿 → 歌單編輯 → 作品資訊 → 探索頁

---

## 已知限制（非 bug，但要知道）

- **上架時照片會消失**：`WorksController.cs` 把上傳檔寫進 `wwwroot/uploads/`，
  多數主機平台檔案系統是暫時性的，重新部署後所有作品變破圖。
  與資料庫選擇無關，解法是改用物件儲存（Azure Blob / R2 / S3）。上架前必辦。
- **快取鍵從 8 個變成 32 個**：8 情緒 × 4 濾鏡狀態。每天 100 次搜尋的配額仍夠，但命中率下降。
- **AI 情緒判讀的規則（日後接時務必遵守）**：**AI 讀原圖，不讀修圖後的圖**。
  否則暖陽濾鏡會被計算兩次——一次 AI 看到偏橘判成「溫暖」，一次修飾詞又加了 `warm`。

---

## 踩過的坑（別再踩）

1. **`dotnet ef` 的 `--no-build` 會騙人**：新增 migration 之後若用 `--no-build`，
   EF 讀到的是還沒包含新 migration 的舊組件，會回報 `Done.` 但**一行都沒套用**。
   正確順序：`dotnet build` → `dotnet ef database update`。
   當時是查 `__EFMigrationsHistory`（0 筆）才發現的。
2. **Razor 會把中文屬性值編碼成 `&#x6696;`**：這是正確且安全的行為，瀏覽器會解碼。
   用腳本比對 HTML 前要先 `HtmlDecode`，否則會誤判成 bug。
3. **`form.submit()` 不會觸發 submit 事件**，`requestSubmit()` 才會。
   修圖頁靠 submit 事件把 canvas 轉成 data URL，用錯會送出空值。
4. **建置前要先停掉 `dotnet run`**，否則 `.exe` 被鎖住，建置失敗。

---

## 怎麼把環境跑起來

```powershell
cd C:\Users\admin\source\repos\MyMusicBuddy
dotnet build
dotnet ef database update      # 第一次，或刪掉 .db 之後
dotnet run --launch-profile https
```

開 https://localhost:7167 　（Visual Studio 按 F5 也可以，且行程由自己掌控比較穩）

測試：`dotnet test MyMusicBuddy.slnx`

**不需要安裝或啟動任何資料庫服務**——SQLite 隨 NuGet 套件編進程式，
整個資料庫就是 `mymusicbuddy.db` 一個檔案。備份＝複製它，重來＝刪掉它再 `database update`。
想用眼睛看資料：DB Browser for SQLite（已安裝）。
