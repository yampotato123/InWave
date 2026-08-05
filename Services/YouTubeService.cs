using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyMusicBuddy.Data;
using MyMusicBuddy.Models;

namespace MyMusicBuddy.Services;

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
