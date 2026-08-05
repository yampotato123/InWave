namespace MyMusicBuddy.Models;

/// <summary>只保存 YouTube 影片 ID、標題與縮圖網址,不下載任何音樂(企劃書第十一節)。</summary>
public class Song
{
    public int Id { get; set; }
    public string YoutubeVideoId { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>YouTube 的頻道名稱,當作歌手/來源顯示</summary>
    public string Artist { get; set; } = "";

    public string ThumbnailUrl { get; set; } = "";

    public List<PlaylistSong> PlaylistSongs { get; set; } = new();
}
