# Windows 原生托盘（C# / .NET 8）

与 `macos/` Swift 菜单栏应用并列。配置文件是 `%APPDATA%\CursorRemain\config.json`；若只有旧版 `CursorTokenTray` 目录，启动时会迁过去。Token 用当前用户 DPAPI 加密后写入；开机自启写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。

发布是自包含单文件：运行时打进 exe，用户不必另装 .NET。`dotnet run` / SDK 开发不受影响。打包版默认对照 GitHub 正式版（`v*`）自动更新：本机已有 .NET 8 Desktop Runtime 时下不含运行时的 `CursorRemain-windows-light.zip`，否则仍下自包含 zip。`dotnet run` 不会覆盖本机文件。版本号来自仓库根目录 `VERSION`。

安装版用 Inno Setup 打 `CursorRemain-windows-setup.exe`，默认装到 `%LOCALAPPDATA%\Programs\CursorRemain`（当前用户，无需管理员），这样自动更新仍能原地替换 exe。便携 zip 继续保留。

```powershell
dotnet test CursorTokenCore.Tests\CursorTokenCore.Tests.csproj
dotnet run --project CursorRemain\CursorRemain.csproj
dotnet publish CursorRemain\CursorRemain.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o dist
powershell -File packaging\build_installer.ps1 -PublishDir dist
```
