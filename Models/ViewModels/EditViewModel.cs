namespace MyMusicBuddy.Models.ViewModels;

/// <summary>修圖頁的資料。滑桿值 100 = 原圖,範圍 0–200(與 PhotoEdit 一致)。</summary>
public class EditViewModel
{
    public int PhotoId { get; set; }

    /// <summary>
    /// canvas 要載入的圖,**一律是原圖**。重修時若載入上次的合成圖,
    /// 濾鏡與滑桿會疊第二層,越修越歪。
    /// </summary>
    public string PhotoPath { get; set; } = "";

    public string MoodName { get; set; } = "";

    public int Brightness { get; set; } = 100;
    public int Contrast { get; set; } = 100;
    public int Saturation { get; set; } = 100;

    /// <summary>濾鏡名稱,取值見 Services/PhotoFilters.All;不套濾鏡為 null 或空字串。</summary>
    public string? FilterName { get; set; }

    /// <summary>canvas 匯出的合成圖(data:image/jpeg;base64,...),由前端 JS 於送出前填入。</summary>
    public string? EditedImage { get; set; }
}
