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
         "keywords":["sunset"],"songs":[{"artist":"Max Richter","title":"On the Nature of Daylight","why":"溫柔"}],
         "model":"models/gemini-3.6-flash"}
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
        // 由工作流標註,存進 PhotoAnalysis.ModelUsed 供日後比較模型版本
        Assert.Equal("models/gemini-3.6-flash", result.Model);
    }

    [Fact]
    public async Task 工作流沒標註model時_不阻斷判讀()
    {
        var result = await AnalyzeAsync(HttpStatusCode.OK,
            """{"ok":true,"scene":"海邊","moodPick":"安靜","songs":[]}""");

        Assert.True(result.Ok);
        Assert.Null(result.Model);
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
    public async Task 逾時_回TIMEOUT而不是把例外往外丟()
    {
        // Gemini 實測延遲 16-105 秒,逾時是常態不是例外。
        // 這條路若丟例外,整個推薦頁會 500,而不是安靜回落到關鍵字搜尋。
        var http = new HttpClient(new NeverRespondsHandler())
        {
            Timeout = TimeSpan.FromMilliseconds(300),
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["N8n:AnalyzeUrl"] = "http://n8n.test/webhook/inwave/analyze",
            })
            .Build();
        var service = new PhotoAnalysisService(http, config, NullLogger<PhotoAnalysisService>.Instance);

        var result = await service.AnalyzeAsync(new byte[] { 1 }, "image/jpeg", null, null, 100, 100, 100);

        Assert.False(result.Ok);
        Assert.Equal("TIMEOUT", result.Error);
    }

    [Fact]
    public async Task 連不上n8n_回NETWORK()
    {
        // n8n 容器停掉時就是這條路(連線被拒)
        var http = new HttpClient(new ThrowsHandler(new HttpRequestException("Connection refused")));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["N8n:AnalyzeUrl"] = "http://n8n.test/webhook/inwave/analyze",
            })
            .Build();
        var service = new PhotoAnalysisService(http, config, NullLogger<PhotoAnalysisService>.Instance);

        var result = await service.AnalyzeAsync(new byte[] { 1 }, "image/jpeg", null, null, 100, 100, 100);

        Assert.False(result.Ok);
        Assert.Equal("NETWORK", result.Error);
    }

    // ── 型別不符:AI 可以回任何形狀,而本服務對外宣告「不丟例外」。
    //    這些案例先前會丟 InvalidOperationException,結果是整頁 500 而不是回落。
    [Theory]
    [InlineData("""[{"ok":true}]""")]          // 最外層是陣列
    [InlineData("""[]""")]
    [InlineData(""""just a string"""")]
    [InlineData("""42""")]
    public async Task 回應最外層不是物件_回BAD_JSON而不是丟例外(string body)
    {
        var result = await AnalyzeAsync(HttpStatusCode.OK, body);

        Assert.False(result.Ok);
        Assert.Equal("BAD_JSON", result.Error);
    }

    [Theory]
    [InlineData("""{"ok":"true","scene":"x"}""")]   // 字串而非布林
    [InlineData("""{"ok":1,"scene":"x"}""")]        // 數字
    [InlineData("""{"ok":null,"scene":"x"}""")]
    [InlineData("""{"scene":"x"}""")]               // 根本沒有 ok
    public async Task ok不是布林true_一律視為失敗(string body)
    {
        var result = await AnalyzeAsync(HttpStatusCode.OK, body);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task error欄位不是字串_不丟例外()
    {
        var result = await AnalyzeAsync(HttpStatusCode.OK, """{"ok":false,"error":500}""");

        Assert.False(result.Ok);
        Assert.Equal("UNKNOWN", result.Error);   // 取不到就用預設值,不是炸掉
    }

    [Fact]
    public async Task 陣列裡混雜非字串_過濾掉而不是丟例外()
    {
        var result = await AnalyzeAsync(HttpStatusCode.OK,
            """{"ok":true,"mood":["夢幻",42,null,"懷舊"],"keywords":"不是陣列","songs":[]}""");

        Assert.True(result.Ok);
        Assert.Equal(new[] { "夢幻", "懷舊" }, result.Mood);
        Assert.Empty(result.Keywords);
    }

    [Fact]
    public async Task 歌曲缺歌手或歌名_跳過那筆()
    {
        var result = await AnalyzeAsync(HttpStatusCode.OK, """
            {"ok":true,"songs":[{"artist":"","title":"沒有歌手"},
                                {"artist":"有歌手","title":""},
                                {"artist":"完整","title":"這首"},
                                "這根本不是物件"]}
            """);

        Assert.Single(result.Songs);
        Assert.Equal("完整", result.Songs[0].Artist);
    }

    [Fact]
    public async Task 歌曲數量超過五首_只取前五首()
    {
        // prompt 要 5 首,但模型漂移時可能回更多。一首 = 一次 YouTube 搜尋 = 100 單位,
        // 不設上限的話 40 首就是 4,000 單位(一天只有 10,000)。
        var many = string.Join(",", Enumerable.Range(1, 40)
            .Select(i => $$"""{"artist":"歌手{{i}}","title":"歌{{i}}"}"""));
        var result = await AnalyzeAsync(HttpStatusCode.OK, $$"""{"ok":true,"songs":[{{many}}]}""");

        Assert.Equal(5, result.Songs.Count);
        Assert.Equal("歌手1", result.Songs[0].Artist);
    }

    [Fact]
    public void ParseRaw_與首次判讀走同一段解析()
    {
        // 每次快取命中都走這條路。若與 AnalyzeAsync 的解析不一致,
        // 「第一次看到的推薦」與「重新整理後看到的」會不一樣。
        var service = MakeService(HttpStatusCode.OK, "");

        var result = service.ParseRaw("""{"ok":true,"scene":"海邊","moodPick":"寂寥","mood":["寂寥"]}""");

        Assert.True(result.Ok);
        Assert.Equal("海邊", result.Scene);
        Assert.Null(result.MoodPick);   // 八種以外,這裡也要擋掉
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

    /// <summary>永遠不回應,用來觸發 HttpClient 的逾時。</summary>
    private sealed class NeverRespondsHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("不會執行到這裡");
        }
    }

    /// <summary>丟出指定例外,模擬連線層失敗。</summary>
    private sealed class ThrowsHandler : HttpMessageHandler
    {
        private readonly Exception _ex;

        public ThrowsHandler(Exception ex) => _ex = ex;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(_ex);
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
