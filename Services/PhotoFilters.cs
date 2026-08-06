namespace MyMusicBuddy.Services;

/// <summary>一種修圖濾鏡。</summary>
/// <param name="Name">顯示名稱,同時是存進 PhotoEdit.FilterName 的值</param>
/// <param name="CssFilter">canvas 的 ctx.filter 字串(CSS filter 語法)</param>
/// <param name="KeywordModifier">接在情緒關鍵字後面的搜尋修飾詞</param>
public record PhotoFilter(string Name, string CssFilter, string KeywordModifier);

/// <summary>
/// 濾鏡定義的唯一來源。JS 端的 canvas 濾鏡字串由 View 以 data- 屬性傳過去,
/// 兩邊各寫一份必然會走鐘。
///
/// 濾鏡名稱刻意與 MoodKeywordMapper.AllMoods 的情緒詞分開——情緒在 Create 選,
/// 濾鏡在 Edit 選,兩者是不同的東西,共用字彙會讓使用者搞不清楚推薦到底聽誰的。
/// </summary>
public static class PhotoFilters
{
    public static readonly PhotoFilter[] All =
    {
        new("暖陽", "sepia(0.2) saturate(1.1) brightness(1.05)",         "warm"),
        new("冷夜", "hue-rotate(-10deg) contrast(1.15) brightness(0.9)", "night"),
        new("褪色", "saturate(0.6) sepia(0.3) contrast(0.9)",            "vintage"),
    };

    /// <summary>找不到就回 null(未套濾鏡,或前端送來無效的名字)。</summary>
    public static PhotoFilter? Find(string? name) =>
        string.IsNullOrEmpty(name) ? null : All.FirstOrDefault(f => f.Name == name);

    /// <summary>null 或空字串代表「不套濾鏡」,也算有效。</summary>
    public static bool IsValid(string? name) =>
        string.IsNullOrEmpty(name) || Find(name) != null;
}
