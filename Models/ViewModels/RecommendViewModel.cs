namespace InWave.Models.ViewModels;

/// <summary>推薦頁的畫面資料:一張照片 + 情緒 + 候選歌曲清單,勾選後存成歌單。</summary>
public class RecommendViewModel
{
    public int PhotoId { get; set; }
    public string PhotoPath { get; set; } = "";
    public string MoodName { get; set; } = "";

    /// <summary>實際拿去搜尋 YouTube 的關鍵字,顯示在頁面上讓使用者知道為什麼是這些歌</summary>
    public string SearchKeyword { get; set; } = "";

    public string PlaylistName { get; set; } = "";
    public List<SongInput> Songs { get; set; } = new();
}

public class SongInput
{
    public string VideoId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";

    /// <summary>使用者是否勾選要加進歌單</summary>
    public bool Selected { get; set; }
}
