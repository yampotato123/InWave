using Microsoft.EntityFrameworkCore;
using MyMusicBuddy.Models;

namespace MyMusicBuddy.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<PhotoEdit> PhotoEdits => Set<PhotoEdit>();
    public DbSet<MoodProfile> MoodProfiles => Set<MoodProfile>();
    public DbSet<Song> Songs => Set<Song>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistSong> PlaylistSongs => Set<PlaylistSong>();
    public DbSet<SearchCache> SearchCaches => Set<SearchCache>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 同一部 YouTube 影片在 Songs 表只存一筆,加入歌單時先查再建
        modelBuilder.Entity<Song>()
            .HasIndex(s => s.YoutubeVideoId)
            .IsUnique();

        // 快取查詢都是用關鍵字找,加索引
        modelBuilder.Entity<SearchCache>()
            .HasIndex(c => c.Query);

        // 一張照片只有一組修圖設定與一組情緒設定
        modelBuilder.Entity<Photo>()
            .HasOne(p => p.Edit)
            .WithOne(e => e.Photo)
            .HasForeignKey<PhotoEdit>(e => e.PhotoId);

        modelBuilder.Entity<Photo>()
            .HasOne(p => p.Mood)
            .WithOne(m => m.Photo)
            .HasForeignKey<MoodProfile>(m => m.PhotoId);
    }
}
