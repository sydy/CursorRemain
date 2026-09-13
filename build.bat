@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo 请用 .NET 8 打包 Windows 程序（框架依赖，不内嵌运行时）：
echo   dotnet publish windows\CursorTokenTray\CursorTokenTray.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
echo.
where dotnet >nul 2>&1
if errorlevel 1 (
  echo 未找到 dotnet，请先安装 .NET 8 SDK。
  exit /b 1
)
dotnet publish windows\CursorTokenTray\CursorTokenTray.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
if errorlevel 1 exit /b 1
copy /y "%~dp0windows\packaging\首次运行.txt" "%~dp0dist\首次运行.txt" >nul
echo 产物: %~dp0dist\CursorTokenTray.exe
echo 运行该 exe 的机器需要已安装 .NET 8 Desktop Runtime。
pause
