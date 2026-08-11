# PROGRESS

最後更新：2026-08-12
遠端：https://github.com/yampotato123/InWave（**私有**）　分支 `main`　最新 commit `e761083`

---

## 更新（2026-08-11）：專案更名 InWave、完成容器化

### 更名 MyMusicBuddy → InWave（commit `64be6de`）

讓程式碼追上 repo 名稱與 InWave 視覺改版。命名空間、專案檔、Dockerfile、
compose、`_Layout` 的 scoped CSS 打包檔（現為 `InWave.styles.css`）皆已更新。

**刻意不改**：`mymusicbuddy.db` 檔名（改了要一併處理連線字串與既有資料，無實益）；
`docs/superpowers/specs/` 下的設計文件（那是特定時間點的記錄）。

### 容器化（commit `4a47051`、`30a8bcd`）

新增 `Dockerfile`（兩階段建置，映像檔 428MB）、`docker-compose.yml`、
`.dockerignore`、`.gitattributes`。

`Program.cs` 兩處改動：啟動時 `Database.Migrate()` 自動建資料庫；
容器內跳過 `UseHttpsRedirection`。

| 項目 | 值 |
|---|---|
| 本機偵錯（F5） | http://localhost:5120 |
| 容器 | http://localhost:5121 |
| 容器資料庫 | `docker-data/mymusicbuddy.db`（與本機那份各自獨立） |
| 容器照片 | 沿用 `wwwroot/uploads/` |

**驗證**：build 0 警告 0 錯誤；test 18/18；三個端點皆 200；
容器經 down／改名／重建後資料完整；日期與播放器經使用者實測正確。

### 相關文件

- `docs/superpowers/specs/2026-08-11-docker-n8n-ai-design.md` — 五階段設計
- `docs/superpowers/specs/2026-08-11-codebase-survey.md` — 全專案掃描報告

### 階段 2 完成：n8n 已納入同一個 compose（commit `d8a89ec`）

n8n 由 `C:\Users\admin\source\repos\n8n0807` 遷入。
**原資料夾保留未動可當後路**；`C:\Users\admin\source\repos\Docker\n8n\n8n_storage`
是 2026-08-10 的舊快照，**不要**拿它當來源。

作法：先停容器（SQLite 跑著時複製會拿到半套）→ 複製而非搬移 →
逐檔比對大小確認 `.sqlite` / `-shm` / `-wal` 三個檔完整 → 才動 compose。

順便修好原設定的三個問題：

| 原本 | 問題 | 改成 |
|---|---|---|
| `N8N_HOST=http://localhost:5678/home/workflows` | 該變數只吃主機名稱 | 刪除，改用下兩者 |
| `WEBHOOK_URL` | n8n 已標為淘汰 | `N8N_WEBHOOK_URL` |
| `restart: always` | 手動停止也會被拉起 | `unless-stopped` |

另加 `N8N_EDITOR_BASE_URL`。對外埠維持 **5678**——`tailscale serve`
已設定代理到 `127.0.0.1:5678`，改了就對不上。

**容器間連線**（同一個 compose 的服務自動同網路，用服務名稱互連）：

```
從 inwave 容器  → http://n8n:5678
從 n8n 容器     → http://inwave:8080
```

實測佐證：`inwave` 解析 `n8n` 得 `172.20.0.3`；`n8n` 打 `http://inwave:8080`
取得首頁 HTML；`n8n` 打 `http://localhost:8080` 連線被拒
（容器內的 `localhost` 指容器自身）。

資料完整性驗證：`/rest/settings` 的 `showSetupOnFirstLoad` 為 `false`，
既有帳號已帶入；`tailscale serve` 代理未受影響。

| 服務 | 網址 |
|---|---|
| InWave（容器） | http://localhost:5121 |
| InWave（本機 F5） | http://localhost:5120 |
| n8n | http://localhost:5678 ／ https://desktop-cj8q9sn.tail02844b.ts.net |

### 下一步：階段 3，AI 判讀照片

設計見 `docs/superpowers/specs/2026-08-11-docker-n8n-ai-design.md` 階段 3。
重點：接入點是 `WorksController.cs:199` 單一行；AI 結果須持久化
（`Recommend` 是 GET，重新整理會重複付費呼叫），需一次 migration。

---

## 更新（2026-08-10）：視覺方向改走 InWave,以下 Phase 2 的三個方向已作廢

原本規劃的「照片定調 / 暗房 / 展場」三選一,使用者選了 A 並實作完成（commit `042192f`、`f62a894`）,
但之後改用 claude.ai/design 產出的 **InWave** 設計全站覆蓋。

**InWave 設計**（VHS 書背收藏櫃 / 玻璃面板 / 音浪）：
- 來源：claude.ai design 專案 `625628ae-1794-4d27-8a9b-23a8b36788c4` 的 `frontend/`
- 透過 `DesignSync` 工具讀取（`list_files` / `get_file`）——瀏覽器下載被 Chrome 擋住,這條路才是可行的
- 新增 `wwwroot/css/inwave.css`（28 種書背版型）
- 改寫 `_Layout.cshtml`（`<body class="iw">`、頂部切換列、背景層,`inwave.css` 在 `site.css` 之後）
- 覆蓋 `Works/Index`（收藏櫃）、`Works/Details`（播放頁）、`Works/Edit`（修圖工作台）
- `Home/Index`、`Works/Create`、`Works/Recommend` 設計稿沒涵蓋,自行以 InWave 語彙補齊
- 舊版備份在 `backup-before-inwave-*/`（被 `.gitignore` 的 `Backup*/` 擋住,不進版控）

**刻意偏離設計稿的三處**（都有理由,不要「修正」回去）：
1. 濾鏡 CSS 與搜尋關鍵字改由 `PhotoFilters` / `MoodKeywordMapper` 產生。設計稿硬編在 JS 且與後端值不一致
2. 補回長邊 1600px 縮圖。設計稿用原尺寸 + q0.9,大照片會超過後端 4MB 上限
3. YouTube API callback 改成先定義再載入 script,否則 API 先載完就不會回呼

**InWave 尚未接的部分**（設計稿 README 自己標註的）：
- 情緒篩選 chips 是靜態的,要篩得在 `WorksController.Index` 加 query 參數
- 收藏櫃的選取固定第一筆,要換成 `?selected=` 或前端 JS 切換
- 收藏櫃只有 1 份歌單時,中央封面會壓在書背上（版面預期是一整排書背分列兩側）

**資安檢查（2026-08-10）**：repo 為私有；版控中無任何金鑰、照片或 `.db`；
`appsettings.json` 的 `ApiKey` 為空字串,YouTube key 走 `dotnet user-secrets`。
**轉公開前要處理**：commit 歷史含協作者的公司信箱 `jiayi.wang@qburger.com.tw`,屬他人個資。

---

---

## ✓ 已完成並已推送

### Phase 0　改用 SQLite（commit `c51e073`）

從 SQL Server LocalDB 換成 SQLite。原因：LocalDB 不是上架選項，沒有主機平台會跑它；
換掉之後 clone 下來即可執行，不必安裝或啟動任何資料庫服務。

- `InWave.csproj`：`EntityFrameworkCore.SqlServer` → `EntityFrameworkCore.Sqlite`
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

- `InWave.Tests`：xUnit，18 項測試（情緒 × 濾鏡的所有組合、fallback、濾鏡定義約束）
- `docs/superpowers/specs/2026-08-06-photo-editor-design.md`：設計文件
- `InWave.slnx`：方案檔，讓 Visual Studio 18 的測試總管看得到測試專案
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
- [x] ~~**改名成 InWave**~~ → 已於 2026-08-11 完成（commit `64be6de`）
- [ ] **清理 `wwwroot/uploads/`**：測試累積的檔案，多數是沒有資料庫紀錄的孤兒檔。
      在 `.gitignore` 內，不影響版控。使用者尚未決定是否清除
- [ ] **設定 YouTube API key**（否則搜尋一直回 `[示範]` 假資料）：
      `dotnet user-secrets set "YouTube:ApiKey" "你的key"`
- [ ] README 「接下來要做的」清單：情緒滑桿 → 歌單編輯 → 作品資訊 → 探索頁

### 使用者系統與個人化（2026-08-11 討論，**刻意延後**）

想做的是「讓每個人的推薦越用越貼近自己」。拆解後的依賴關係：

```
□ 使用者系統(註冊/登入/歌單歸屬)          ← 前提,最大的一塊
  └─ □ 個人化推薦:把使用者的歷史餵進 prompt
  └─ □ 免費額度 + 使用者自備金鑰(BYOK)雙軌
  └─ □ 用量上限 / 防盜刷
```

**目前完全沒有使用者概念**：Models 只有 `Photo`、`PhotoEdit`、`MoodProfile`、
`Playlist`、`PlaylistSong`、`Song`、`SearchCache`，**沒有 `User`**，
所有歌單都是全域的。上面每一項都卡在這個前提。

**釐清一個誤解（重要）**：使用者自備 API key **不會**讓 AI 更懂他。
模型是無狀態的，同樣的圖與 prompt，用誰的 key 結果都一樣。
真正的個人化來自「把使用者的歷史存進我方資料庫，再餵進 prompt」——
**這條路用專案自己的 key 就能做，不需要 BYOK。**

### local-first 架構（2026-08-11 評估過，**現階段不採用**）

構想是讓每個使用者的向量與 prompt 存在他自己電腦，專案端不替可能不回訪的人保管資料。
方向合理（Obsidian、瀏覽器擴充功能都是這模式），但現階段三個問題：

1. **要省的成本極小**：一首歌的向量約 3 KB（768 維 × 4 bytes），
   一人存 100 首 = 300 KB，一萬個使用者 = 3 GB。儲存費用量級是「幾塊錢」。
2. **網頁做不到（硬限制）**：瀏覽器無法在使用者電腦上啟動 Qdrant，這是安全模型，
   不是難度問題。要做到得換成桌面應用、瀏覽器擴充功能、或要使用者自己跑 Docker——
   摩擦力比 BYOK 更高。
3. **失去改進依據**：資料散在使用者端，就看不到「哪種 prompt 有效」「哪些歌常被收藏」，
   之後只能盲改推薦品質。

**什麼時候該重新考慮**（任一成立）：

- **隱私成為賣點** —— 要主打「你的照片絕不上傳」。那時 local-first 不只是省錢，是產品定位。這條比下面那條更可能先發生
- **儲存真的變貴** —— 幾十萬使用者，或每人存幾萬首歌

屆時該重新設計的是**整個產品形態**，不只是金鑰歸屬。

---

## 🔻 交接：新 session 從這裡接手（2026-08-12）

### 現在在哪

| 階段 | 狀態 |
|---|---|
| 0　基準線 | ✅ 使用者自行驗過 |
| 1　容器化 InWave | ✅ 完成 |
| 2　n8n 納入同一個 compose | ✅ 完成 |
| **Spike　驗證 AI 路徑** | ✅ **完成，且推翻了原始設計** |
| **3　AI 判讀接進 InWave** | ⬜ **未開始 ← 下一步** |
| 4　AI 風格濾鏡（選配） | ⬜ 未開始 |

### 開場先讀這三份（依序）

1. `docs/superpowers/specs/2026-08-11-docker-n8n-ai-design.md` — 設計與 spike 全部結論
2. 本檔的「資安檢查」與「已知限制」兩節
3. `README.md` 開頭的架構與決策（面試用，寫給評估者看的）

**不要重新設計**——spike 已經把「AI 產關鍵字 → YouTube 搜」推翻為
「AI 直接推薦歌曲 → YouTube 找影片」，理由與實測數據都在設計文件 §3.1.1。

### 環境狀態

```
git       main 與 origin 同步,工作區乾淨
建置      0 警告 0 錯誤;測試 18/18
容器      inwave 127.0.0.1:5121 / n8n 0.0.0.0:5678,皆執行中
金鑰      YouTube:ApiKey 已設於 user-secrets;Gemini 金鑰在 n8n 憑證內
配額      YouTube 昨日用掉 1,100/10,000 單位(每日重置)
```

### 階段 3 的執行計畫（已與使用者討論，**尚未取得最終確認**）

順序的理由：每一步結束都是可驗證狀態，且**持久化要早做**——
否則開發期間每次重新整理都在付費呼叫 AI。

**步驟 1　n8n 工作流**（不碰程式碼）
`[Webhook] → [Gemini 分析圖片] → [回應 Webhook]`
驗收：用 PowerShell 直接打 webhook，丟 base64 照片，拿到 JSON。
先獨立驗證 n8n 端，之後 C# 出問題才好定位。

**步驟 2　C# 打得到 n8n**（只驗管線，不改行為）
新增 `IRecommendService`，照 `Program.cs:13` 的 typed client 模式註冊。
`Recommend` 呼叫它並**寫進 log**，推薦邏輯維持原樣。
驗收：`docker logs inwave` 看得到 AI 回傳的 JSON。
此步只驗「容器內的 C# 能否以 `http://n8n:5678` 連到隔壁容器」。

**步驟 3　持久化**（一次 migration）
新增 `PhotoAnalysis` 實體，與 `Photo` 一對一：
`PhotoId` / `RawJson` / `Scene` / `AnalyzedAt` / `ModelUsed`
`Recommend` 先查是否已分析過，有則直接使用，不再呼叫 AI。
驗收：跑一次 → 重新整理 → n8n 執行歷史只有一筆。

**步驟 4　YouTube 改以歌名搜尋**
`YouTubeService` 新增方法：給「歌手 + 歌名」回一支影片。
```
1. 先查 Songs 資料表 → 命中則零配額
2. 未命中才打 API,maxResults=5
3. 挑選順序:「- Topic」頻道 > 頻道名含歌手名 > 其他
4. 過濾 liveBroadcastContent != "none"
5. 存進 Songs 表
```

**步驟 5　Fallback 與測試**
n8n 掛掉／逾時／格式錯 → 回落 `MoodKeywordMapper` 並寫 log。
驗收：停掉 n8n 容器，功能仍可用；現有 18 項測試全綠。

### 兩個待使用者決定

1. **`PhotoAnalysis` 存原始 JSON 還是拆成欄位？**
   建議：存原始 JSON + 少數常用欄位（Scene / AnalyzedAt / ModelUsed）。
   理由：AI 回傳格式之後還會改，存原文最有彈性；真要查詢再拆。
2. **今天做到哪裡**（步驟 1–3 約為一日的量）。

### n8n webhook 的介面契約（暫定，實作時確認）

請求：
```json
{ "imageBase64": "...", "mimeType": "image/jpeg",
  "mood": "午後", "filter": "冷夜",
  "sliders": { "brightness": 100, "contrast": 100, "saturation": 100 },
  "style": "default" }
```
回應：
```json
{ "scene": "...", "mood": ["..."], "keywords": ["..."],
  "songs": [{ "artist": "...", "title": "...", "why": "..." }] }
```

`style` 目前只有 `default`，是為「多種推薦人格」預留的接縫——
即使只有一個值也要帶，見設計文件 §3.5.1。

### 使用中的 prompt（v3，已驗證）

完整版在設計文件與 `notes/2026-08-11-spike-AI推薦流程.md` §五。
**不要重寫**——六條規則各自解決一個實測發現的問題。

---

## 資安檢查（2026-08-11）

### 通過

| 檢查 | 結果 |
|---|---|
| 全部 commit 歷史掃 API 金鑰 | 未發現任何 `AIza...` 或 api key 字串 |
| 敏感檔案是否進版控 | `.db`／`.env`／`n8n-data/`／`docker-data/` 皆未進版控 |
| 套件弱點（含傳遞相依） | InWave 與 InWave.Tests **皆無** |
| repo 權限 | 私有 |

### 已修正：容器埠綁定

`inwave` 原本綁 `0.0.0.0:5121`，同網段任何裝置皆可存取——**而本站沒有任何認證機制**，
對方可上傳照片、瀏覽全部歌單、消耗 API 配額。已改為 `127.0.0.1:5121`。

`n8n` 維持 `0.0.0.0:5678`（有登入保護，且需保留同網段存取）。
註：綁 `127.0.0.1` 不會影響 Tailscale——`tailscale serve` 本來就是從本機
連 `127.0.0.1:5678`。

### ⚠️ 待處理：commit 歷史含他人公司信箱

```
jiayi.wang@qburger.com.tw
```

**這是轉公開的阻斷條件。** repo 目前私有故無立即風險，但一旦為了給人看而轉公開，
即等同公開他人個資。

**給他人看程式碼的正確做法**：維持私有，用 GitHub Collaborators 邀請
（Settings → Collaborators → Add people）。面試亦同——說明「這是私有 repo，
我發邀請給您」反而展現資安意識。

**真的要轉公開時**：先備份整個資料夾，再用 `git filter-repo` 將該 email
換成匿名值（commit 訊息與歷史全數保留），然後 `git push --force`。
**不要用「開新 repo 重新 commit」的方式規避**——commit 歷史是本專案的價值之一。

```powershell
pip install git-filter-repo
git filter-repo --email-callback '
    return b"anonymous@example.com" if email == b"jiayi.wang@qburger.com.tw" else email
'
```

### ℹ️ 知悉即可：n8n 憑證的存放位置

`n8n-data\database.sqlite` 存放 Gemini API 金鑰（n8n 會加密），
但**加密金鑰就在同一個資料夾的 `config` 檔**。因此：

- 不要將 `n8n-data\` 同步到雲端硬碟
- 不要壓縮整個專案資料夾傳給他人
- 已在 `.gitignore` 中

附帶好處：金鑰只存在 n8n 一處，**外洩面收斂到單一資料夾**，
而非散落於程式碼、環境變數、設定檔各處。

### ⚠️ 待處理：Tailscale 上有他人的機器

```
100.73.120.120  desktop-cj8q9sn                       tonya22406@（本人）
100.108.168.24  bs-sqlserver2022.bigeye-wezen.ts.net  andyhuang1223@（他人）
```

若日後把**沒有認證的 InWave** 也掛上 `tailscale serve`，該機器擁有者即可存取。
三個選項：只掛 n8n（現況，最安全）／掛上並接受對方看得到／先為 InWave 加認證再掛。

---

## 已知限制（非 bug，但要知道）

- **上架時照片會消失**：`WorksController.cs` 把上傳檔寫進 `wwwroot/uploads/`，
  多數主機平台檔案系統是暫時性的，重新部署後所有作品變破圖。
  與資料庫選擇無關，解法是改用物件儲存（Azure Blob / R2 / S3）。上架前必辦。
- **快取鍵從 8 個變成 32 個**：8 情緒 × 4 濾鏡狀態。每天 100 次搜尋的配額仍夠，但命中率下降。
- **AI 情緒判讀的規則**（2026-08-11 改寫，原為禁令式表述）：
  **AI 一次判讀，輸入為「原圖」加上「使用者選擇的結構化文字」（情緒、濾鏡、滑桿值），
  輸出即為最終關鍵字，不再事後拼接修飾詞。**
  舊表述是「AI 讀原圖，不讀修圖後的圖」，意圖正確（避免暖陽濾鏡被算兩次）
  但仍保留事後拼接。完整理由與被否決的替代方案見
  `docs/superpowers/specs/2026-08-11-docker-n8n-ai-design.md` §3.2。
- **推薦目前完全沒有用到照片內容**：`GetKeyword` 的輸入只有手選的情緒與濾鏡，
  輸出空間固定為 32 種字串（8 情緒 × 4 濾鏡狀態）。換掉照片、其餘不變，
  推薦結果完全相同。`PROGRESS.md` 先前指出「產品命題在畫面上完全不見」，
  **同一句話對推薦邏輯也成立**。這正是階段 3 要補的缺口。

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
4. **建置前要先停掉 `dotnet run`**，否則 `.exe` 被鎖住，建置失敗（`MSB3027`／`MSB3021`）。
   在 Visual Studio 按建置卻噴「處理序無法存取檔案」時，第一個要查的就是這個。
5. **要拿 claude.ai/design 專案的檔案，用 `DesignSync` 工具，不要走瀏覽器下載。**
   瀏覽器路徑試了四種全失敗：WebFetch 403、Chrome 擋多檔自動下載、打包單檔仍不落地、
   帶 cookie 的 fetch 被擴充功能封鎖。`DesignSync` 的 `list_files` / `get_file` 一次就成功。
   通則：瀏覽器自動化碰不到瀏覽器 chrome 與作業系統對話框（下載提示、另存新檔），
   卡在那裡就換路，不要一直重試。
6. **容器與本機偵錯搶同一個埠**（2026-08-11）：compose 一開始也綁 5120，
   結果容器只要開著，按 F5 就噴
   `System.IO.IOException: Failed to bind to address http://[::1]:5120: address already in use`。
   錯誤訊息完全不會提到 Docker。容器改綁 5121，兩邊才能同時跑。
   **通則：容器對外的埠不要跟 `launchSettings.json` 相同。**
7. **Windows 不分大小寫，`./data` 會撞到 `Data\`**（2026-08-11）：
   volume 主機路徑原本寫 `./data`，Windows 直接解析成既有的 `Data\` 原始碼資料夾，
   SQLite 就被掛進了放 `AppDbContext.cs` 的地方。Linux 上不會發生，
   所以在別人機器或 CI 上測不出來。已改名為 `docker-data`。
8. **`backup-before-inwave-*` 一直在被編譯**（2026-08-11 更名時才發現）：
   備份資料夾位於專案內，Web SDK 會遞迴收錄並把其中的 `.cshtml` 交給 Razor 產生器。
   更名前因命名空間相同而沒爆，更名後才現形。已在 `InWave.csproj` 加上
   與 `InWave.Tests` 相同的 `Compile/Content/None Remove`。
   **通則：放在專案資料夾內的備份，Web SDK 都會當成原始碼。**

## 驗證腳本（目前在暫存區，未進版控）

端對端驗證腳本寫在這次 session 的 scratchpad，**session 結束後會消失**：

- `e2e.ps1` — 走完上傳 → 情緒 → 推薦 → 存歌單 → 作品頁
- `e2e-edit.ps1` — 修圖頁 17 項，含錯誤路徑（無預覽圖、偽造濾鏡名）、滑桿夾值、狀態還原
- `dbq/` — 小工具，直接查 SQLite 內容（`dotnet run --project dbq -- <db> "<sql>"`）

要保留的話得搬進 repo（例如 `scripts/`）。目前**沒有搬**，所以下次要回歸測試得重寫。
比對 HTML 前記得先 `HtmlDecode`，並把空白壓成單一空格（見上面第 2 條）。

---

## 怎麼把環境跑起來

```powershell
cd C:\Users\admin\source\repos\InWave
dotnet build
dotnet ef database update      # 第一次，或刪掉 .db 之後
dotnet run --launch-profile https
```

開 https://localhost:7167 　（Visual Studio 按 F5 也可以，且行程由自己掌控比較穩）

測試：`dotnet test InWave.slnx`

**不需要安裝或啟動任何資料庫服務**——SQLite 隨 NuGet 套件編進程式，
整個資料庫就是 `mymusicbuddy.db` 一個檔案。備份＝複製它，重來＝刪掉它再 `database update`。
想用眼睛看資料：DB Browser for SQLite（已安裝）。

### 用容器跑（2026-08-11 起）

```powershell
cd C:\Users\admin\source\repos\InWave
docker compose up -d --build     # 首次或改過程式碼
docker compose logs -f           # 看 log
docker compose down              # 停掉
```

開 http://localhost:5121

- **容器與本機可以同時跑**，埠不同（本機 5120／容器 5121），資料庫也各自獨立
- 容器的資料庫在 `docker-data/mymusicbuddy.db`，**不是**根目錄那份
- 容器首次啟動會自動套用 migration，不必手動 `dotnet ef database update`
- 照片兩邊共用 `wwwroot/uploads/`
- 要接真的 YouTube 搜尋：在根目錄建 `.env` 寫 `YOUTUBE_API_KEY=你的金鑰`
  （`.gitignore` 已擋，不會進版控）。沒設就回 `[示範]` 資料
