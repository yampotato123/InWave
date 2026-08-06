# 修圖頁設計（含 SQLite 遷移與全站視覺提升）

日期：2026-08-06

## Context

`MyMusicBuddy` 目前完成的垂直切片是：上傳照片 → 選情緒 → YouTube 推薦 → 存歌單 → 播放。
對照《時程預估與企劃書審查》的五個階段，階段 1、3、4 已完成，**階段 2（照片功能）只做了上傳**——
全專案沒有任何 `<canvas>` 或 `getContext`，`PhotoEdits` 表與 `Photo.EditedPath` 欄位都建好了卻空轉。

這份設計補上階段 2：一個 canvas 修圖頁，3 個滑桿（亮度／對比／飽和度）+ 3 種濾鏡，
存修圖參數與合成後的圖。範圍刻意收斂在審查報告第 7 點的建議（「建議寫死：3 參數 + 3 濾鏡」），
企劃書列的裁切、旋轉、色溫、暗角、顆粒感**不在這次範圍內**。

同時處理兩件相鄰的事：
- **SQLite 遷移**——現用的 LocalDB 不是上架選項（見「為什麼現在換」），而現在是換的成本最低的時刻
- **全站視覺提升**——使用者要求，分階段執行以免與功能問題混淆

## 決策摘要

| 決策 | 選擇 | 理由 |
|---|---|---|
| 修圖頁流程位置 | 插在 `Create` 之後、`Recommend` 之前 | 改動最小；照片已在伺服器上，canvas 用 URL 載入無 CORS 問題；`Create.cshtml` 完全不動 |
| 濾鏡與情緒的關係 | 濾鏡有自己的名字，並作為**搜尋關鍵字修飾詞** | 實現企劃書「濾鏡影響音樂推薦」，規則完全確定可解釋；避免與 `MoodName` 產生兩個情緒來源 |
| 三種濾鏡 | 暖陽 `warm`／冷夜 `night`／褪色 `vintage` | 三個方向視覺互斥，且修飾詞在 YouTube 音樂查詢裡都是有意義的詞 |
| 資料庫 | 現在換 SQLite，當第一步 | LocalDB 無法上架；現在只有 1 個 migration、2 筆測試資料，成本最低 |
| AI 情緒判讀的鋪路 | 不預先加欄位 | `MoodProfile` 已是獨立一層，AI 之後只是換一個生產者，接縫已夠乾淨（YAGNI） |
| 測試 | 加小型 xUnit 專案 | 只測純函式；canvas 視覺效果無法自動測，明確承認 |
| 視覺範圍 | 全站提升，但排在功能驗證之後 | 使用者要求全站；分階段是為了讓「功能壞了」與「樣式壞了」可以分開查，不是縮小範圍 |

## Phase 0：SQLite 遷移

### 為什麼現在換

LocalDB 是開發用的隨選執行個體，**沒有任何主機平台會跑它**——所以「維持現狀」不存在，遲早要換。
現在換最便宜：只有 1 個 migration 要重生，資料庫裡只有 2 筆測試照片紀錄、0 筆歌單。

已驗證此專案**零 provider 綁定**：`Controllers`／`Data`／`Models`／`Services` 下沒有任何
`FromSql`、`ExecuteSql`、`HasColumnType`，`AppDbContext.OnModelCreating` 全是通用 Fluent API。
因此換 provider 的成本就是換套件 + 改一行 + 重生 migration，**第二次換（例如日後上 Postgres）一樣便宜。
這不是不可逆的決定。**

### 改動

| 檔案 | 動作 |
|---|---|
| `MyMusicBuddy.csproj` | `Microsoft.EntityFrameworkCore.SqlServer` → `Microsoft.EntityFrameworkCore.Sqlite` |
| `Program.cs:10` | `options.UseSqlServer(...)` → `options.UseSqlite(...)` |
| `appsettings.json` | 連線字串 → `Data Source=mymusicbuddy.db` |
| `Migrations/` | 整個刪除後 `dotnet ef migrations add InitialCreate` 重生 |
| `.gitignore` | 加 `*.db`、`*.db-shm`、`*.db-wal` |

### 執行時發現：SQLite 套件帶進一個高風險弱點（已處理）

EF Core 10.0.10 傳遞相依的 `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 內含 SQLite < 3.50.2，
有 **CVE-2025-6965**（CVSS 7.2，聚合函式記憶體損毀）。

處理方式：在 `.csproj` 明確指定 `SQLitePCLRaw.bundle_e_sqlite3` 2.1.12 覆寫傳遞相依。
選 2.1.12 而非 3.0.x 是因為前者是最小幅度的修正，不跨大版本。

已實測驗證：`SELECT sqlite_version()` 回 **3.53.3**（≥ 3.50.2），
`dotnet list package --vulnerable --include-transitive` 回報無弱點套件。
不是只把警告壓下去。

補充：此弱點需要特製的 SQL 才能觸發，而本專案已驗證無任何原生 SQL 或注入點，
實際暴露面趨近於零——修它是因為成本低，不是因為當下有風險。

現有 migration 無法沿用，它是純 SQL Server 產物：
`.Annotation("SqlServer:Identity", "1, 1")`、`type: "nvarchar(max)"`、`type: "datetime2"`。

**資料影響**：資料庫紀錄全部重來（2 筆照片／修圖／情緒，0 筆歌單）。
`wwwroot/uploads/` 裡的實體檔案不會被刪，只是失去資料庫紀錄而成為孤兒檔。

## Phase 1：修圖頁

### 流程

```
Create（上傳＋選情緒）
   ↓ RedirectToAction(Edit, photoId)     ← 原本直接去 Recommend
GET  /Works/Edit/{photoId}
POST /Works/Edit
   ↓
Recommend（關鍵字 = 情緒詞 + 濾鏡修飾詞）
```

- 修圖頁提供「略過修圖」連結直接到 `Recommend`；`EditedPath` 留 `null`，
  `WorksController.cs:103` 既有的 `photo.EditedPath ?? photo.OriginalPath` 已處理此 fallback
- 重新進入 `/Works/Edit/{id}` 會從 `PhotoEdit` 還原滑桿與濾鏡狀態，可反覆修改

### 濾鏡定義只有一份

濾鏡要同時供 JS（canvas 濾鏡字串）與 C#（關鍵字修飾詞、伺服器端驗證）使用。
兩邊各寫一份必然走鐘，因此定義放 C#，View 以 `data-` 屬性傳給 JS。

```csharp
// Services/PhotoFilters.cs（新增）
public record PhotoFilter(string Name, string CssFilter, string KeywordModifier);

public static class PhotoFilters
{
    public static readonly PhotoFilter[] All =
    {
        new("暖陽", "sepia(0.2) saturate(1.1) brightness(1.05)",        "warm"),
        new("冷夜", "hue-rotate(-10deg) contrast(1.15) brightness(0.9)", "night"),
        new("褪色", "saturate(0.6) sepia(0.3) contrast(0.9)",           "vintage"),
    };
}
```

上表的 CSS 數值是**起點，實作時要用真實照片對眼調整**——濾鏡好不好看沒有客觀判準，
以實際畫面為準，調整後把最終值寫回本文件。

`MoodKeywordMapper` 加可選參數，既有呼叫端不受影響：

```csharp
public static string GetKeyword(string moodName, string? filterName = null)
```

規則：查到濾鏡就在情緒關鍵字後面接一個空格與修飾詞；查不到或為 `null` 就回原本的關鍵字。

### 資料流

滑桿與濾鏡都用 CSS filter 語法。**CSS filter 的同名函式是連乘的**，所以濾鏡預設會疊在滑桿之上
（滑桿 `brightness(1.2)` × 暖陽 `brightness(1.05)` = 1.26）——這是預期行為，不是 bug。

```
<img> 載入 photo.OriginalPath（同源）
   ↓ 每次 input 事件
ctx.filter = `brightness(b/100) contrast(c/100) saturate(s/100) ${預設濾鏡字串}`
ctx.drawImage(...)                    ← 單一 <canvas> 即時重畫
   ↓ 送出前
長邊縮到 1600px → toDataURL('image/jpeg', 0.85)
   ↓ 塞進 hidden input，一般 form POST + antiforgery
   ↓ 伺服器
驗證 → 寫 /uploads/{原檔GUID}_edited.jpg → 存 PhotoEdit + Photo.EditedPath
```

- 用一般 form POST 而非 fetch/JSON，因為專案目前沒有任何 fetch 或 JSON 端點，維持既有慣例
- 合成圖檔名由原檔 GUID 推導，**重修原地覆蓋**，不產生孤兒檔，不需要刪檔邏輯
- 1600px / q0.85 輸出約 200–600KB，base64 後遠低於 ASP.NET Core 表單欄位的 4MB 預設上限，無需改設定

### 錯誤處理

| 情況 | 處理 |
|---|---|
| `photoId` 不存在 | `NotFound()`（比照既有 `Recommend`／`Details`） |
| 濾鏡名不在 `PhotoFilters.All` | ModelState 錯誤，退回修圖頁——不信任前端送來的值 |
| 滑桿值超出 0–200 | 伺服器端 `Math.Clamp(value, 0, 200)` |
| hidden 欄位沒有圖（JS 未執行） | ModelState 錯誤：「瀏覽器沒有產生預覽圖，請確認 JavaScript 已啟用」。**不**默默只存參數 |
| data URL 字串超過 4,000,000 字元 | ModelState 錯誤 |
| 檔案寫入失敗 | 不 catch，比照既有 `Create` 上傳寫法，讓例外拋出 |

### 動到的檔案

| 檔案 | 動作 |
|---|---|
| `Services/PhotoFilters.cs` | 新增 |
| `Views/Works/Edit.cshtml` | 新增 |
| `Controllers/WorksController.cs` | 加 `Edit` GET/POST；`Create` 結尾改導向 `Edit`；**`Recommend` 補 `.Include(p => p.Edit)`** |
| `Services/MoodKeywordMapper.cs` | 加修飾詞查表 + 多載 |
| `Models/PhotoEdit.cs` | 只改 `FilterName` 註解（現寫「夜色、午後」已不成立） |
| `wwwroot/css/site.css` | 加滑桿樣式 |

**不需要 migration**——欄位全都已存在。

### `Recommend` 的必要修正

`WorksController.cs:91-93` 目前只 `.Include(p => p.Mood)`。不補 `.Include(p => p.Edit)`
的話 `photo.Edit` 會是 `null`，濾鏡修飾詞永遠讀不到，而且**不會報錯**——會安靜地退化成沒有濾鏡的行為。
這是這次改動裡最容易漏、也最難察覺的一處。

### 快取影響

`YouTubeService.cs:33` 以關鍵字原文為快取鍵（`c.Query == query`），所以加修飾詞會自然分出新快取，
不會污染既有資料。代價是快取鍵從 8 個變成 **32 個**（8 情緒 × 4 濾鏡狀態，含未套濾鏡），命中率下降。
每天 100 次搜尋的配額仍然夠用，但值得知道。

## Phase 2：全站視覺提升

排在 Phase 1 實跑驗證通過之後。現有基礎是完整的，不打掉重來：
`wwwroot/css/site.css`（407 行）已有 CSS 變數色票與 37 個 `.mb-*` 類別，YouTube Music 暗色語彙一致。

這一階段是**品味導向**，無法在此完整規格化。執行時的做法：
先提出 2–3 個差異明顯的視覺方向讓使用者選定，再全站套用。涵蓋間距節奏、hover／focus 狀態、
空狀態、載入狀態、轉場動畫。色票若要改動，`site.css` 檔頭與 `README.md` 的說明必須同步更新。

## 驗證計畫

自動化測試只涵蓋純函式；**canvas 的視覺效果只能靠實際看畫面**，這點不假裝測得到。

**xUnit 專案 `MyMusicBuddy.Tests`**（新增）：
- `GetKeyword("午後", null)` → `"warm lo-fi afternoon music"`
- `GetKeyword("午後", "冷夜")` → `"warm lo-fi afternoon music night"`
- `GetKeyword("午後", "不存在的濾鏡")` → 退回無修飾詞的關鍵字
- `GetKeyword("不存在的情緒", "暖陽")` → `"chill music warm"`（既有 fallback 行為維持）
- 斷言業務值本身，不是 not-null

**端對端實跑**（每一步的實際輸出貼給使用者）：
1. Phase 0 後：`dotnet ef database update` 產生 `mymusicbuddy.db`，走完上傳 → 情緒 → 推薦 → 存歌單 → 播放
2. 上傳照片 → 確認導向 `/Works/Edit/{id}`
3. 拉三個滑桿 → 畫面即時變化
4. 套「冷夜」→ 送出 → 確認 `wwwroot/uploads/{guid}_edited.jpg` 產生，且 `PhotoEdit` 資料列數值正確
5. `Recommend` 頁顯示的照片是修圖後的版本，且搜尋關鍵字結尾是 `night`
6. 重新進入 `/Works/Edit/{id}` → 滑桿與濾鏡還原成上次的狀態
7. 走「略過修圖」→ 確認 `EditedPath` 為 `null` 且 `Recommend` 正常顯示原圖
8. 關閉 JS 送出 → 確認出現明確錯誤訊息而非默默成功

## 已知限制與待辦

- **上架時照片會消失**：`WorksController.cs:67` 把上傳檔寫進 `wwwroot/uploads/`，
  多數主機平台檔案系統是暫時性的，重新部署後所有作品變破圖。這與資料庫選擇無關，
  解法是改用物件儲存（Azure Blob／R2／S3）。**這次不做**，但它是上架前的必辦事項。
- **孤兒檔案**：Phase 0 換庫後，`wwwroot/uploads/` 既有的兩張 png 會失去資料庫紀錄。
  照片刪除功能目前不存在，所以尚無清理機制。
- **AI 情緒判讀的規則**：日後接 AI 時，**AI 讀原圖，不讀修圖後的圖**。
  否則暖陽濾鏡會被計算兩次——一次是 AI 看到偏橘的照片判成「溫暖」，一次是修飾詞又加了 `warm`。
- 企劃書的裁切、旋轉、色溫、暗角、顆粒感不在本次範圍。
- 情緒滑桿（`MoodProfile` 的 Energy／Calmness／Warmth／Exploration）不在本次範圍。
