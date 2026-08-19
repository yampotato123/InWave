using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InWave.Data;
using InWave.Models;
using InWave.Models.ViewModels;
using InWave.Services;

namespace InWave.Controllers;

/// <summary>
/// 作品流程:上傳照片 → 選情緒 → 推薦歌曲 → 存歌單 → 檢視作品。
/// 這是整個 MVP 的垂直切片,之後的功能(修圖、滑桿、換歌…)都照這個模式加。
/// </summary>
public class WorksController : Controller
{
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private const long MaxPhotoBytes = 10 * 1024 * 1024; // 10 MB

    // 合成圖以 data URL 走一般表單欄位送上來。ASP.NET Core 的表單欄位預設上限是 4 MB,
    // 這裡卡在略低的位置,讓使用者看到明確訊息而不是伺服器層的 400。
    private const int MaxEditedDataUrlChars = 4_000_000;
    private const string JpegDataUrlPrefix = "data:image/jpeg;base64,";

    private readonly AppDbContext _db;
    private readonly IYouTubeService _youtube;
    private readonly IPhotoAnalysisService _analysis;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<WorksController> _logger;

    public WorksController(
        AppDbContext db,
        IYouTubeService youtube,
        IPhotoAnalysisService analysis,
        IWebHostEnvironment env,
        ILogger<WorksController> logger)
    {
        _db = db;
        _youtube = youtube;
        _analysis = analysis;
        _env = env;
        _logger = logger;
    }

    // GET /Works — 我的作品列表
    public async Task<IActionResult> Index()
    {
        var playlists = await _db.Playlists
            .Include(p => p.Photo).ThenInclude(ph => ph.Mood)   // 書背印刷色由情緒決定;少了會全部變成預設灰
            .Include(p => p.PlaylistSongs)                      // 收藏櫃要顯示 TRACKS 數;少了會顯示 0
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

        // 情緒可以不選,交給 AI 判讀(設計文件 §3.2.1)。被逼著八選一時使用者只是挑個最接近的,
        // 那不是意圖是雜訊,卻會錨定 AI。有選的話仍必須是 AllMoods 之一。
        if (!string.IsNullOrWhiteSpace(moodName) && !MoodKeywordMapper.AllMoods.Contains(moodName))
            ModelState.AddModelError("", "選到不存在的情緒,請重新選擇。");

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
            // 空字串 = 未指定,待 AI 判讀後回填。MoodProfile 照樣建立,下游的
            // photo.Mood 才不會變 null(Recommend:196 靠它擋 404)。滑桿先用預設 50
            Mood = new MoodProfile { MoodName = moodName?.Trim() ?? "" },
        };
        _db.Photos.Add(photo);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Edit), new { id = photo.Id });
    }

    // GET /Works/Edit/5 — canvas 修圖:3 個滑桿 + 3 種濾鏡
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var photo = await _db.Photos
            .Include(p => p.Edit)
            .Include(p => p.Mood)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (photo == null)
            return NotFound();

        var edit = photo.Edit ?? new PhotoEdit();
        return View(new EditViewModel
        {
            PhotoId = photo.Id,
            PhotoPath = photo.OriginalPath,
            MoodName = photo.Mood?.MoodName ?? "",
            Brightness = edit.Brightness,
            Contrast = edit.Contrast,
            Saturation = edit.Saturation,
            FilterName = edit.FilterName,
        });
    }

    // POST /Works/Edit — 存修圖參數與 canvas 合成後的圖
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditViewModel vm)
    {
        var photo = await _db.Photos
            .Include(p => p.Edit)
            .FirstOrDefaultAsync(p => p.Id == vm.PhotoId);
        if (photo == null)
            return NotFound();

        // 「不套濾鏡」的 radio 送上來是空字串,統一收斂成 null
        var filterName = string.IsNullOrEmpty(vm.FilterName) ? null : vm.FilterName;

        if (!PhotoFilters.IsValid(filterName))
            ModelState.AddModelError("", "選到不存在的濾鏡,請重新選擇。");

        if (string.IsNullOrEmpty(vm.EditedImage))
            ModelState.AddModelError("", "瀏覽器沒有產生預覽圖,請確認 JavaScript 已啟用。");
        else if (vm.EditedImage.Length > MaxEditedDataUrlChars)
            ModelState.AddModelError("", "修圖後的圖片太大,請換一張尺寸小一點的照片。");

        var bytes = ModelState.IsValid ? DecodeJpegDataUrl(vm.EditedImage!) : null;
        if (ModelState.IsValid && bytes == null)
            ModelState.AddModelError("", "預覽圖格式不正確,請重新整理後再試一次。");

        if (!ModelState.IsValid)
        {
            // 重新顯示時 canvas 要重載原圖,PhotoPath 不從表單帶(避免被竄改)
            vm.PhotoPath = photo.OriginalPath;
            return View(vm);
        }

        // 合成圖檔名由原檔推導,重修就原地覆蓋,不會累積孤兒檔
        var editedName = Path.GetFileNameWithoutExtension(photo.OriginalPath) + "_edited.jpg";
        var uploadDir = Path.Combine(_env.WebRootPath, "uploads");
        Directory.CreateDirectory(uploadDir);
        await System.IO.File.WriteAllBytesAsync(Path.Combine(uploadDir, editedName), bytes!);

        var edit = photo.Edit;
        if (edit == null)
        {
            edit = new PhotoEdit { PhotoId = photo.Id };
            _db.PhotoEdits.Add(edit);
        }
        edit.Brightness = Math.Clamp(vm.Brightness, 0, 200);
        edit.Contrast = Math.Clamp(vm.Contrast, 0, 200);
        edit.Saturation = Math.Clamp(vm.Saturation, 0, 200);
        edit.FilterName = filterName;

        photo.EditedPath = "/uploads/" + editedName;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Recommend), new { photoId = photo.Id });
    }

    /// <summary>解出 data URL 裡的 JPEG 位元組;前綴不對或 base64 壞掉回 null。</summary>
    private static byte[]? DecodeJpegDataUrl(string dataUrl)
    {
        if (!dataUrl.StartsWith(JpegDataUrlPrefix, StringComparison.Ordinal))
            return null;
        try
        {
            return Convert.FromBase64String(dataUrl[JpegDataUrlPrefix.Length..]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // GET /Works/Recommend?photoId=1 — 顯示推薦歌曲
    [HttpGet]
    public async Task<IActionResult> Recommend(int photoId)
    {
        var photo = await _db.Photos
            .Include(p => p.Mood)
            .Include(p => p.Edit)       // 濾鏡要參與關鍵字;少了這行 photo.Edit 是 null,濾鏡會安靜失效
            .Include(p => p.Analysis)   // 少了這行每次重新整理都會重打一次 AI
            .FirstOrDefaultAsync(p => p.Id == photoId);
        if (photo?.Mood == null)
            return NotFound();

        // 判讀結果已持久化,同一張照片只打一次 AI(階段 3 步驟 3)
        var analysis = await EnsureAnalysisAsync(photo);

        // 主線:AI 看照片推的歌。AI 沒判成、或推的歌一首都在 YouTube 上找不到時,
        // 回落到規則關鍵字搜尋——功能不會因為 AI 掛掉而消失。
        var results = new List<SongResult>();
        var keyword = MoodKeywordMapper.GetKeyword(photo.Mood.MoodName, photo.Edit?.FilterName);
        var searchDescription = keyword;

        if (analysis is { Ok: true } && analysis.Songs.Count > 0)
        {
            results = await ResolveAsync(analysis.Songs);
            if (results.Count > 0)
            {
                searchDescription = string.IsNullOrWhiteSpace(analysis.Scene)
                    ? "AI 判讀照片後推薦"
                    : $"AI 判讀:{analysis.Scene}";
            }
            else
            {
                _logger.LogWarning(
                    "AI 推的 {Count} 首歌在 YouTube 上一首都沒找到(photoId={PhotoId}),回落關鍵字搜尋",
                    analysis.Songs.Count, photo.Id);
            }
        }

        if (results.Count == 0)
            results = await _youtube.SearchAsync(keyword);

        var vm = new RecommendViewModel
        {
            PhotoId = photo.Id,
            PhotoPath = photo.EditedPath ?? photo.OriginalPath,
            MoodName = photo.Mood.MoodName,
            SearchKeyword = searchDescription,
            // 情緒未指定時不要留下開頭孤伶伶的「・」
            PlaylistName = string.IsNullOrEmpty(photo.Mood.MoodName)
                ? $"作品・{DateTime.Now:MM/dd}"
                : $"{photo.Mood.MoodName}・{DateTime.Now:MM/dd}",
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

    /// <summary>
    /// 確保這張照片有 AI 判讀結果:沒判讀過才呼叫 AI,判過就直接用資料庫那筆。
    ///
    /// 為什麼一定要持久化:Recommend 是 GET,使用者按重新整理就會再跑一次。
    /// 沒有這層擋著的話,每次重新整理都是一次付費呼叫,而且要等 16–105 秒。
    ///
    /// 送的是原圖(OriginalPath),不是 EditedPath——濾鏡以文字進入 prompt,
    /// 送合成圖會讓濾鏡被算兩次(設計文件 §3.2)。
    ///
    /// 判讀失敗時**不寫入**,讓下次重新整理可以重試;失敗只記 log,不影響畫面。
    /// </summary>
    private async Task<PhotoAnalysisResult?> EnsureAnalysisAsync(Photo photo)
    {
        if (photo.Analysis != null)
        {
            _logger.LogInformation(
                "已有 AI 判讀(photoId={PhotoId},{AnalyzedAt:yyyy-MM-dd HH:mm}),不重複呼叫",
                photo.Id, photo.Analysis.AnalyzedAt);
            return _analysis.ParseRaw(photo.Analysis.RawJson);
        }

        var fullPath = Path.Combine(_env.WebRootPath, photo.OriginalPath.TrimStart('/'));
        if (!System.IO.File.Exists(fullPath))
        {
            _logger.LogWarning("AI 判讀略過:找不到原圖 {Path}", fullPath);
            return null;
        }

        var result = await _analysis.AnalyzeAsync(
            imageBytes: await System.IO.File.ReadAllBytesAsync(fullPath),
            mimeType: MimeTypeOf(fullPath),
            // MoodName 用空字串當「未指定」的哨兵值(2026-08-17 定案,零 migration)
            mood: string.IsNullOrEmpty(photo.Mood!.MoodName) ? null : photo.Mood.MoodName,
            filter: photo.Edit?.FilterName,
            brightness: photo.Edit?.Brightness ?? 100,
            contrast: photo.Edit?.Contrast ?? 100,
            saturation: photo.Edit?.Saturation ?? 100);

        if (!result.Ok)
        {
            // 不寫入,下次重新整理會重試。AI 掛掉不該在資料庫留下一筆空判讀擋住後續。
            _logger.LogWarning("AI 判讀失敗(photoId={PhotoId}):{Error}", photo.Id, result.Error);
            return result;
        }

        _db.PhotoAnalyses.Add(new PhotoAnalysis
        {
            PhotoId = photo.Id,
            RawJson = result.RawJson,
            Scene = result.Scene,
            AnalyzedAt = DateTime.UtcNow,
            ModelUsed = result.Model,
        });
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "AI 判讀成功並已存檔(photoId={PhotoId},model={Model}):scene={Scene} moodPick={MoodPick} mood=[{Mood}] songs={Songs}",
            photo.Id,
            result.Model ?? "(未標註)",
            result.Scene,
            result.MoodPick ?? "(不在八種內)",
            string.Join('、', result.Mood),
            string.Join(" / ", result.Songs.Select(s => $"{s.Artist} - {s.Title}")));

        return result;
    }

    /// <summary>
    /// 把 AI 推的「歌手 + 歌名」逐首換成可播放的 YouTube 影片。找不到的直接跳過。
    ///
    /// 刻意循序而非並行:AppDbContext 不是執行緒安全的,而 FindVideoAsync 會讀寫快取表。
    /// 五首歌命中快取時只是五次本機查詢,不值得為此引入 scope 管理。
    /// </summary>
    private async Task<List<SongResult>> ResolveAsync(IReadOnlyList<AnalyzedSong> songs)
    {
        var resolved = new List<SongResult>();
        foreach (var song in songs)
        {
            var found = await _youtube.FindVideoAsync(song.Artist, song.Title);
            if (found != null)
                resolved.Add(found);
        }
        return resolved;
    }

    private static string MimeTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };

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
