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
    /// 留白時要用的建議名稱(情緒・日期),只當 placeholder 顯示。
    ///
    /// **不放進表單送回來**:存檔時伺服器端會重新算一次,送回來的值不會被採用,
    /// 那就沒有理由讓它往返。型別也是可為 null 的 string ——
    /// 不可為 null 的參考型別會讓 MVC 自動加 required 驗證,
    /// 而這個欄位在某些路徑上本來就是空的(2026-08-20 被煙霧測試抓到)。
    /// </summary>
    public string? SuggestedName { get; set; }

    /// <summary>
    /// AI 判讀的時間;沒判讀過(或判讀失敗)為 null。
    /// 畫面用它決定要不要顯示「重新判讀」——沒有判讀紀錄的照片按了也沒東西可清。
    /// </summary>
    public DateTime? AnalyzedAt { get; set; }

    // ── AI 怎麼看這張照片 ─────────────────────────────────────────
    // 這些在判讀時就拿到了,存在 PhotoAnalysis.RawJson 裡,先前完全沒顯示。
    // 拿出來給使用者看,產品才看得出「它真的看過照片」而不是隨機推歌。

    /// <summary>AI 對照片的一句話描述</summary>
    public string? Scene { get; set; }

    /// <summary>AI 給的氣氛形容詞(自由詞彙,與八種情緒無關)</summary>
    public IReadOnlyList<string> Mood { get; set; } = Array.Empty<string>();

    /// <summary>備用搜尋詞。只有五首歌全找不到時才會用到,顯示出來是為了讓判讀過程可檢視。</summary>
    public IReadOnlyList<string> Keywords { get; set; } = Array.Empty<string>();

    /// <summary>判讀用的模型,顯示在詳情面板</summary>
    public string? ModelUsed { get; set; }

    public List<SongInput> Songs { get; set; } = new();
}

public class SongInput
{
    public string VideoId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";

    /// <summary>
    /// AI 說明「為什麼這首配這張照片」。回落到關鍵字搜尋時是空的
    /// ——那些歌 AI 根本沒看過,不該假造理由。
    /// </summary>
    /// <remarks>
    /// 型別是可為 null 的 string:不可為 null 的參考型別會讓 MVC 自動加 required 驗證,
    /// 而回落路徑的 Why 本來就是空的,存歌單時會被「The Why field is required.」擋下
    /// ——n8n 未設定時根本存不了歌單。這正是 PlaylistName/SuggestedName 已經處理過的
    /// 同一個坑,只是 Why 當初漏掉(2026-08-20 煙霧測試在回落路徑抓到)。
    /// </remarks>
    public string? Why { get; set; }

    /// <summary>使用者是否勾選要加進歌單</summary>
    public bool Selected { get; set; }
}
