# InWave

照片情緒音樂分享平台——上傳照片、選情緒、取得 YouTube 推薦歌單。

技術:ASP.NET Core MVC (.NET 10) + EF Core 10 + SQLite + YouTube Data API v3

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
