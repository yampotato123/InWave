namespace InWave.Services;

/// <summary>AI 推薦的單首歌(還沒對應到 YouTube 影片)。</summary>
public record AnalyzedSong(string Artist, string Title, string Why);

/// <summary>
/// n8n 工作流 inwave-analyze 的回應。
/// 契約見 PROGRESS.md「n8n webhook 的介面契約」與 n8n-workflows/inwave-analyze.json。
/// </summary>
/// <param name="Ok">false 時其餘欄位不保證有值,呼叫端應走 MoodKeywordMapper fallback。</param>
/// <param name="Error">失敗原因代碼:AI_UNAVAILABLE、PARSE_FAILED(n8n 端)或 TIMEOUT、NETWORK、HTTP_xxx、BAD_JSON(C# 端)。</param>
/// <param name="MoodPick">封閉八選一,可直接寫回 MoodProfile.MoodName;AI 若給了範圍外的值,這裡會是 null。</param>
/// <param name="Mood">自由詞彙,僅供顯示,不可拿來當 MoodName。</param>
/// <param name="Model">判讀用的模型,由工作流標註(n8n 的 Gemini 節點輸出拿不到 modelVersion)。</param>
/// <param name="RawJson">原始回應全文,整包存進 PhotoAnalysis.RawJson。</param>
public record PhotoAnalysisResult(
    bool Ok,
    string? Error,
    string? Scene,
    string? MoodPick,
    IReadOnlyList<string> Mood,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<AnalyzedSong> Songs,
    string? Model,
    string RawJson)
{
    public static PhotoAnalysisResult Failure(string error, string raw = "") =>
        new(false, error, null, null, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<AnalyzedSong>(), null, raw);
}

public interface IPhotoAnalysisService
{
    /// <summary>
    /// 把照片送給 n8n 的 Gemini 工作流判讀氣氛並推薦歌曲。
    /// 送的必須是**原圖**:濾鏡與滑桿以文字進入 prompt,送修圖後的圖會讓濾鏡被算兩次
    /// (設計文件 §3.2、EditViewModel.cs:8-11)。
    /// 不丟例外——任何失敗都回 Ok=false 的結果,呼叫端據此走 fallback。
    /// </summary>
    /// <param name="mood">使用者選的情緒;沒選就傳 null,整個欄位不會送出(AI 純憑照片判讀)。</param>
    /// <param name="filter">使用者選的濾鏡名稱;沒選傳 null。</param>
    /// <summary>
    /// 解析已經存在資料庫的 PhotoAnalysis.RawJson。
    /// 與 AnalyzeAsync 共用同一段解析邏輯(含 moodPick 的八選一驗證),
    /// 免得「第一次判讀」與「之後從資料庫讀出來」兩條路得到不同結果。
    /// </summary>
    PhotoAnalysisResult ParseRaw(string rawJson);

    Task<PhotoAnalysisResult> AnalyzeAsync(
        byte[] imageBytes,
        string mimeType,
        string? mood,
        string? filter,
        int brightness,
        int contrast,
        int saturation,
        CancellationToken cancellationToken = default);
}
