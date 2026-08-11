namespace InWave.Services;

/// <summary>YouTube 搜尋回傳的單筆結果(還沒存進 DB 的型態)。</summary>
public record SongResult(string VideoId, string Title, string Artist, string ThumbnailUrl);

public interface IYouTubeService
{
    /// <summary>
    /// 用關鍵字搜尋 YouTube 音樂影片。
    /// 有 7 天快取;沒設定 API key 時回傳固定的示範資料,方便還沒申請 key 就能開發。
    /// </summary>
    Task<List<SongResult>> SearchAsync(string query, int maxResults = 10);
}
