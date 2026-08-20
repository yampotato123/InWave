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
/// FindVideosAsync:一次找多首(AI 推薦的整組歌),未命中的並行打 API。
///
/// 批次版最容易錯的是「結果索引對齊」——第 N 首找不到時,不能讓後一首的影片頂上來。
/// 這裡用一個能依歌名(URL 的 q 參數)回不同結果的假 handler,不打真的 YouTube API。
/// </summary>
public class YouTubeServiceFindVideosTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;

    public YouTubeServiceFindVideosTests()
    {
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

    private YouTubeService MakeService(RoutingHandler handler, string apiKey = "test-key")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["YouTube:ApiKey"] = apiKey })
            .Build();
        return new YouTubeService(new HttpClient(handler), _db, config,
            NullLogger<YouTubeService>.Instance);
    }

    /// <summary>回一支頻道就是「歌手 - Topic」的原曲。</summary>
    private static string OneHit(string videoId, string artist) =>
        JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new
                {
                    id = new { videoId },
                    snippet = new
                    {
                        title = "some title",
                        channelTitle = $"{artist} - Topic",
                        liveBroadcastContent = "none",
                        thumbnails = new { medium = new { url = $"https://i.ytimg.com/vi/{videoId}/mqdefault.jpg" } },
                    },
                },
            },
        });

    private const string Empty = """{"items":[]}""";

    [Fact]
    public async Task 結果與輸入同順序對齊_中間找不到的是null()
    {
        // 第二首(B)故意回空,驗證它變 null 而不會讓第三首的影片頂到第二格
        var handler = new RoutingHandler(q =>
            q.Contains("Aaa") ? OneHit("vidA", "Aaa") :
            q.Contains("Ccc") ? OneHit("vidC", "Ccc") :
            Empty);

        var result = await MakeService(handler).FindVideosAsync(new[]
        {
            ("Aaa", "song1"),
            ("Bbb", "song2"),   // 找不到
            ("Ccc", "song3"),
        });

        Assert.Equal(3, result.Count);
        Assert.Equal("vidA", result[0]!.VideoId);
        Assert.Null(result[1]);
        Assert.Equal("vidC", result[2]!.VideoId);
    }

    [Fact]
    public async Task 命中快取的不重打API_只對未命中的打()
    {
        var handler = new RoutingHandler(q => q.Contains("Aaa") ? OneHit("vidA", "Aaa") : OneHit("vidB", "Bbb"));
        var service = MakeService(handler);

        // 先單獨找 A,把它寫進快取
        await service.FindVideoAsync("Aaa", "song1");
        Assert.Equal(1, handler.CallCount);

        // 批次找 A(已快取)+ B(未快取):只應為 B 打一次 API
        var result = await service.FindVideosAsync(new[] { ("Aaa", "song1"), ("Bbb", "song2") });

        Assert.Equal(2, handler.CallCount);   // 不是 3:A 走快取
        Assert.Equal("vidA", result[0]!.VideoId);
        Assert.Equal("vidB", result[1]!.VideoId);
    }

    [Fact]
    public async Task 空歌手或歌名的位置_是null且不打API()
    {
        var handler = new RoutingHandler(_ => OneHit("vidA", "Aaa"));

        var result = await MakeService(handler).FindVideosAsync(new[]
        {
            ("Aaa", "song1"),
            ("", "song2"),      // 無效
            ("Ccc", ""),        // 無效
        });

        Assert.Equal("vidA", result[0]!.VideoId);
        Assert.Null(result[1]);
        Assert.Null(result[2]);
        Assert.Equal(1, handler.CallCount);   // 只為有效的 A 打了一次
    }

    [Fact]
    public async Task 第二次批次同一組_全部走快取不打API()
    {
        var handler = new RoutingHandler(q => q.Contains("Aaa") ? OneHit("vidA", "Aaa") : Empty);
        var service = MakeService(handler);

        var songs = new[] { ("Aaa", "song1"), ("Bbb", "song2") };
        await service.FindVideosAsync(songs);
        var callsAfterFirst = handler.CallCount;

        await service.FindVideosAsync(songs);

        // 第二次全命中快取(含「找不到」的空結果),一次 API 都不再打
        Assert.Equal(callsAfterFirst, handler.CallCount);
    }

    [Fact]
    public async Task 沒設ApiKey時_未命中的維持null也不打API()
    {
        var handler = new RoutingHandler(_ => OneHit("vidA", "Aaa"));

        var result = await MakeService(handler, apiKey: "").FindVideosAsync(new[]
        {
            ("Aaa", "song1"),
            ("Bbb", "song2"),
        });

        Assert.Null(result[0]);
        Assert.Null(result[1]);
        Assert.Equal(0, handler.CallCount);
    }

    /// <summary>依 URL 的 q 參數回不同結果,並計數。用來驗證「哪幾首真的打了 API」。</summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<string, string> _bodyForQuery;
        private int _callCount;

        public int CallCount => _callCount;

        public RoutingHandler(Func<string, string> bodyForQuery) => _bodyForQuery = bodyForQuery;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);   // 並行打 API 時計數不能漏算
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_bodyForQuery(query), Encoding.UTF8, "application/json"),
            });
        }
    }
}
