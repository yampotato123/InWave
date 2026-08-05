# MyMusicBuddy

照片情緒音樂分享平台——上傳照片、選情緒、取得 YouTube 推薦歌單。

技術:ASP.NET Core MVC (.NET 10) + EF Core 10 + SQL Server LocalDB + YouTube Data API v3

## 第一次跑起來

需求:.NET 10 SDK(或含 ASP.NET 工作負載、支援 .NET 10 的 Visual Studio)。

```powershell
# 1. 建資料庫(LocalDB,VS 裝好就有,不用另外裝 SQL Server)
dotnet tool install --global dotnet-ef   # 沒裝過 ef 工具才需要
dotnet ef database update

# 2. 跑起來
dotnet run
# 打開畫面上顯示的網址(例如 https://localhost:5001)
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
Views/Works/       Create(上傳+選情緒)→ Recommend(推薦+勾選)→ Details(播放)
wwwroot/uploads/   使用者上傳的照片(不進 git)
```

## 已完成的垂直切片

上傳照片 → 選 8 種情緒之一 → 情緒轉搜尋關鍵字(`MoodKeywordMapper`)→
YouTube 搜尋(含快取)→ 勾選歌曲 → 存歌單 → 作品頁播放(iframe 官方播放器)。

**每個新功能都照這條的模式做**:Model 加欄位 → migration → Controller action → View。

## 接下來要做的(建議順序)

1. **canvas 修圖**:Create 之後加一頁修圖(亮度/對比/飽和度 3 個滑桿 + 3 種濾鏡),
   前端 canvas 處理,存「參數(PhotoEdits 表,欄位已建好)+ 合成後的圖(EditedPath)」
2. **情緒滑桿**:MoodProfiles 的 Energy/Calmness/Warmth/Exploration 欄位已建好,
   在選情緒頁加 4 支滑桿,並讓 `MoodKeywordMapper.GetKeyword` 依數值調整關鍵字
   (例:Energy > 70 就在關鍵字加 "upbeat")
3. **歌單編輯**:Details 頁加「刪除歌曲」「調整順序」(改 PlaylistSong.SortOrder)
4. **作品資訊**:儲存時可輸入一段文字、地點(Playlists 或新表加欄位 → migration)
5. **探索頁(demo 用模擬)**:用假資料做瀏覽別人作品的頁面,誠實標示是模擬

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
- **LocalDB 連不上** → `sqllocaldb start MSSQLLocalDB`,VS 沒裝完整的話用
  VS Installer 補「資料儲存與處理」工作負載
- **搜尋一直回 [示範] 資料** → API key 沒設定,見上面 user-secrets 指令
- **搜尋回空清單** → 看主控台 log:配額用完(quotaExceeded)或網路問題
