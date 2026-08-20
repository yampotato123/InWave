# syntax=docker/dockerfile:1

# ============================================================
# 第一階段:建置
# ============================================================
# sdk 映像檔含編譯器、NuGet、EF 工具,體積大(約 3 GB),但只有這裡用得到。
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先只複製專案檔,單獨跑一次 restore。
#
# 為什麼要分兩次複製:Docker 把每個指令的結果存成一層並快取,
# 只有當該層的輸入改變時才重新執行。專案檔沒動的話,
# restore 這層直接命中快取 —— 改一行 C# 不必重新下載所有 NuGet 套件。
# 若直接 `COPY . .` 再 restore,任何一個字的改動都會讓套件重下一次。
COPY InWave.csproj .
RUN dotnet restore InWave.csproj

# 再複製其餘原始碼(哪些檔案會進來由 .dockerignore 決定)
COPY . .

# --no-restore:上一步已經 restore 過,不必再做一次
RUN dotnet publish InWave.csproj -c Release -o /app/publish --no-restore

# ============================================================
# 第二階段:執行
# ============================================================
# 換成 aspnet 映像檔:只有執行期,沒有編譯器也沒有 SDK,體積約為 sdk 的四分之一。
# 第一階段的所有東西(原始碼、NuGet 快取、編譯器)都不會進到最終映像檔。
#
# 這也是為什麼不能在容器內執行 `dotnet ef database update`
# —— `dotnet ef` 是 SDK 工具,這一層沒有它。
# 資料庫建立改由 Program.cs 啟動時呼叫 Database.Migrate() 完成。
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# 容器內只走 http。TLS 由外層處理(Tailscale serve 或反向代理),
# 容器自己不管憑證 —— 這是容器化 web 應用的標準分工。
ENV ASPNETCORE_HTTP_PORTS=8080

# EXPOSE 只是文件用途,宣告這個映像檔預期使用哪個埠。
# 實際對外開放由 compose 的 ports 決定。
EXPOSE 8080

COPY --from=build /app/publish .

# ------------------------------------------------------------
# 以非 root 身分執行
# ------------------------------------------------------------
# 官方 aspnet 映像檔內建一個 UID 1654 的 app 使用者,但**預設不會切過去**,
# 所以不寫這幾行的話容器是用 root 跑的(可用 `docker exec inwave id` 確認)。
#
# 為什麼要切:容器逃逸或應用程式被攻破時,root 的破壞力大得多。
# 這個站沒有認證機制、又接受檔案上傳,更沒有理由用 root 跑。
#
# 埠 8080 是刻意的:1024 以下的埠只有 root 能綁,
# 這也是 .NET 8 起把預設埠從 80 改成 8080 的原因。
ARG APP_UID=1654

# 執行期要寫入的兩個目錄先建好並轉移擁有權。
# compose 會把主機目錄掛到這兩個路徑上蓋掉內容,但:
#   - Windows/Docker Desktop:掛載層不套用 Linux 擁有權,寫入不受影響
#   - Linux:主機端目錄的擁有者要能讓 UID 1654 寫入(見 README)
# 沒有掛載時(例如單獨跑映像檔)則靠這裡建立的擁有權。
RUN mkdir -p /app/docker-data /app/wwwroot/uploads \
 && chown -R $APP_UID:$APP_UID /app

USER $APP_UID

ENTRYPOINT ["dotnet", "InWave.dll"]
