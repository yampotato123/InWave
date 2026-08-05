namespace MyMusicBuddy.Services;

/// <summary>
/// 情緒 → YouTube 搜尋關鍵字的規則對照表(企劃書第五節:初期用規則,不用 AI)。
/// 之後要加入情緒滑桿的影響,就在 GetKeyword 裡依 MoodProfile 的數值調整關鍵字。
/// </summary>
public static class MoodKeywordMapper
{
    public static readonly string[] AllMoods =
        { "夜色", "午後", "溫暖", "懷舊", "夢幻", "安靜", "活力", "慶祝" };

    private static readonly Dictionary<string, string> Keywords = new()
    {
        ["夜色"] = "late night chill music",
        ["午後"] = "warm lo-fi afternoon music",
        ["溫暖"] = "warm acoustic songs",
        ["懷舊"] = "nostalgic oldies music",
        ["夢幻"] = "dreamy ambient music",
        ["安靜"] = "calm piano instrumental",
        ["活力"] = "upbeat pop playlist",
        ["慶祝"] = "party celebration songs",
    };

    public static string GetKeyword(string moodName) =>
        Keywords.TryGetValue(moodName, out var keyword) ? keyword : "chill music";
}
