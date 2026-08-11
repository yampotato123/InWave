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
COPY MyMusicBuddy.csproj .
RUN dotnet restore MyMusicBuddy.csproj

# 再複製其餘原始碼(哪些檔案會進來由 .dockerignore 決定)
COPY . .

# --no-restore:上一步已經 restore 過,不必再做一次
RUN dotnet publish MyMusicBuddy.csproj -c Release -o /app/publish --no-restore

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

ENTRYPOINT ["dotnet", "MyMusicBuddy.dll"]
