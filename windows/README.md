# Windows 原生托盘（C# / .NET 8）

与 `macos/` Swift 菜单栏应用并列。配置文件仍是 `%APPDATA%\CursorTokenTray\config.json`。Token 用当前用户 DPAPI 加密后写入；开机自启写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。

发布是框架依赖：不把 CLR / BCL 打进 exe。运行发布包的机器需要已安装 [.NET 8 Desktop Runtime](https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe)（x64）。`dotnet run` / SDK 开发不受影响。

```powershell
dotnet test CursorTokenCore.Tests\CursorTokenCore.Tests.csproj
dotnet run --project CursorTokenTray\CursorTokenTray.csproj
dotnet publish CursorTokenTray\CursorTokenTray.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```
