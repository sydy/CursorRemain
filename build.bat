@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo 请用 .NET 8 打包 Windows 程序（框架依赖，不内嵌运行时）：
echo   dotnet publish windows\CursorRemain\CursorRemain.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
echo.
where dotnet >nul 2>&1
if errorlevel 1 (
  echo 未找到 dotnet，请先安装 .NET 8 SDK。
  exit /b 1
)
dotnet publish windows\CursorRemain\CursorRemain.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
if errorlevel 1 exit /b 1
copy /y "%~dp0windows\packaging\首次运行.txt" "%~dp0dist\首次运行.txt" >nul
echo 产物: %~dp0dist\CursorRemain.exe
echo 运行该 exe 的机器需要已安装 .NET 8 Desktop Runtime。
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" (
  powershell -NoProfile -File "%~dp0windows\packaging\build_installer.ps1" -PublishDir "%~dp0dist" -OutputDir "%~dp0dist"
) else (
  echo 未检测到 Inno Setup，已跳过安装包。需要时安装 https://jrsoftware.org/isinfo.php 后执行:
  echo   powershell -File windows\packaging\build_installer.ps1 -PublishDir dist
)
pause
