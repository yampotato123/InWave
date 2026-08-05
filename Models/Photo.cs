namespace MyMusicBuddy.Models;

public class Photo
{
    public int Id { get; set; }

    /// <summary>原始上傳圖片的路徑(相對於 wwwroot,例:/uploads/xxx.jpg)</summary>
    public string OriginalPath { get; set; } = "";

    /// <summary>修圖後圖片的路徑;尚未修圖時為 null</summary>
    public string? EditedPath { get; set; }

    public DateTime CreatedAt { get; set; }

    public PhotoEdit? Edit { get; set; }
    public MoodProfile? Mood { get; set; }
    public List<Playlist> Playlists { get; set; } = new();
}
