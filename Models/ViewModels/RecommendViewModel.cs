namespace InWave.Models.ViewModels;

/// <summary>推薦頁的畫面資料:一張照片 + 情緒 + 候選歌曲清單,勾選後存成歌單。</summary>
public class RecommendViewModel
{
    public int PhotoId { get; set; }
    public string PhotoPath { get; set; } = "";
    public string MoodName { get; set; } = "";

    /// <summary>實際拿去搜尋 YouTube 的關鍵字,顯示在頁面上讓使用者知道為什麼是這些歌</summary>
    public string SearchKeyword { get; set; } = "";

    /// <summary>
    /// 使用者自己取的名字。**沒取名時是空字串,不預先填入建議值** ——
    /// 先前會自動填成「情緒・日期」,看起來像系統替你取了名字,
    /// 讓人誤以為那個名字會影響 AI 判讀(其實判讀在修圖頁那一步就完成了)。
    /// </summary>
    /// <remarks>
    /// 型別是可為 null 的 string 而不是 string ——
    /// 不可為 null 的參考型別會讓 MVC 自動加上 required 驗證(輸出裡會看到 data-val-required),
    /// 那會把「留白」擋在驗證階段,伺服器端的建議名邏輯永遠走不到。
    /// </remarks>
    public string? PlaylistName { get; set; }

    /// <summary>
    /// 留白時要用的建議名稱(情緒・日期),當 placeholder 顯示。
    /// 實際存檔時由伺服器端重新算一次,不信任表單帶回來的值。
    /// </summary>
    public string SuggestedName { get; set; } = "";

    /// <summary>
    /// AI 判讀的時間;沒判讀過(或判讀失敗)為 null。
    /// 畫面用它決定要不要顯示「重新判讀」——沒有判讀紀錄的照片按了也沒東西可清。
    /// </summary>
    public DateTime? AnalyzedAt { get; set; }

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
