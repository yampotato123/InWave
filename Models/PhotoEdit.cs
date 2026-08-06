namespace MyMusicBuddy.Models;

/// <summary>一張照片的修圖參數。100 = 原圖,範圍 0–200。</summary>
public class PhotoEdit
{
    public int Id { get; set; }
    public int PhotoId { get; set; }

    public int Brightness { get; set; } = 100;
    public int Contrast { get; set; } = 100;
    public int Saturation { get; set; } = 100;

    /// <summary>
    /// 套用的濾鏡名稱,取值見 Services/PhotoFilters.All(暖陽、冷夜、褪色);未套用為 null。
    /// 注意這與 MoodProfile.MoodName 是不同的東西:情緒在 Create 選,濾鏡在 Edit 選。
    /// </summary>
    public string? FilterName { get; set; }

    public Photo Photo { get; set; } = null!;
}
