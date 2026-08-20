namespace InWave.Services;

/// <summary>
/// 對「不能信任的輸入」做格式驗證。抽成 static 純函式,方便單獨測試,
/// 也讓 Controller 只描述規則、不塞驗證細節。
/// </summary>
public static class InputValidation
{
    /// <summary>
    /// 看檔案開頭的 magic bytes,判斷是不是我們接受的圖片格式(JPEG/PNG/WebP)。
    /// 只驗副檔名擋不住「把任意檔案改名成 .jpg」——那種檔會被瀏覽器與 n8n/模型當圖片去解析。
    /// header 傳前 12 個 byte 即可(WebP 要看到第 12 個)。
    /// </summary>
    public static bool IsSupportedImageHeader(ReadOnlySpan<byte> header)
    {
        // JPEG: FF D8 FF
        if (header.Length >= 3 &&
            header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return true;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (header.Length >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return true;

        // WebP: "RIFF"(52 49 46 46) 四碼,跳過 4 個長度 byte,再 "WEBP"(57 45 42 50)。
        // 光看前四碼不夠——WAV 也是 RIFF 開頭,必須連第 8–11 碼一起看。
        if (header.Length >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
            return true;

        return false;
    }

    /// <summary>
    /// YouTube 影片 ID 一律是 11 個字元,字元集 [A-Za-z0-9_-]。
    /// SavePlaylist 收到的 VideoId 來自推薦頁的隱藏欄位,正常一定合法;
    /// 不合法代表表單被竄改,呼叫端據此擋掉,不讓偽造的 ID 寫進資料庫。
    /// </summary>
    public static bool IsYoutubeVideoId(string? id) =>
        id is { Length: 11 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
}
