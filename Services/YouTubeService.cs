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

        // 快取鍵加前綴,與 SearchAsync 的關鍵字搜尋分開,避免兩種語意共用同一筆
        var cacheKey = $"song:{artist} - {title}";
        var cacheCutoff = DateTime.UtcNow - CacheDuration;
        var cached = await _db.SearchCaches
            .Where(c => c.Query == cacheKey && c.FetchedAt > cacheCutoff)
            .OrderByDescending(c => c.FetchedAt)
            .FirstOrDefaultAsync();

        if (cached != null)
        {
            // 空陣列代表「上次找過,確定找不到」——照樣算命中,不要再打 API
            return JsonSerializer.Deserialize<List<SongResult>>(cached.JsonResult)?.FirstOrDefault();
        }

        var apiKey = _config["YouTube:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("YouTube:ApiKey 未設定,無法為「{Artist} - {Title}」找影片。", artist, title);
            return null;
        }

        // 這裡刻意不加 videoCategoryId=10。指名搜尋要的是「那一首」,
        // 而 OST、動畫歌常被歸在其他分類,加了會找不到。改用下面的挑選規則過濾。
        var url = "https://www.googleapis.com/youtube/v3/search" +
                  "?part=snippet&type=video&maxResults=5" +
                  $"&q={Uri.EscapeDataString($"{artist} {title}")}&key={apiKey}";

        List<SongResult> candidates;
        try
        {
            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("YouTube API 回應 {Status}:{Body}", response.StatusCode, errorBody);
                return null;   // 不快取,配額或網路問題排除後應該要能重試
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            candidates = json.RootElement.GetProperty("items").EnumerateArray()
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "呼叫 YouTube API 失敗({Artist} - {Title})", artist, title);
            return null;
        }

        var picked = Pick(candidates, artist);

        // 找不到也要快取(存空陣列),否則冷門歌每次重新整理都燒 100 單位
        _db.SearchCaches.Add(new SearchCache
        {
            Query = cacheKey,
            JsonResult = JsonSerializer.Serialize(
                picked == null ? new List<SongResult>() : new List<SongResult> { picked }),
            FetchedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        if (picked == null)
            _logger.LogInformation("YouTube 找不到「{Artist} - {Title}」", artist, title);

        return picked;
    }

    /// <summary>
    /// 從候選中挑最可能是「原曲本身」的那支。
    /// 「- Topic」是 YouTube 自動為唱片公司音源建立的頻道,幾乎一定是原曲;
    /// 其次是頻道名帶歌手名的(官方頻道);再其次才是第一筆(通常是翻唱或二創)。
    /// </summary>
    private static SongResult? Pick(List<SongResult> candidates, string artist)
    {
        if (candidates.Count == 0)
            return null;

        return candidates.FirstOrDefault(c => c.Artist.EndsWith("- Topic", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(c => c.Artist.Contains(artist, StringComparison.OrdinalIgnoreCase))
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
