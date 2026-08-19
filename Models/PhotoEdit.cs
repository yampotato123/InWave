namespace InWave.Models;

/// <summary>一張照片的修圖參數。100 = 原圖,範圍 0–200。</summary>
public class PhotoEdit
{
    public int Id { get; set; }
    public int PhotoId { get; set; }

    public int Brightness { get; set; } = 100;
    public int Contrast { get; set; } = 100;
    public int Saturation { get; set; } = 100;

    /// <summary>
    /// 色溫。**這一組的 0 才是原圖**,不是 100——它是加減量不是百分比。
    /// −100 冷（偏藍）↔ +100 暖（偏橘）。前端以 sepia + hue-rotate 合成。
    /// </summary>
    public int Temperature { get; set; }

    /// <summary>銳利度 0–100。前端用 SVG feConvolveMatrix 卷積,0 = 不套用。</summary>
    public int Sharpness { get; set; }

    /// <summary>柔焦 0–100,對應 0–8px 的模糊。0 = 不套用。</summary>
    public int Softness { get; set; }

    /// <summary>
    /// 色調曲線的五個控制點(輸入 0、0.25、0.5、0.75、1 各自對應的輸出值),逗號分隔。
    ///
    /// 這個字串**就是** SVG feComponentTransfer 的 tableValues,沒有中間轉換層——
    /// 曲線本質上是一張查找表,這樣存最直接。第二點是暗部、第四點是亮部。
    /// 預設 "0,0.25,0.5,0.75,1" 是對角線,即不改變。
    ///
    /// 注意:這個值會被寫進頁面的 SVG 屬性,**存進來之前一定要驗證與正規化**
    /// (見 WorksController.NormalizeCurve),不可直接信任表單送來的字串。
    /// </summary>
    public string CurvePoints { get; set; } = IdentityCurve;

    public const string IdentityCurve = "0,0.25,0.5,0.75,1";

    /// <summary>
    /// 套用的濾鏡名稱,取值見 Services/PhotoFilters.All(暖陽、冷夜、褪色);未套用為 null。
    /// 注意這與 MoodProfile.MoodName 是不同的東西:情緒在 Create 選,濾鏡在 Edit 選。
    /// </summary>
    public string? FilterName { get; set; }

    public Photo Photo { get; set; } = null!;
}
