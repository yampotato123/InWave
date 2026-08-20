using InWave.Services;

namespace InWave.Tests;

/// <summary>
/// InputValidation:對不可信輸入的格式驗證。
/// 這些分支各自對應一個真實的攻擊面——改名的假圖、RIFF 家族的誤判、被竄改的 VideoId。
/// </summary>
public class InputValidationTests
{
    // ---- 圖片檔頭(magic bytes)----

    [Fact]
    public void JPEG檔頭_通過() =>
        Assert.True(InputValidation.IsSupportedImageHeader(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }));

    [Fact]
    public void PNG檔頭_通過() =>
        Assert.True(InputValidation.IsSupportedImageHeader(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));

    [Fact]
    public void WebP檔頭_通過() =>
        Assert.True(InputValidation.IsSupportedImageHeader(
            new byte[] { 0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 }));

    [Fact]
    public void 改名的執行檔_擋下() =>
        // "MZ"(4D 5A)是 Windows PE 執行檔開頭——改名成 .jpg 也不能放行
        Assert.False(InputValidation.IsSupportedImageHeader(new byte[] { 0x4D, 0x5A, 0x90, 0x00 }));

    [Fact]
    public void 只有RIFF沒有WEBP_擋下() =>
        // WAV 也是 "RIFF"...."WAVE",光看前四碼會誤放;要連第 8–11 碼一起看
        Assert.False(InputValidation.IsSupportedImageHeader(
            new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45 }));

    [Fact]
    public void 太短的檔頭_擋下() =>
        Assert.False(InputValidation.IsSupportedImageHeader(new byte[] { 0x52, 0x49, 0x46, 0x46 }));

    [Fact]
    public void 空檔頭_擋下() =>
        Assert.False(InputValidation.IsSupportedImageHeader(ReadOnlySpan<byte>.Empty));

    // ---- YouTube 影片 ID ----

    [Theory]
    [InlineData("dQw4w9WgXcQ")]   // 標準 11 碼
    [InlineData("kJQP7kiw5Fk")]
    [InlineData("_-aB3cD4eF5")]   // 含合法的 - 和 _
    public void 合法的11碼VideoId_通過(string id) =>
        Assert.True(InputValidation.IsYoutubeVideoId(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tooShort")]        // 8 碼
    [InlineData("waytoolong1234")]  // 14 碼
    [InlineData("elevenchar/")]     // 剛好 11 碼但含非法的 /
    [InlineData("bad char x1")]     // 11 碼但含空白
    public void 不合法的VideoId_擋下(string? id) =>
        Assert.False(InputValidation.IsYoutubeVideoId(id));
}
