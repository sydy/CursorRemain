# Windows

.NET 8 托盘程序，与 `macos/` 并列。使用说明见仓库根目录 [README.md](../README.md)。

配置在 `%APPDATA%\CursorRemain\config.json`，Token 用当前用户 DPAPI 加密。开机自启写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。版本号来自仓库根目录 `VERSION`。

```powershell
dotnet test CursorTokenCore.Tests\CursorTokenCore.Tests.csproj
dotnet run --project CursorRemain\CursorRemain.csproj
dotnet publish CursorRemain\CursorRemain.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o dist
powershell -File packaging\build_installer.ps1 -PublishDir dist
```

发布包自带运行时。安装版默认放到 `%LOCALAPPDATA%\Programs\CursorRemain`，便于自动更新原地替换。`dotnet run` 不会覆盖已安装的程序。
