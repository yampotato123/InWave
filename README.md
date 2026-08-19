# InWave

**一張照片，一種情緒，一份歌單。**

上傳照片、選情緒、修圖，由視覺模型判讀照片氣氛並推薦歌曲，存成可播放的作品收藏。

技術:ASP.NET Core MVC (.NET 10) + EF Core 10 + SQLite + YouTube Data API v3
+ Docker Compose + n8n + OpenAI(視覺模型)

---

## 架構

```
瀏覽器
  │ POST 照片 + 情緒 + 濾鏡 (multipart/form-data)
  ▼
InWave (容器)  ────────────────┐
  │ POST 原圖 + 選擇 (JSON)     │ GET 歌名查影片 (query string)
  ▼                            ▼
n8n (容器)                YouTube Data API
  │ POST 圖 + prompt
  ▼
OpenAI(視覺模型)
```

三個服務由一份 `docker-compose.yml` 起，容器間以**服務名稱**互連（`http://n8n:5678`）。

| 服務 | 對外埠 | 說明 |
|---|---|---|
| InWave | 5121 | 本機偵錯用 5120，兩者可同時執行 |
| n8n | 5678 | Tailscale serve 代理，可從外部存取 |

---

## 三個關鍵決策

### 1. AI 判讀放在 n8n，不放在 C#

**否決的方案**：在 C# 直接呼叫視覺模型 API。

**理由**：AI 功能的 prompt 會反覆修改數十次。在 n8n 中改完存檔即生效；
在 C# 中每次都要重建映像檔並重啟容器。n8n 的執行歷史保留每一步的輸入輸出，
是 AI 除錯的決定性優勢。附帶好處：模型 API 金鑰只存在 n8n 一處，
**C# 程式碼與其容器中都沒有 AI 金鑰**。

**已知代價**：多一跳（延遲較高）；n8n 故障會影響推薦——
以「回落到既有規則式推薦」處理，使 AI 成為加分項而非單點故障。

### 2. AI 直接推薦「歌曲」，不是產生「搜尋關鍵字」

**這是實測後推翻原設計的結果**，見下方 Spike。

### 3. 刻意延後的功能（附理由）

| 延後項目 | 理由 |
|---|---|
| **Qdrant 向量資料庫** | 音樂來源是 YouTube 搜尋，沒有自有音樂庫，向量檢索無對象。要等累積歌庫後才有價值 |
| **使用者系統 / BYOK** | 目前使用者數為 0。且釐清一個常見誤解：使用者自備 API key **不會**讓 AI 更懂他（模型無狀態），真正的個人化來自把歷史餵進 prompt |
| **local-first 架構** | 要省的儲存成本量級是「幾塊錢」（一萬使用者約 3 GB），而瀏覽器無法在使用者端啟動向量資料庫——這是安全模型限制，不是難度問題 |

---

## Spike：實測推翻了原始設計

動工前先用半小時驗證「照片 → AI → 音樂」這條路是否可行。**結果是原設計不可行。**

**原設計**：AI 產生氣氛關鍵字 → 拿去 YouTube 搜尋

**實測發現**：

1. YouTube Data API 是**文字搜尋**，不是推薦引擎——它沒有開放推薦 API
2. 搜氣氛關鍵字（`dream pop sunset`、`lofi ocean`）的結果被**三小時合輯與 24/7 直播電台**佔滿。
   一支三小時影片不是歌單，且播放器的「播完自動接下一首」永遠不會觸發

**改成**：AI 直接推薦真實歌曲（artist / title / why），YouTube 退居為「找到這首歌的影片」。

```
舊：照片 → AI → "dream pop sunset"     → YouTube → 一堆合輯
新：照片 → AI → "Kavinsky - Nightcall" → YouTube → 就是那首歌
```

**驗證**：三張氣氛差異大的照片（海邊夕陽／賽博朋克夜景／營火露營），
三組推薦完全不重疊，重複率約 13%。模型能理解領域慣例——
賽博朋克夜景推出《電馭叛客：邊緣行者》片尾曲，海邊夕陽推出《你的名字》主題曲。

**同時發現的成本問題**：改為「5 首歌各查一次」後，YouTube 免費配額
（10,000 單位/日 ÷ 每次搜尋 100 單位）使每日可處理量由 100 張降為 20 張。
緩解方式是用既有 `Song` 資料表的 `YoutubeVideoId` 唯一索引當永久快取——
熱門歌重複率高，命中率反而優於舊做法。

完整記錄：[`docs/superpowers/specs/2026-08-11-docker-n8n-ai-design.md`](docs/superpowers/specs/2026-08-11-docker-n8n-ai-design.md)

---

## 為擴充預留的接縫

現在只留接縫，不建功能——接縫成本近乎零，事後補很貴。

| 接縫 | 現況 | 之後能做什麼 |
|---|---|---|
| 模型可換 | 設在 n8n 節點 | 換模型不需重建映像檔 |
| 金鑰可換 | 存於 n8n 憑證 | 換供應商 = 換一個節點 |
| **prompt 角色參數** | 請求帶 `style` 欄位（目前僅 `default`） | 多種推薦人格（電影配樂／獨立音樂／熱門排行），各自調校的 prompt |
| 多人同時使用 | 無 static 可變狀態，`DbContext` 為 Scoped | 加 `UserId` 欄位即可，不需重寫 |

**多人使用的真正瓶頸不是架構，是 YouTube 配額**——每日 20 張的上限會先被撞到。
解法順序：`Song` 表快取 → 用量上限 → 提高配額 → 登入系統（排最後）。

---

## 開發過程記錄的踩坑

專案的 `PROGRESS.md` 記錄了每個實際踩到的問題與原始錯誤訊息，例如：

- **Windows 檔案系統不分大小寫**：volume 主機路徑寫 `./data` 會撞到既有的 `Data\`
  原始碼資料夾，導致 SQLite 被掛進原始碼目錄。Linux 上不會發生，CI 也測不出來
- **容器與本機偵錯搶埠**：錯誤訊息 `address already in use` 完全不會提到 Docker
- **`backup-*` 資料夾一直被編譯**：Web SDK 遞迴收錄專案內所有 `.cshtml`，
  更名前因命名空間相同而未爆

---

## 第一次跑起來

需求:.NET 10 SDK。資料庫是 SQLite,不需要另外安裝或啟動任何服務。

```powershell
# 1. 建資料庫(產生專案根目錄下的 mymusicbuddy.db)
dotnet tool install --global dotnet-ef   # 沒裝過 ef 工具才需要
dotnet ef database update

# 2. 跑起來
dotnet run
# 打開畫面上顯示的網址(例如 https://localhost:7167)
```

**還沒設定 YouTube API key 也能跑**——搜尋會回固定的示範資料(標題有 [示範] 字樣),
方便先開發畫面。要接真的搜尋結果:

```powershell
# API key 用 user-secrets 保存,不會進到 git(絕對不要寫進 appsettings.json)
dotnet user-secrets set "YouTube:ApiKey" "你的key"
```

申請 key:Google Cloud Console → 啟用「YouTube Data API v3」→ 憑證 → 建立 API 金鑰。
注意配額:免費額度一天 10,000 單位,一次搜尋要 100 單位 = 一天只能搜 100 次,
所以程式內建了 7 天搜尋快取(SearchCaches 表),同關鍵字不會重複打 API。

### AI 判讀照片(選用,沒設也能跑)

照片的氣氛判讀交給 n8n 的工作流(見「三個關鍵決策」第 1 點)。沒設定就略過 AI,
推薦仍由 `MoodKeywordMapper` 的規則產生,功能不會壞。

| 設定鍵 | 預設值 | 說明 |
|---|---|---|
| `N8n:AnalyzeUrl` | `http://localhost:5678/webhook/inwave/analyze` | 容器內要改成 `http://n8n:5678/...`,已由 `docker-compose.yml` 覆寫 |
| `N8n:Token` | 空 | n8n Webhook 節點的 Header Auth token,送出時放在 `X-InWave-Token` |
| `N8n:TimeoutSeconds` | 120 | 實測延遲:OpenAI 約 8–11 秒,先前的 Gemini 是 16–105 秒。上限抓寬,換供應商不必跟著調 |

### 金鑰放哪裡、怎麼換

**一律放 user-secrets，本機與容器共用同一份。**

```powershell
dotnet user-secrets set "YouTube:ApiKey" "你的key"
dotnet user-secrets set "N8n:Token"      "你的token"
```

容器讀得到，是因為 `docker-compose.override.yml` 把本專案的 user-secrets 目錄
**唯讀掛**到 `/app/secrets`，`Program.cs` 再把它當成一般 JSON 組態檔讀進來
（容器本身讀不到 user-secrets：檔案在主機的 `%APPDATA%`，而且那個組態來源
只在 Development 環境才會被加入）。

**換金鑰不必重啟，也不必重建容器** —— 執行上面的指令，幾秒後就生效。
實測：容器連續執行 57 分鐘不動，途中換掉 token，下一個請求就用新值。

| 位置 | 用途 | 換了之後 |
|---|---|---|
| user-secrets | 開發機（本機 F5 與容器共用） | 幾秒內生效 |
| `.env` 的 `YOUTUBE_API_KEY` / `N8N_WEBHOOK_TOKEN` | 沒有 user-secrets 時的退路 | 要 `docker compose up -d` 重建容器 |
| n8n UI → Credentials | AI 供應商金鑰、webhook 的 Header Auth | 存檔即生效 |

⚠️ **webhook token 兩端都要改**：n8n 的 Header Auth 憑證與 user-secrets 必須一致，
只改一邊會得到 403。改完用 `scripts/test-n8n-webhook.ps1` 驗證（回 403 就是不一致）。

⚠️ **因外洩而換金鑰時**，記得去原廠停用舊的，否則舊金鑰仍然可用。

### 正式部署

`docker-compose.yml` 是正式部署也能直接用的基礎設定；user-secrets 的掛載在
`docker-compose.override.yml`，那是開發機專屬的（依賴 Windows 的 `%APPDATA%`）。
部署時**排除** override：

```powershell
docker compose -f docker-compose.yml up -d
```

金鑰改由環境變數，或由平台的 secret volume 掛到同一個 `/app/secrets` 路徑
（Kubernetes Secret、Azure Container Apps 的 secret 掛載都是檔案）。
掛檔案的好處是**輪替金鑰不必重啟**——與開發機同一套機制，程式碼不必知道差別。

> `DOTNET_USE_POLLING_FILE_WATCHER=true` 不能拿掉。掛載進容器的檔案**不會傳遞
> inotify 事件**（Windows→Linux 的 bind mount、Kubernetes 的 Secret volume 都一樣），
> 預設的檔案監看器永遠不會被觸發，換了金鑰也不會生效。這點實測過確實不會，
> 改成輪詢才會動。

n8n 那端的工作流定義在 `n8n-workflows/inwave-analyze.json`,
由 `scripts/build-n8n-workflow.ps1` 產生。匯入與驗證方式見該腳本開頭的註解,
不經過 C# 的單獨驗證用 `scripts/test-n8n-webhook.ps1`。

## 專案結構(照這個模式加新功能)

```
Models/            資料表對應的類別(一表一檔)
Models/ViewModels/ 畫面專用的資料類別(不進資料庫)
Data/              AppDbContext(EF Core 設定)
Services/          商業邏輯:YouTubeService(搜尋+快取)、MoodKeywordMapper(情緒→關鍵字規則)
Controllers/       WorksController = 整個 MVP 流程
mymusicbuddy.db    SQLite 資料庫(不進 git;備份就是複製這個檔)
Views/Works/       Create(上傳+選情緒)→ Edit(canvas 修圖)→ Recommend(推薦+勾選)→ Details(播放)
wwwroot/uploads/   使用者上傳的照片(不進 git)
```

## 前端版面

介面對齊 YouTube Music 的暗色風格,樣式全部在 `wwwroot/css/site.css`
(檔頭有色票說明:背景 #030303、面板 #1C1C1C、品牌紅 #FF0033 只用在 logo 與播放中狀態):

- 左側欄 + 主內容的版面骨架在 `Views/Shared/_Layout.cshtml`
- 情緒選擇 = YT 的濾鏡 chips(`.mb-chip`,radio + label,不用 JS)
- 作品牆 = 圓角縮圖卡片(`.mb-card`,懸停浮出播放鈕)
- 歌曲列 = `.mb-song-row`(56px 縮圖 + 標題/來源)
- **作品頁底部播放列**:`Views/Works/Details.cshtml` 的 script 區塊,
  用 YouTube IFrame API 控制播放/暫停/上下首、播完自動接下一首。
  這是骨架裡最需要先讀懂的 50 行 JS——demo 一定會被問到播放怎麼做的。

## 已完成的垂直切片

上傳照片 → 選 8 種情緒之一 → canvas 修圖(3 滑桿 + 3 濾鏡)→
情緒與濾鏡轉搜尋關鍵字(`MoodKeywordMapper` + `PhotoFilters`)→
YouTube 搜尋(含快取)→ 勾選歌曲 → 存歌單 → 作品頁播放(iframe 官方播放器)。

**濾鏡會影響音樂推薦**:濾鏡除了改照片外觀,還會在情緒關鍵字後面接一個修飾詞
(暖陽→warm、冷夜→night、褪色→vintage)。例:「午後」+「冷夜」
→ `warm lo-fi afternoon music night`。定義集中在 `Services/PhotoFilters.cs`,
JS 端靠 View 傳的 `data-` 屬性取用,只有一份來源。

修圖參數存 `PhotoEdits`,canvas 合成後的圖存成 `{原檔名}_edited.jpg`(重修原地覆蓋)。
修圖頁可以「略過」,也可以回頭重修——重修一律從原圖開始,不會把濾鏡疊兩層。

**每個新功能都照這條的模式做**:Model 加欄位 → migration → Controller action → View。

## 接下來要做的(建議順序)

1. **情緒滑桿**:MoodProfiles 的 Energy/Calmness/Warmth/Exploration 欄位已建好,
   在選情緒頁加 4 支滑桿,並讓 `MoodKeywordMapper.GetKeyword` 依數值調整關鍵字
   (例:Energy > 70 就在關鍵字加 "upbeat")
2. **歌單編輯**:Details 頁加「刪除歌曲」「調整順序」(改 PlaylistSong.SortOrder)
3. **作品資訊**:儲存時可輸入一段文字、地點(Playlists 或新表加欄位 → migration)
4. **探索頁(demo 用模擬)**:用假資料做瀏覽別人作品的頁面,誠實標示是模擬

## 改資料表的流程

```powershell
# 1. 改 Models/ 下的類別
# 2. 產生 migration(名稱取有意義的)
dotnet ef migrations add AddXxxColumn
# 3. 套用到資料庫
dotnet ef database update
```

## 常見問題

- **`dotnet ef` 說找不到指令** → `dotnet tool install --global dotnet-ef`
- **想從頭來過** → 停掉 app,刪掉 `mymusicbuddy.db`,再跑一次 `dotnet ef database update`
- **`database is locked`** → app 還開著。SQLite 是單一檔案,執行中會被鎖住,
  要改資料或刪檔前先停掉 `dotnet run`
- **多出 `.db-shm` 和 `.db-wal` 兩個檔** → 正常,是 SQLite 的 WAL 日誌伴生檔,不要手動刪
- **改了 Model 或加了 migration 卻沒生效** → `dotnet ef` 的 `--no-build` 會讀舊組件。
  新增 migration 之後一定要先 `dotnet build` 再 `dotnet ef database update`,
  否則 EF 會回報 `Done.` 但其實一行都沒套用
- **搜尋一直回 [示範] 資料** → API key 沒設定,見上面 user-secrets 指令;
  設了還是 [示範] 的話,確認是以 Development 環境執行(user-secrets 只在
  Development 載入;VS 或一般 `dotnet run` 預設就是,只有 `--no-launch-profile`
  之類的跑法要自己設 `ASPNETCORE_ENVIRONMENT=Development`)
- **搜尋回空清單** → 看主控台 log:配額用完(quotaExceeded)或網路問題
