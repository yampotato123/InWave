using System.Net;
using System.Text;
using System.Text.Json;
using InWave.Data;
using InWave.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace InWave.Tests;

/// <summary>
/// FindVideoAsync:把 AI 推的「歌手 + 歌名」換成一支可播放的影片。
///
/// 這裡的每個分支都對應真實成本或真實錯誤:挑錯影片會播到翻唱或直播,
/// 少一層快取每次重新整理就燒 100 單位配額(一天只有 10,000)。
/// 用假的 HttpMessageHandler,不打真的 YouTube API。
/// </summary>
public class YouTubeServiceFindVideoTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;

    public YouTubeServiceFindVideoTests()
    {
        // 記憶體資料庫:連線關掉就消失,測試之間互不影響
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private YouTubeService MakeService(StubHandler handler, string apiKey = "test-key")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["YouTube:ApiKey"] = apiKey })
            .Build();

        return new YouTubeService(new HttpClient(handler), _db, config,
            NullLogger<YouTubeService>.Instance);
    }

    /// <summary>組一份 YouTube search API 形狀的回應。用序列化而不是手拼字串,免得漏一個括號。</summary>
    private static string Items(params (string VideoId, string Title, string Channel, string Live)[] items) =>
        JsonSerializer.Serialize(new
        {
            items = items.Select(i => new
            {
                id = new { videoId = i.VideoId },
                snippet = new
                {
                    title = i.Title,
                    channelTitle = i.Channel,
                    liveBroadcastContent = i.Live,
                    thumbnails = new
                    {
                        medium = new { url = $"https://i.ytimg.com/vi/{i.VideoId}/mqdefault.jpg" },
                    },
                },
            }),
        });

    [Fact]
    public async Task 有Topic頻道時_優先選它()
    {
        // 「- Topic」是 YouTube 為唱片公司音源自動建立的頻道,幾乎一定是原曲
        var handler = new StubHandler(HttpStatusCode.OK, Items(
            ("cover1", "Space Song (Cover)", "某個翻唱頻道", "none"),
            ("topic1", "Space Song", "Beach House - Topic", "none")));

        var result = await MakeService(handler).FindVideoAsync("Beach House", "Space Song");

        Assert.NotNull(result);
        Assert.Equal("topic1", result.VideoId);
    }

    [Fact]
    public async Task 別的歌手的Topic頻道_不能贏過本人的頻道()
    {
        // 「- Topic」是任何發行到串流的作品都會有的,翻唱歌手也有自己的 Topic 頻道。
        // 只看 Topic 前綴的話,會把翻唱當成原曲。
        var handler = new StubHandler(HttpStatusCode.OK, Items(
            ("cover-topic", "スパークル (Cover)", "某翻唱歌手 - Topic", "none"),
            ("official", "Sparkle - movie ver.", "RADWIMPS - Topic", "none")));

        var result = await MakeService(handler).FindVideoAsync("RADWIMPS", "スパークル");

        Assert.Equal("official", result!.VideoId);
    }

    [Fact]
    public async Task 沒有Topic頻道時_選頻道名含歌手名的()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Items(
            ("other1", "Space Song 分析", "音樂雜談", "none"),
            ("official1", "Space Song", "Beach House Official", "none")));

        var result = await MakeService(handler).FindVideoAsync("Beach House", "Space Song");

        Assert.Equal("official1", result!.VideoId);
    }

    [Fact]
    public async Task 都不符合時_退回第一筆而不是放棄()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Items(
            ("first1", "Space Song", "某個播放清單頻道", "none"),
            ("second1", "Space Song", "另一個頻道", "none")));

        var result = await MakeService(handler).FindVideoAsync("Beach House", "Space Song");

        Assert.Equal("first1", result!.VideoId);
    }

    [Fact]
    public async Task 直播與首播要濾掉()
    {
        // 直播嵌入播放常有限制,而且不會是那首歌本身
        var handler = new StubHandler(HttpStatusCode.OK, Items(
            ("live1", "24/7 lofi radio", "Beach House - Topic", "live"),
            ("normal1", "Space Song", "某個頻道", "none")));

        var result = await MakeService(handler).FindVideoAsync("Beach House", "Space Song");

        Assert.Equal("normal1", result!.VideoId);   // Topic 頻道那支是直播,不能因為前綴對就選它
    }

    [Fact]
    public async Task 第二次呼叫同一首_不再打API()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Items(
            ("topic1", "Space Song", "Beach House - Topic", "none")));
        var service = MakeService(handler);

        await service.FindVideoAsync("Beach House", "Space Song");
        var second = await service.FindVideoAsync("Beach House", "Space Song");

        Assert.Equal(1, handler.CallCount);
        Assert.Equal("topic1", second!.VideoId);
    }

    [Fact]
    public async Task 找不到的歌也要快取_否則每次重新整理都燒配額()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"items":[]}""");
        var service = MakeService(handler);

        var first = await service.FindVideoAsync("查無此人", "查無此曲");
        var second = await service.FindVideoAsync("查無此人", "查無此曲");

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, handler.CallCount);   // 第二次是快取命中,不是又問了一次
    }

    [Fact]
    public async Task API出錯時_不快取以免暫時性故障被記住七天()
    {
        var handler = new StubHandler(HttpStatusCode.Forbidden, """{"error":{"message":"quota exceeded"}}""");
        var service = MakeService(handler);

        var first = await service.FindVideoAsync("Beach House", "Space Song");
        var second = await service.FindVideoAsync("Beach House", "Space Song");

        Assert.Null(first);
        Assert.Equal(2, handler.CallCount);   // 有重試,沒把配額用盡的狀態當成「這首歌不存在」
    }

    [Fact]
    public async Task 沒設ApiKey時_不打API也不炸()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Items(("x", "y", "z", "none")));

        var result = await MakeService(handler, apiKey: "").FindVideoAsync("Beach House", "Space Song");

        Assert.Null(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("", "Space Song")]
    [InlineData("Beach House", "")]
    [InlineData("   ", "   ")]
    public async Task 歌手或歌名是空的_直接回null不浪費配額(string artist, string title)
    {
        var handler = new StubHandler(HttpStatusCode.OK, Items(("x", "y", "z", "none")));

        var result = await MakeService(handler).FindVideoAsync(artist, title);

        Assert.Null(result);
        Assert.Equal(0, handler.CallCount);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public int CallCount { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
