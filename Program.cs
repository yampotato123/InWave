using Microsoft.EntityFrameworkCore;
using InWave.Data;
using InWave.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// YouTubeService 需要 HttpClient,用 typed client 註冊
builder.Services.AddHttpClient<IYouTubeService, YouTubeService>();

// AI 判讀照片(n8n → Gemini)。實測延遲 16–105 秒且落差很大,預設 30 秒會誤殺,
// 所以拉到 120 秒;超過就當它掛了,由呼叫端走 MoodKeywordMapper fallback。
builder.Services.AddHttpClient<IPhotoAnalysisService, PhotoAnalysisService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(
        builder.Configuration.GetValue<int?>("N8n:TimeoutSeconds") ?? 120);
})
// n8n 容器停著時,連線被拒之前預設會等約 11 秒(實測),而那是每一張新照片都要付的等待。
// 連得上的話握手是毫秒等級,所以 5 秒足夠區分「慢」與「不在」。
// 這個上限只管建立連線,不影響上面那個等回應的 120 秒。
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    ConnectTimeout = TimeSpan.FromSeconds(5),
});

var app = builder.Build();

// 容器首次啟動時資料庫檔案不存在,啟動時自動套用 migration 建好。
// 本機用 `dotnet ef database update` 效果相同,兩邊不衝突。
// 注意:多個執行個體同時啟動會有競爭,單機使用沒問題,正式環境要另外處理。
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// 容器內只開 http 埠,TLS 由外層(Tailscale serve 或反向代理)負責。
// DOTNET_RUNNING_IN_CONTAINER 由微軟官方映像檔自動設定。
// 留著這行在容器中雖然不會轉址(找不到 https 埠),但每個請求都會記一筆警告。
if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
