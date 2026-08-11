# 程式碼掃描報告

日期：2026-08-11
目的：在 Docker 化與 AI 功能動工前，確認既有程式碼的實際狀態
配合文件：`2026-08-11-docker-n8n-ai-design.md`

---

## 0. 掃描範圍

### 已完整讀過

| 範圍 | 檔案 |
|---|---|
| Controllers | `WorksController.cs`、`HomeController.cs` |
| Models | 全部 8 個 + 2 個 ViewModel |
| Data | `AppDbContext.cs` |
| Services | `YouTubeService.cs`、`MoodKeywordMapper.cs`、`PhotoFilters.cs`、`IYouTubeService.cs` |
| Views | `Works/` 四頁、`Shared/_Layout.cshtml`、`_ViewImports`、`_ViewStart` |
| JS | `accent.js`、`site.js` |
| 測試 | `MoodKeywordMapperTests.cs`（含 `PhotoFiltersTests`） |
| 設定 | `Program.cs`、`MyMusicBuddy.csproj`、`MyMusicBuddy.slnx`、`appsettings.json` |

### 未讀，及原因

| 範圍 | 行數 | 原因 |
|---|---|---|
| `wwwroot/lib/`（Bootstrap、jQuery） | 約 73,000 | 第三方套件，不是本專案程式碼 |
| `Migrations/` 三個檔 | 610 | EF 自動產生；schema 已由 `Models/` 與 `AppDbContext.cs` 完整取得 |
| `site.css`、`inwave.css` 逐行 | 785 | 改以「class 使用率分析」取代逐行閱讀，見 §4 |
| `Views/Home/`、`Shared/Error`、`_ValidationScriptsPartial` | 約 60 | 預設樣板，未被本輪功能觸及 |

---

## 1. 整體評價

程式碼品質高於一般個人專案水準，具體證據：

- **註解記錄的是「為什麼」與「不這樣做會怎樣」**，而非複述程式碼。
  例：`WorksController.cs:194` — `濾鏡要參與關鍵字;少了這行 photo.Edit 是 null,濾鏡會安靜失效`
- **單一來源原則被明確實踐**：`PhotoFilters` 同時供應 CSS 字串與搜尋修飾詞，
  `Edit.cshtml:12` 由伺服器序列化給前端，避免兩邊各寫一份
- **安全性有意識處理**：GUID 檔名、副檔名白名單、大小上限、
  所有 POST 皆有 `ValidateAntiForgeryToken`、`PhotoPath` 不信任表單輸入（`WorksController.cs:145`）
- **錯誤不被吞掉**：`YouTubeService` 失敗時回空清單並寫 log，未偽裝成成功

**結論：不需要為了容器化或 AI 功能進行預先重構。**

---

## 2. 影響階段 1（容器化）的發現

### 2.1 時區依賴共三處

容器預設時區為 UTC，台北為 UTC+8。以下三處會在容器中取到錯誤的日期：

| 位置 | 程式碼 | 影響 |
|---|---|---|
| `WorksController.cs:208` | `DateTime.Now:MM/dd` | 歌單預設名稱的日期 |
| `Views/Works/Index.cshtml` | `CreatedAt.ToLocalTime()`（2 處） | 收藏櫃書背與作品資訊的日期 |
| `Views/Works/Details.cshtml:17` | `CreatedAt.ToLocalTime()` | 作品頁日期 |

**不會報錯，只會顯示錯誤日期。** 台北時間晚上 8 點後建立的作品會顯示成前一天。

處置：compose 設定 `TZ=Asia/Taipei`。

### 2.2 YouTube 金鑰不需改程式碼（已確認）

`YouTubeService.cs:44` 使用 `_config["YouTube:ApiKey"]`。
ASP.NET Core 的組態系統本就會讀取環境變數，設定 `YouTube__ApiKey`（雙底線）即可生效。

**設計文件原先列此為「需處理項目」，實際上只需在 compose 注入環境變數，零程式碼改動。**

### 2.3 外部網路依賴

| 依賴 | 位置 | 失敗時的表現 |
|---|---|---|
| Google Fonts（6 個字體家族） | `_Layout.cshtml:12-14` | 不報錯，靜默 fallback 成系統字體，InWave 視覺走樣 |
| YouTube IFrame API | `Details.cshtml:81` | 播放器不出現 |
| YouTube Data API | `YouTubeService.cs:52` | 回空清單並記 log（已妥善處理） |

前兩項在階段 1 驗收時須明確檢查，不能只看「頁面有開起來」。

### 2.4 建置上下文體積

`wwwroot/lib/` 含 Bootstrap 與 jQuery 共約 73,000 行且已進版控。
執行期需要（`_Layout.cshtml:15,44-45` 有引用），故不能排除，
但 `bin/`、`obj/`、`.git/`、`mymusicbuddy.db`、`wwwroot/uploads/` 必須由 `.dockerignore` 排除。

### 2.5 首次啟動需要建立資料庫

容器首次啟動時 `mymusicbuddy.db` 不存在，需執行 `dotnet ef database update`。
這表示需要一個 entrypoint 腳本 → **必然遭遇 CRLF 換行問題**（見設計文件階段 1）。

---

## 3. 影響階段 3（AI 判讀）的發現

### 3.1 接入點是單一一行

```csharp
// WorksController.cs:199
var keyword = MoodKeywordMapper.GetKeyword(photo.Mood.MoodName, photo.Edit?.FilterName);
```

設計文件所需的三項輸入在此處全部可得：
原圖路徑 `photo.OriginalPath`、情緒 `photo.Mood.MoodName`、濾鏡 `photo.Edit?.FilterName`。

### 3.2 重新整理會重複觸發 AI（設計缺口）

`Recommend` 是 **GET** action（`WorksController.cs:190`）。
使用者按 F5、返回上一頁、或開啟書籤都會重新執行第 199 行。

現況無害（`MoodKeywordMapper` 是純函式），但 AI 接上後每次重新整理都是一次付費呼叫。

**`SearchCache` 無法涵蓋此問題**：快取查詢發生在 `YouTubeService.cs:30-39`，
以「關鍵字」為鍵。AI 在產生關鍵字**之前**，位於快取範圍之外。

```
WorksController.cs:199   AI 呼叫      ← 無任何快取
WorksController.cs:200   YouTube 搜尋  ← 有 7 天快取
```

處置：AI 產生的關鍵字須持久化（存於 `MoodProfile` 或 `Photo`），
`Recommend` 先檢查是否已有結果。**需要一次 migration。**

### 3.3 既有欄位可直接沿用

`MoodProfile` 已有 `Energy` / `Calmness` / `Warmth` / `Exploration` 四個 0–100 數值欄位
（`Models/MoodProfile.cs`），目前僅存預設值 50，尚未被任何邏輯使用。
可作為 AI 判讀結果的落點，或未來 embedding 的前身。

---

## 4. 影響「AI 濾鏡」新需求的發現

### 4.1 現行修圖完全在瀏覽器端

```js
Edit.cshtml:114   ctx.filter = `brightness(..) contrast(..) saturate(..) ${CSSF[mod]}`
Edit.cshtml:124   拖動滑桿即時重繪
Edit.cshtml:137   送出時才 toDataURL 匯出
```

零伺服器成本、零延遲。改為 AI 生圖後，**即時預覽無法保留**（生圖需 5–30 秒）。

### 4.2 會直接失敗的既有測試（兩項）

| 測試 | 斷言 | AI 濾鏡加入後 |
|---|---|---|
| `PhotoFiltersTests.恰好三種濾鏡()` | `PhotoFilters.All.Length == 3` | **失敗** |
| `PhotoFiltersTests.每種濾鏡都有css字串與搜尋修飾詞()` | 每個濾鏡的 `CssFilter` 非空 | **失敗**（AI 濾鏡無 CSS） |

這兩項是設計上刻意的約束，不是疏漏。修改它們等於修改設計決策，須有意識地做。

### 4.3 命名衝突風險

`PhotoFiltersTests.濾鏡名稱不得與情緒名稱重疊()` 檢查濾鏡名是否出現在 `AllMoods` 中。

擬新增的「懷舊電影」與既有情緒「懷舊」**不是完全相同的字串，測試會通過**，
但正是該測試想防止的混淆情境。命名需另行斟酌。

---

## 5. 技術債（不影響本輪，記錄備查）

### 5.1 `accent.js` 是孤兒檔（89 行）

從照片取樣主色寫入 `--mb-accent` 的模組，品質良好且註解完整，但**目前沒有任何地方載入它**：

- 舊版 `backup-before-inwave-20260810-112202/_Layout.cshtml:62` 有 `<script src="~/js/accent.js">`
- 現行 `Views/Shared/_Layout.cshtml:44-46` 只載入 jQuery、Bootstrap、`site.js`
- 現行 Views 無任何 `data-accent-src` 屬性（僅備份資料夾中有）

連帶影響：`site.css` 有 18 處使用 `var(--mb-accent)`，該變數永遠停留在
`site.css:26` 的 fallback 灰 `#9aa0a6`。

### 5.2 `site.css` 約九成為死碼

以「View 中是否出現該 class 名稱」靜態比對：**53 個 class 中有 48 個未被使用**。
未使用者皆為 `mb-*` 前綴，屬 InWave 之前的舊設計。

### 5.3 `inwave.css` 的分析結果須修正

自動比對報告 29 個未使用，但其中 `s3`–`s27`（共 25 個）是**誤判**——
`Index.cshtml` 以 `var skin = "s" + ((p.Id % 28) + 1);` 動態組出 class 名稱，靜態比對看不到。

真正可疑的僅 `f-warm`、`f-night`、`f-vintage` 三個：
現行修圖以 JS 的 `ctx.filter` 實作，未使用 CSS class，疑為設計稿遺留。

### 5.4 jQuery 可能未被使用

`_Layout.cshtml:44` 載入 jQuery，但 jquery-validation 位於
`_ValidationScriptsPartial.cshtml`，而該 partial 未被任何 View 引用。
需實測確認 Bootstrap bundle 是否依賴它（Bootstrap 5 本身不需要）。

### 5.5 已知且已記錄於 PROGRESS.md

- `Index.cshtml` 情緒篩選 chips 為靜態，未接 query 參數
- 收藏櫃選取固定第一筆（`var sel = Model.FirstOrDefault();`）

---

## 6. 建議寫回設計文件的項目

| 項目 | 來源 | 影響階段 |
|---|---|---|
| `TZ=Asia/Taipei`，並列出三處時區依賴 | §2.1 | 階段 1 |
| YouTube 金鑰改為「零程式碼改動」 | §2.2 | 階段 1 |
| 字體與 IFrame API 須列入驗收檢查項 | §2.3 | 階段 1 |
| AI 結果須持久化，需一次 migration | §3.2 | 階段 3 |
| AI 濾鏡會弄壞兩項既有測試 | §4.2 | 階段 4 |
| 「懷舊電影」與情緒「懷舊」的命名衝突 | §4.3 | 階段 4 |
