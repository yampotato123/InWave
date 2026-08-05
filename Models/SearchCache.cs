namespace MyMusicBuddy.Models;

/// <summary>
/// YouTube 搜尋結果快取。
/// YouTube Data API 免費配額一天 10,000 單位、一次搜尋 100 單位(一天只能搜 100 次),
/// 所以同一個關鍵字 7 天內直接回快取,不重打 API。
/// </summary>
public class SearchCache
{
    public int Id { get; set; }
    public string Query { get; set; } = "";

    /// <summary>API 回傳結果序列化後的 JSON(List&lt;SongResult&gt;)</summary>
    public string JsonResult { get; set; } = "";

    public DateTime FetchedAt { get; set; }
}
