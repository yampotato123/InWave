namespace MyMusicBuddy.Models;

/// <summary>一張照片的修圖參數。100 = 原圖,範圍 0–200。</summary>
public class PhotoEdit
{
    public int Id { get; set; }
    public int PhotoId { get; set; }

    public int Brightness { get; set; } = 100;
    public int Contrast { get; set; } = 100;
    public int Saturation { get; set; } = 100;

    /// <summary>套用的情緒濾鏡名稱(夜色、午後、溫暖…);未套用為 null</summary>
    public string? FilterName { get; set; }

    public Photo Photo { get; set; } = null!;
}
