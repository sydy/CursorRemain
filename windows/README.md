# Windows 原生托盘（C# / .NET 8）

与 `macos/` Swift 菜单栏应用并列。配置文件是 `%APPDATA%\CursorRemain\config.json`；若只有旧版 `CursorTokenTray` 目录，启动时会迁过去。Token 用当前用户 DPAPI 加密后写入；开机自启写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。

发布是框架依赖：不把 CLR / BCL 打进 exe。运行发布包的机器需要已安装 [.NET 8 Desktop Runtime](https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe)（x64）。`dotnet run` / SDK 开发不受影响。打包版默认对照 GitHub Releases 的 `latest` 自动更新；`dotnet run` 不会覆盖本机文件。

安装版用 Inno Setup 打 `CursorRemain-windows-setup.exe`，默认装到 `%LOCALAPPDATA%\Programs\CursorRemain`（当前用户，无需管理员），这样自动更新仍能原地替换 exe。便携 zip 继续保留。

```powershell
dotnet test CursorTokenCore.Tests\CursorTokenCore.Tests.csproj
dotnet run --project CursorRemain\CursorRemain.csproj
dotnet publish CursorRemain\CursorRemain.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
powershell -File packaging\build_installer.ps1 -PublishDir dist
```
