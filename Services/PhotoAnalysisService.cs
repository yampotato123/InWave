using System.Text;
using System.Text.Json;

namespace InWave.Services;

/// <summary>
/// 呼叫 n8n 工作流 inwave-analyze(Webhook → Gemini → 回 JSON)。
/// 工作流本身的定義在 n8n-workflows/inwave-analyze.json。
/// </summary>
public class PhotoAnalysisService : IPhotoAnalysisService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<PhotoAnalysisService> _logger;

    public PhotoAnalysisService(HttpClient http, IConfiguration config, ILogger<PhotoAnalysisService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<PhotoAnalysisResult> AnalyzeAsync(
        byte[] imageBytes,
        string mimeType,
        string? mood,
        string? filter,
        int brightness,
        int contrast,
        int saturation,
        CancellationToken cancellationToken = default)
    {
        var url = _config["N8n:AnalyzeUrl"];
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogWarning("N8n:AnalyzeUrl 未設定,略過 AI 判讀。設定方式見 README。");
            return PhotoAnalysisResult.Failure("NOT_CONFIGURED");
        }

        // 契約:沒選情緒/濾鏡就整個欄位不送(不是送空字串)——送了 AI 會被錨定。
        var payload = new Dictionary<string, object>
        {
            ["imageBase64"] = Convert.ToBase64String(imageBytes),
            ["mimeType"] = mimeType,
            ["sliders"] = new { brightness, contrast, saturation },
            // 目前只有 default,是「多種推薦人格」預留的接縫(設計文件 §3.5.1),即使單值也要帶
            ["style"] = "default",
        };
        if (!string.IsNullOrWhiteSpace(mood)) payload["mood"] = mood;
        if (!string.IsNullOrWhiteSpace(filter)) payload["filter"] = filter;

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        // webhook 由 n8n 的 Header Auth 保護,值在 .env 的 N8N_WEBHOOK_TOKEN
        var token = _config["N8n:Token"];
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Add("X-InWave-Token", token);

        string body;
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("n8n 回應 {Status}:{Body}", response.StatusCode, Truncate(body, 500));
                return PhotoAnalysisResult.Failure($"HTTP_{(int)response.StatusCode}", body);
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient 逾時也是 OperationCanceledException,要跟使用者取消分開
            _logger.LogError(ex, "呼叫 n8n 逾時({Timeout} 秒)", _http.Timeout.TotalSeconds);
            return PhotoAnalysisResult.Failure("TIMEOUT");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "呼叫 n8n 失敗(url={Url})", url);
            return PhotoAnalysisResult.Failure("NETWORK");
        }

        return Parse(body);
    }

    public PhotoAnalysisResult ParseRaw(string rawJson) => Parse(rawJson);

    private PhotoAnalysisResult Parse(string body)
    {
        // 工作流失敗時 n8n 也是回 200 + {"ok":false,...},所以狀態碼不能當成功判準
        using JsonDocument? doc = TryParseJson(body);
        if (doc == null)
            return PhotoAnalysisResult.Failure("BAD_JSON", body);

        var root = doc.RootElement;

        // 這是最不可信的輸入:AI 可以回任何形狀。型別一律先驗再取,
        // 否則 TryGetProperty / GetBoolean / GetString 會丟例外,
        // 而本服務對外宣告「不丟例外」——丟了就是整頁 500 而不是回落。
        if (root.ValueKind != JsonValueKind.Object)
        {
            _logger.LogError("n8n 回應的最外層不是物件({Kind}):{Body}",
                root.ValueKind, Truncate(body, 500));
            return PhotoAnalysisResult.Failure("BAD_JSON", body);
        }

        var isOk = root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
        if (!isOk)
        {
            var error = GetString(root, "error");
            _logger.LogWarning("n8n 判讀失敗:{Error}", error ?? "(未提供 error 欄位)");
            return PhotoAnalysisResult.Failure(error ?? "UNKNOWN", body);
        }

        // moodPick 必須落在系統的八種內,否則書背印刷色與 MoodKeywordMapper 都會安靜退回預設值。
        // 唯一來源是 MoodKeywordMapper.AllMoods——這份清單不在 n8n 再抄一份,驗證一律在這裡做。
        var moodPick = GetString(root, "moodPick");
        if (moodPick != null && !MoodKeywordMapper.AllMoods.Contains(moodPick))
        {
            _logger.LogWarning("AI 回的 moodPick「{MoodPick}」不在八種情緒內,視為未指定", moodPick);
            moodPick = null;
        }

        return new PhotoAnalysisResult(
            Ok: true,
            Error: null,
            Scene: GetString(root, "scene"),
            MoodPick: moodPick,
            Mood: GetStringArray(root, "mood"),
            Keywords: GetStringArray(root, "keywords"),
            Songs: GetSongs(root),
            Model: GetString(root, "model"),
            RawJson: body);
    }

    private JsonDocument? TryParseJson(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "n8n 回應不是合法 JSON:{Body}", Truncate(body, 500));
            return null;
        }
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static IReadOnlyList<string> GetStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return v.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!)
            .ToList();
    }

    /// <summary>
    /// prompt 要 5 首,但這裡不能假設 AI 一定守約。沒有上限的話,模型漂移回 40 首
    /// 就是一次 4,000 單位的 YouTube 配額(一天只有 10,000),頁面也會卡住。
    /// </summary>
    private const int MaxSongs = 5;

    private static IReadOnlyList<AnalyzedSong> GetSongs(JsonElement root)
    {
        if (!root.TryGetProperty("songs", out var songs) || songs.ValueKind != JsonValueKind.Array)
            return Array.Empty<AnalyzedSong>();

        return songs.EnumerateArray()
            .Where(s => s.ValueKind == JsonValueKind.Object)
            .Select(s => new AnalyzedSong(
                Artist: GetString(s, "artist") ?? "",
                Title: GetString(s, "title") ?? "",
                Why: GetString(s, "why") ?? ""))
            .Where(s => s.Artist != "" && s.Title != "")
            .Take(MaxSongs)
            .ToList();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
