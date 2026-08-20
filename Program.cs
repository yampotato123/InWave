using Microsoft.EntityFrameworkCore;
using InWave.Data;
using InWave.Services;

var builder = WebApplication.CreateBuilder(args);

// 掛進來的金鑰檔。優先權最高,蓋過 appsettings.json 與環境變數;檔案不存在就跳過。
//
// 誰負責放這個檔,依環境而異——**程式碼不必知道差別**:System.IO.InvalidDataException: 'Failed to load configuration from file 'C:\Users\admin\AppData\Roaming\Microsoft\UserSecrets\23d905ff-b3e8-4062-88ce-cfa5c7e6c3d9\secrets.json'.'

//   開發機   docker-compose.override.yml 把 user-secrets 目錄唯讀掛到 /app/secrets,
//            所以容器與本機 F5 共用同一份金鑰(容器讀不到 user-secrets:檔案在主機的
//            %APPDATA%,而且那個組態來源只在 Development 環境才會被加入)
//   正式環境 平台的 secret volume 掛到同一個路徑(Kubernetes Secret、
//            Azure Container Apps 的 secret 掛載都是檔案),或什麼都不掛,改用環境變數
//
// 為什麼用檔案而不是只用環境變數:**環境變數換不了**。它在容器建立時就固定,
// 連 docker restart 都讀不到新值,要換金鑰得重建容器。檔案來源支援熱重載,
// 所以換金鑰存檔即生效——正式環境輪替金鑰時,Secret volume 原地更新即可,不必重啟。
builder.Configuration.AddJsonFile("secrets/secrets.json", optional: true, reloadOnChange: true);

builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// YouTubeService 需要 HttpClient,用 typed client 註冊
builder.Services.AddHttpClient<IYouTubeService, YouTubeService>();

// AI 判讀照片(n8n → 視覺模型)。實測延遲:OpenAI 約 8–11 秒,先前的 Gemini 是 16–105 秒。
// 上限抓 120 秒是為了容納最慢的情況,換供應商不必跟著調;
// 超過就當它掛了,由呼叫端走 MoodKeywordMapper fallback。
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
