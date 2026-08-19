namespace InWave.Models;

/// <summary>一張照片的情緒設定。滑桿範圍 0–100,50 = 中間值。</summary>
public class MoodProfile
{
    public int Id { get; set; }
    public int PhotoId { get; set; }

    /// <summary>主情緒名稱(夜色、午後、溫暖、懷舊、夢幻、安靜、活力、慶祝)</summary>
    public string MoodName { get; set; } = "";

    /// <summary>
    /// MoodName 是 AI 判讀填的(true)還是使用者自己選的(false)。
    ///
    /// 必須明確記錄,不能事後反推。prompt 在使用者有指定情緒時會要求 AI
    /// 「moodPick 直接回傳該值」,所以「MoodName 等於判讀結果」對兩種來源都成立,
    /// 拿它當判準會把使用者的選擇誤判成 AI 填的。
    /// 用途:重新判讀時,只有 AI 填的才會被新結果覆蓋。
    /// </summary>
    public bool IsAiFilled { get; set; }

    /// <summary>安靜(0) ↔ 熱鬧(100)</summary>
    public int Energy { get; set; } = 50;

    /// <summary>有能量(0) ↔ 放鬆(100)</summary>
    public int Calmness { get; set; } = 50;

    /// <summary>冷(0) ↔ 溫暖(100)</summary>
    public int Warmth { get; set; } = 50;

    /// <summary>熟悉(0) ↔ 探索(100)</summary>
    public int Exploration { get; set; } = 50;

    public Photo Photo { get; set; } = null!;
}
