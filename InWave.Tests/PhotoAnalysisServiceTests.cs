using System.Net;
using System.Text;
using InWave.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace InWave.Tests;

/// <summary>
/// n8n 回應 → PhotoAnalysisResult 的解析。
/// 這裡是 AI 與系統的交界:AI 回什麼都有可能,解析錯了會安靜地把推薦導向錯誤的情緒,
/// 不會丟例外也不會有錯誤畫面,所以每種壞法都要有斷言。
/// 不打真的 n8n——用假的 HttpMessageHandler 餵固定回應。
/// </summary>
public class PhotoAnalysisServiceTests
{
    private const string ValidBody = """
        {"ok":true,"scene":"夕陽海畔","moodPick":"懷舊","mood":["懷舊","詩意"],
         "keywords":["sunset"],"songs":[{"artist":"Max Richter","title":"On the Nature of Daylight","why":"溫柔"}]}
        """;

    private static PhotoAnalysisService MakeService(HttpStatusCode status, string body)
    {
        var http = new HttpClient(new StubHandler(status, body))
        {
            BaseAddress = new Uri("http://n8n.test"),
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["N8n:AnalyzeUrl"] = "http://n8n.test/webhook/inwave/analyze",
                ["N8n:Token"] = "test-token",
            })
            .Build();

        return new PhotoAnalysisService(http, config, NullLogger<PhotoAnalysisService>.Instance);
    }

    private static Task<PhotoAnalysisResult> AnalyzeAsync(HttpStatusCode status, string body) =>
        MakeService(status, body).AnalyzeAsync(
            imageBytes: new byte[] { 1, 2, 3 }, mimeType: "image/jpeg",
            mood: null, filter: null, brightness: 100, contrast: 100, saturation: 100);

    [Fact]
    public async Task 正常回應_解析出場景與歌曲()
    {
        var result = await AnalyzeAsync(HttpStatusCode.OK, ValidBody);

        Assert.True(result.Ok);
        Assert.Equal("夕陽海畔", result.Scene);
        Assert.Equal("懷舊", result.MoodPick);
        Assert.Equal(new[] { "懷舊", "詩意" }, result.Mood);
        Assert.Single(result.Songs);
        Assert.Equal("Max Richter", result.Songs[0].Artist);
        Assert.Equal("On the Nature of Daylight", result.Songs[0].Title);
    }

    [Fact]
    public async Task 原始回應全文要留著_階段3要整包存進資料庫()
    {
        var result = await AnalyzeAsync(HttpStatusCode.OK, ValidBody);

        Assert.Contains("\"scene\":\"夕陽海畔\"", result.RawJson);
    }

    [Fact]
    public async Task moodPick不在八種情緒內_視為未指定而不是照單全收()
    {
        // AI 實測會回「寂寥」這種八種以外的詞。直接寫進 MoodName 的話,
        // 書背印刷色(Index.cshtml)與 MoodKeywordMapper 都會安靜退回預設值。
        var result = await AnalyzeAsync(HttpStatusCode.OK,
            """{"ok":true,"moodPick":"寂寥","mood":["寂寥"],"songs":[]}""");

        Assert.True(result.Ok);
        Assert.Null(result.MoodPick);
        Assert.Equal(new[] { "寂寥" }, result.Mood);   // 自由詞彙不受限制,要留著給畫面顯示
    }

    [Fact]
    public async Task n8n回報失敗_原樣帶出錯誤碼()
    {
        // 工作流失敗時 n8n 也是回 HTTP 200,所以狀態碼不能當成功判準
        var result = await AnalyzeAsync(HttpStatusCode.OK,
            """{"ok":false,"error":"AI_UNAVAILABLE","detail":"high demand"}""");

        Assert.False(result.Ok);
        Assert.Equal("AI_UNAVAILABLE", result.Error);
        Assert.Empty(result.Songs);
    }

    [Fact]
    public async Task 回應不是JSON_回BAD_JSON而不是丟例外()
    {
        var result = await AnalyzeAsync(HttpStatusCode.OK, "<html>504 Gateway Timeout</html>");

        Assert.False(result.Ok);
        Assert.Equal("BAD_JSON", result.Error);
    }

    [Fact]
    public async Task 空的回應_回BAD_JSON()
    {
        // 2026-08-19 實測:工作流中斷時 n8n 回 200 + 零長度 body。
        // 工作流已補上錯誤分支,但 C# 這端仍不能假設對方一定守約。
        var result = await AnalyzeAsync(HttpStatusCode.OK, "");

        Assert.False(result.Ok);
        Assert.Equal("BAD_JSON", result.Error);
    }

    [Fact]
    public async Task HTTP錯誤_錯誤碼帶上狀態碼()
    {
        var result = await AnalyzeAsync(HttpStatusCode.Forbidden, "Authorization data is wrong!");

        Assert.False(result.Ok);
        Assert.Equal("HTTP_403", result.Error);
    }

    [Fact]
    public async Task 沒設定AnalyzeUrl_不呼叫也不炸()
    {
        var service = new PhotoAnalysisService(
            new HttpClient(new StubHandler(HttpStatusCode.OK, ValidBody)),
            new ConfigurationBuilder().Build(),
            NullLogger<PhotoAnalysisService>.Instance);

        var result = await service.AnalyzeAsync(
            new byte[] { 1 }, "image/jpeg", null, null, 100, 100, 100);

        Assert.False(result.Ok);
        Assert.Equal("NOT_CONFIGURED", result.Error);
    }

    [Fact]
    public async Task 沒選情緒與濾鏡時_那兩個欄位整個不送()
    {
        // 契約如此:送空字串會錨定 AI 的判讀(設計文件 §3.2.1)
        var handler = new StubHandler(HttpStatusCode.OK, ValidBody);
        var service = new PhotoAnalysisService(
            new HttpClient(handler),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["N8n:AnalyzeUrl"] = "http://n8n.test/webhook/inwave/analyze",
            }).Build(),
            NullLogger<PhotoAnalysisService>.Instance);

        await service.AnalyzeAsync(new byte[] { 1 }, "image/jpeg",
            mood: null, filter: null, brightness: 100, contrast: 100, saturation: 100);

        Assert.DoesNotContain("\"mood\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"filter\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task 有選情緒與濾鏡時_兩個欄位都要送出()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ValidBody);
        var service = new PhotoAnalysisService(
            new HttpClient(handler),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["N8n:AnalyzeUrl"] = "http://n8n.test/webhook/inwave/analyze",
                ["N8n:Token"] = "test-token",
            }).Build(),
            NullLogger<PhotoAnalysisService>.Instance);

        await service.AnalyzeAsync(new byte[] { 1 }, "image/jpeg",
            mood: "夜色", filter: "冷夜", brightness: 130, contrast: 100, saturation: 100);

        Assert.Contains("\"mood\":\"\\u591C\\u8272\"", handler.LastRequestBody);   // JSON 會把中文轉義
        Assert.Contains("\"filter\":", handler.LastRequestBody);
        Assert.Contains("\"brightness\":130", handler.LastRequestBody);
        Assert.Equal("test-token", handler.LastToken);
    }

    /// <summary>固定回應的假 handler,順便記下送出去的內容供斷言。</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public string LastRequestBody { get; private set; } = "";
        public string? LastToken { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content == null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            LastToken = request.Headers.TryGetValues("X-InWave-Token", out var v)
                ? v.FirstOrDefault()
                : null;

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
