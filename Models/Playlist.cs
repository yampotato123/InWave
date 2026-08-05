namespace MyMusicBuddy.Models;

public class Playlist
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int PhotoId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Photo Photo { get; set; } = null!;
    public List<PlaylistSong> PlaylistSongs { get; set; } = new();
}
