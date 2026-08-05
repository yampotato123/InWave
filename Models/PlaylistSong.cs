namespace MyMusicBuddy.Models;

/// <summary>歌單與歌曲的多對多關聯,SortOrder 決定歌曲在歌單中的順序。</summary>
public class PlaylistSong
{
    public int Id { get; set; }
    public int PlaylistId { get; set; }
    public int SongId { get; set; }
    public int SortOrder { get; set; }

    public Playlist Playlist { get; set; } = null!;
    public Song Song { get; set; } = null!;
}
