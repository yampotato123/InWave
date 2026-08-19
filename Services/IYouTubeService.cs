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

    /// <summary>
    /// 用「歌手 + 歌名」找出對應的那一支影片(AI 推薦的歌要靠這個變成可播放的內容)。
    /// 找不到、沒設 API key、或 API 出錯時回 null——呼叫端據此跳過該首,不要當成錯誤。
    ///
    /// 與 SearchAsync 的差別:那個是「給關鍵字要一串候選」,這個是「給指定的歌要一支影片」,
    /// 所以挑選規則不同(見實作),而且結果連「找不到」也會快取——
    /// 一次搜尋 100 單位,不快取的話 AI 推的冷門歌每次重新整理都燒一次配額。
    /// </summary>
    Task<SongResult?> FindVideoAsync(string artist, string title);
}
