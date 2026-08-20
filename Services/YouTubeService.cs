using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using InWave.Data;
using InWave.Models;

namespace InWave.Services;

public class YouTubeService : IYouTubeService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(7);

    private readonly HttpClient _http;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<YouTubeService> _logger;

    public YouTubeService(HttpClient http, AppDbContext db, IConfiguration config, ILogger<YouTubeService> logger)
    {
        _http = http;
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task<List<SongResult>> SearchAsync(string query, int maxResults = 10)
    {
        query = query.Trim();

        // 1. 先查快取:一天只有 100 次搜尋額度,能不打 API 就不打
        // (截止時間要先算好,DateTime 減 TimeSpan 放在查詢裡 EF 翻不成 SQL)
        var cacheCutoff = DateTime.UtcNow - CacheDuration;
        var cached = await _db.SearchCaches
            .Where(c => c.Query == query && c.FetchedAt > cacheCutoff)
            .OrderByDescending(c => c.FetchedAt)
            .FirstOrDefaultAsync();

        if (cached != null)
        {
            return JsonSerializer.Deserialize<List<SongResult>>(cached.JsonResult) ?? new();
        }

        // 2. 沒設定 API key → 回示範資料,讓專案在申請 key 之前就能跑
        var apiKey = _config["YouTube:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("YouTube:ApiKey 未設定,回傳示範資料。設定方式見 README。");
            return MockResults();
        }

        // 3. 打 YouTube Data API v3(videoCategoryId=10 限定音樂類)
        var url = "https://www.googleapis.com/youtube/v3/search" +
                  "?part=snippet&type=video&videoCategoryId=10" +
                  $"&maxResults={maxResults}&q={Uri.EscapeDataString(query)}&key={apiKey}";

        List<SongResult> results;
        try
        {
            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("YouTube API 回應 {Status}:{Body}", response.StatusCode, body);
                return new();
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            results = json.RootElement.GetProperty("items").EnumerateArray()
                .Select(item => new SongResult(
                    VideoId: item.GetProperty("id").GetProperty("videoId").GetString() ?? "",
                    Title: item.GetProperty("snippet").GetProperty("title").GetString() ?? "",
                    Artist: item.GetProperty("snippet").GetProperty("channelTitle").GetString() ?? "",
                    ThumbnailUrl: item.GetProperty("snippet").GetProperty("thumbnails")
                        .GetProperty("medium").GetProperty("url").GetString() ?? ""))
                .Where(r => r.VideoId != "")
                .ToList();
        }
        catch (Exception ex)
        {
            // 網路斷線或 API 異常時不讓整頁炸掉,回空清單並記 log
            _logger.LogError(ex, "呼叫 YouTube API 失敗(query={Query})", query);
            return new();
        }

        // 4. 寫入快取(失敗的結果不快取,所以走到這裡一定是成功的)
        _db.SearchCaches.Add(new SearchCache
        {
            Query = query,
            JsonResult = JsonSerializer.Serialize(results),
            FetchedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        return results;
    }

    public async Task<SongResult?> FindVideoAsync(string artist, string title)
    {
        artist = artist.Trim();
        title = title.Trim();
        if (artist.Length == 0 || title.Length == 0)
            return null;

        var cacheKey = CacheKeyFor(artist, title);
        var cached = await ReadCacheAsync(cacheKey);
        if (cached != null)
        {
            // 空陣列代表「上次找過,確定找不到」——照樣算命中,不要再打 API
            return cached.FirstOrDefault();
        }

        var apiKey = _config["YouTube:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("YouTube:ApiKey 未設定,無法為「{Artist} - {Title}」找影片。", artist, title);
            return null;
        }

        var (ok, picked) = await SearchOneAsync(apiKey, artist, title);
        if (!ok)
            return null;   // HTTP/網路出錯,不快取,配額或網路問題排除後應該要能重試

        // 找不到也要快取(存空陣列),否則冷門歌每次重新整理都燒 100 單位
        AddCache(cacheKey, picked);
        await _db.SaveChangesAsync();

        if (picked == null)
            _logger.LogInformation("YouTube 找不到「{Artist} - {Title}」", artist, title);

        return picked;
    }

    /// <summary>
    /// 一次找多首(AI 推薦的整組歌)。快取查詢與寫入維持單執行緒(DbContext 非執行緒安全),
    /// 只有中間對「未命中」歌曲的 YouTube 搜尋並行——首次一組新歌能從「N 次往返疊加」
    /// 降到約一次往返的時間。命中快取的部分不打 API、也不並行,和逐首查一樣快。
    /// 回傳與輸入同順序、同長度;無效(空歌手/歌名)、找不到、或 API 出錯的位置為 null。
    /// </summary>
    public async Task<IReadOnlyList<SongResult?>> FindVideosAsync(IReadOnlyList<(string Artist, string Title)> songs)
    {
        var results = new SongResult?[songs.Count];
        var cacheKeys = new string?[songs.Count];

        // 1. 循序查快取(只有這裡碰 _db)。命中就填結果,未命中記下 index 待會並行搜尋。
        var misses = new List<int>();
        for (var i = 0; i < songs.Count; i++)
        {
            var artist = songs[i].Artist.Trim();
            var title = songs[i].Title.Trim();
            if (artist.Length == 0 || title.Length == 0)
                continue;   // 無效,維持 null

            var cacheKey = CacheKeyFor(artist, title);
            cacheKeys[i] = cacheKey;
            var cached = await ReadCacheAsync(cacheKey);
            if (cached != null)
                results[i] = cached.FirstOrDefault();
            else
                misses.Add(i);
        }

        if (misses.Count == 0)
            return results;

        var apiKey = _config["YouTube:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("YouTube:ApiKey 未設定,{Count} 首歌無法找影片。", misses.Count);
            return results;
        }

        // 2. 未命中的並行打 HTTP。SearchOneAsync 完全不碰 _db,只用 _http(執行緒安全),可安全並行。
        var searched = await Task.WhenAll(misses.Select(async i =>
        {
            var (ok, picked) = await SearchOneAsync(apiKey, songs[i].Artist.Trim(), songs[i].Title.Trim());
            return (Index: i, Ok: ok, Picked: picked);
        }));

        // 3. 回到單執行緒寫快取。HTTP 出錯的(Ok=false)不快取,以便稍後重試。
        var wroteAny = false;
        foreach (var (index, ok, picked) in searched)
        {
            if (!ok)
                continue;
            results[index] = picked;
            AddCache(cacheKeys[index]!, picked);
            wroteAny = true;
            if (picked == null)
                _logger.LogInformation("YouTube 找不到「{Artist} - {Title}」",
                    songs[index].Artist, songs[index].Title);
        }
        if (wroteAny)
            await _db.SaveChangesAsync();

        return results;
    }

    // 快取鍵加 song: 前綴,與 SearchAsync 的關鍵字搜尋分開,避免兩種語意共用同一筆。
    // 一律轉小寫:AI 有時回「Beach House」有時回「beach house」,大小寫敏感的話同一首歌會各燒一次配額。
    private static string CacheKeyFor(string artist, string title) =>
        $"song:{artist} - {title}".ToLowerInvariant();

    /// <summary>讀快取:命中回反序列化後的清單(可能是空清單=確定找不到),未命中回 null。只在單執行緒呼叫。</summary>
    private async Task<List<SongResult>?> ReadCacheAsync(string cacheKey)
    {
        var cacheCutoff = DateTime.UtcNow - CacheDuration;
        var cached = await _db.SearchCaches
            .Where(c => c.Query == cacheKey && c.FetchedAt > cacheCutoff)
            .OrderByDescending(c => c.FetchedAt)
            .FirstOrDefaultAsync();
        if (cached == null)
            return null;
        return JsonSerializer.Deserialize<List<SongResult>>(cached.JsonResult) ?? new();
    }

    /// <summary>把結果排進待寫入(picked 為 null 就存空陣列)。呼叫端負責 SaveChanges。只在單執行緒呼叫。</summary>
    private void AddCache(string cacheKey, SongResult? picked)
    {
        _db.SearchCaches.Add(new SearchCache
        {
            Query = cacheKey,
            JsonResult = JsonSerializer.Serialize(
                picked == null ? new List<SongResult>() : new List<SongResult> { picked }),
            FetchedAt = DateTime.UtcNow,
        });
    }

    /// <summary>
    /// 純 HTTP:對「歌手 + 歌名」搜尋並挑一支影片。**完全不碰 _db**,所以可以安全並行。
    /// 回 (Ok, Picked):Ok=false 代表 HTTP/網路出錯(呼叫端不要快取,留待重試);
    /// Ok=true 代表搜尋成功,Picked 可能為 null(這首確實找不到,呼叫端應快取空結果)。
    /// </summary>
    private async Task<(bool Ok, SongResult? Picked)> SearchOneAsync(string apiKey, string artist, string title)
    {
        // 這裡刻意不加 videoCategoryId=10。指名搜尋要的是「那一首」,
        // 而 OST、動畫歌常被歸在其他分類,加了會找不到。改用 Pick 的挑選規則過濾。
        var url = "https://www.googleapis.com/youtube/v3/search" +
                  "?part=snippet&type=video&maxResults=5" +
                  $"&q={Uri.EscapeDataString($"{artist} {title}")}&key={apiKey}";

        try
        {
            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("YouTube API 回應 {Status}:{Body}", response.StatusCode, errorBody);
                return (false, null);
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            var candidates = json.RootElement.GetProperty("items").EnumerateArray()
                // 直播與首播嵌入播放常有限制,而且不會是那首歌本身
                .Where(item => item.GetProperty("snippet").GetProperty("liveBroadcastContent")
                    .GetString() == "none")
                .Select(item => new SongResult(
                    VideoId: item.GetProperty("id").GetProperty("videoId").GetString() ?? "",
                    Title: item.GetProperty("snippet").GetProperty("title").GetString() ?? "",
                    Artist: item.GetProperty("snippet").GetProperty("channelTitle").GetString() ?? "",
                    ThumbnailUrl: item.GetProperty("snippet").GetProperty("thumbnails")
                        .GetProperty("medium").GetProperty("url").GetString() ?? ""))
                .Where(r => r.VideoId != "")
                .ToList();

            return (true, Pick(candidates, artist));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "呼叫 YouTube API 失敗({Artist} - {Title})", artist, title);
            return (false, null);
        }
    }

    /// <summary>
    /// 從候選中挑最可能是「這位歌手的原曲」的那支。
    ///
    /// 「- Topic」是 YouTube 為發行到串流的音源自動建立的頻道,**翻唱歌手也會有**,
    /// 所以單看 Topic 不夠:搜「RADWIMPS スパークル」時,某翻唱者的
    /// 「〇〇 - Topic」可能排在官方上傳之前。歌手名必須一起看。
    ///
    /// 順序:Topic 且頻道含歌手名 → 頻道含歌手名 → 任何 Topic → 第一筆。
    /// 注意頻道比對是子字串,仍可能誤命中(歌手 IU 命中頻道 MIUMIU),
    /// 但排在前兩順位的條件同時要求兩個特徵,誤判機率低很多。
    /// </summary>
    private static SongResult? Pick(List<SongResult> candidates, string artist)
    {
        if (candidates.Count == 0)
            return null;

        bool IsTopic(SongResult c) => c.Artist.EndsWith("- Topic", StringComparison.OrdinalIgnoreCase);
        bool HasArtist(SongResult c) => c.Artist.Contains(artist, StringComparison.OrdinalIgnoreCase);

        return candidates.FirstOrDefault(c => IsTopic(c) && HasArtist(c))
            ?? candidates.FirstOrDefault(HasArtist)
            ?? candidates.FirstOrDefault(IsTopic)
            ?? candidates[0];
    }

    /// <summary>沒有 API key 時的示範資料(都是真實可播放的影片)。</summary>
    private static List<SongResult> MockResults() => new()
    {
        new("kJQP7kiw5Fk", "[示範] Luis Fonsi - Despacito ft. Daddy Yankee", "Luis Fonsi",
            "https://i.ytimg.com/vi/kJQP7kiw5Fk/mqdefault.jpg"),
        new("9bZkp7q19f0", "[示範] PSY - GANGNAM STYLE", "officialpsy",
            "https://i.ytimg.com/vi/9bZkp7q19f0/mqdefault.jpg"),
        new("dQw4w9WgXcQ", "[示範] Rick Astley - Never Gonna Give You Up", "Rick Astley",
            "https://i.ytimg.com/vi/dQw4w9WgXcQ/mqdefault.jpg"),
        // 直播頻道嵌入播放常有限制,放最後
        new("jfKfPfyJRdk", "[示範] lofi hip hop radio - beats to relax/study to", "Lofi Girl",
            "https://i.ytimg.com/vi/jfKfPfyJRdk/mqdefault.jpg"),
    };
}
