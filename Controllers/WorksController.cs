using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMusicBuddy.Data;
using MyMusicBuddy.Models;
using MyMusicBuddy.Models.ViewModels;
using MyMusicBuddy.Services;

namespace MyMusicBuddy.Controllers;

/// <summary>
/// 作品流程:上傳照片 → 選情緒 → 推薦歌曲 → 存歌單 → 檢視作品。
/// 這是整個 MVP 的垂直切片,之後的功能(修圖、滑桿、換歌…)都照這個模式加。
/// </summary>
public class WorksController : Controller
{
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private const long MaxPhotoBytes = 10 * 1024 * 1024; // 10 MB

    private readonly AppDbContext _db;
    private readonly IYouTubeService _youtube;
    private readonly IWebHostEnvironment _env;

    public WorksController(AppDbContext db, IYouTubeService youtube, IWebHostEnvironment env)
    {
        _db = db;
        _youtube = youtube;
        _env = env;
    }

    // GET /Works — 我的作品列表
    public async Task<IActionResult> Index()
    {
        var playlists = await _db.Playlists
            .Include(p => p.Photo)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
        return View(playlists);
    }

    // GET /Works/Create — 上傳照片 + 選情緒
    [HttpGet]
    public IActionResult Create()
    {
        return View();
    }

    // POST /Works/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(IFormFile? photoFile, string? moodName)
    {
        if (photoFile == null || photoFile.Length == 0)
            ModelState.AddModelError("", "請選擇一張照片。");
        else if (photoFile.Length > MaxPhotoBytes)
            ModelState.AddModelError("", "照片超過 10 MB,請換一張或先壓縮。");
        else if (!AllowedExtensions.Contains(Path.GetExtension(photoFile.FileName).ToLowerInvariant()))
            ModelState.AddModelError("", "只接受 jpg、png、webp 圖片。");

        if (string.IsNullOrWhiteSpace(moodName) || !MoodKeywordMapper.AllMoods.Contains(moodName))
            ModelState.AddModelError("", "請選擇一種情緒。");

        if (!ModelState.IsValid)
            return View();

        // 檔名用 GUID,避免使用者檔名衝突或含特殊字元
        var fileName = Guid.NewGuid() + Path.GetExtension(photoFile!.FileName).ToLowerInvariant();
        var uploadDir = Path.Combine(_env.WebRootPath, "uploads");
        Directory.CreateDirectory(uploadDir);
        await using (var stream = System.IO.File.Create(Path.Combine(uploadDir, fileName)))
        {
            await photoFile.CopyToAsync(stream);
        }

        var photo = new Photo
        {
            OriginalPath = "/uploads/" + fileName,
            CreatedAt = DateTime.UtcNow,
            Edit = new PhotoEdit(),                         // 預設值 = 未修圖
            Mood = new MoodProfile { MoodName = moodName! }, // 滑桿先用預設 50
        };
        _db.Photos.Add(photo);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Recommend), new { photoId = photo.Id });
    }

    // GET /Works/Recommend?photoId=1 — 顯示推薦歌曲
    [HttpGet]
    public async Task<IActionResult> Recommend(int photoId)
    {
        var photo = await _db.Photos
            .Include(p => p.Mood)
            .FirstOrDefaultAsync(p => p.Id == photoId);
        if (photo?.Mood == null)
            return NotFound();

        var keyword = MoodKeywordMapper.GetKeyword(photo.Mood.MoodName);
        var results = await _youtube.SearchAsync(keyword);

        var vm = new RecommendViewModel
        {
            PhotoId = photo.Id,
            PhotoPath = photo.EditedPath ?? photo.OriginalPath,
            MoodName = photo.Mood.MoodName,
            SearchKeyword = keyword,
            PlaylistName = $"{photo.Mood.MoodName}・{DateTime.Now:MM/dd}",
            Songs = results.Select(r => new SongInput
            {
                VideoId = r.VideoId,
                Title = r.Title,
                Artist = r.Artist,
                ThumbnailUrl = r.ThumbnailUrl,
                Selected = true, // 預設全選,使用者取消不要的
            }).ToList(),
        };
        return View(vm);
    }

    // POST /Works/SavePlaylist — 把勾選的歌存成歌單
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePlaylist(RecommendViewModel vm)
    {
        var selected = vm.Songs.Where(s => s.Selected).ToList();
        if (selected.Count == 0)
            ModelState.AddModelError("", "至少勾選一首歌。");
        if (string.IsNullOrWhiteSpace(vm.PlaylistName))
            ModelState.AddModelError("", "請輸入歌單名稱。");

        if (!ModelState.IsValid)
            return View(nameof(Recommend), vm);

        var playlist = new Playlist
        {
            Name = vm.PlaylistName.Trim(),
            PhotoId = vm.PhotoId,
            CreatedAt = DateTime.UtcNow,
        };

        var sortOrder = 0;
        foreach (var input in selected)
        {
            // 同一部影片全站只存一筆 Song,先查再建
            var song = await _db.Songs.FirstOrDefaultAsync(s => s.YoutubeVideoId == input.VideoId)
                ?? new Song
                {
                    YoutubeVideoId = input.VideoId,
                    Title = input.Title,
                    Artist = input.Artist,
                    ThumbnailUrl = input.ThumbnailUrl,
                };
            playlist.PlaylistSongs.Add(new PlaylistSong { Song = song, SortOrder = sortOrder++ });
        }

        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id = playlist.Id });
    }

    // GET /Works/Details/5 — 作品頁:照片 + 歌單 + 播放器
    public async Task<IActionResult> Details(int id)
    {
        var playlist = await _db.Playlists
            .Include(p => p.Photo).ThenInclude(ph => ph.Mood)
            .Include(p => p.PlaylistSongs.OrderBy(ps => ps.SortOrder))
                .ThenInclude(ps => ps.Song)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (playlist == null)
            return NotFound();

        return View(playlist);
    }
}
