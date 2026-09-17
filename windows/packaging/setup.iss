; Cursor 余量 Windows 安装程序（用户目录，无需管理员）。
; 本地 / CI：ISCC.exe /DAppPublishDir=... /DRepoRoot=... /DOutputDir=...

#ifndef RepoRoot
  #define RepoRoot AddBackslash(SourcePath) + "..\.."
#endif
#ifndef AppPublishDir
  #define AppPublishDir AddBackslash(RepoRoot) + "dist"
#endif
#ifndef OutputDir
  #define OutputDir RepoRoot
#endif
#ifndef AppVersion
  #define AppVersion "2.0.0"
#endif
#ifdef SourceRevisionId
  #define AppVersionText AppVersion + " (" + Copy(SourceRevisionId, 1, 7) + ")"
#else
  #define AppVersionText AppVersion
#endif

#define MyAppName "Cursor 余量"
#define MyAppExe "CursorRemain.exe"
#define MyAppMutex "Local\CursorTokenTray_SingleInstance_v2"
#define DotNetRuntimeUrl "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{E8C4A1B7-5D29-4F6A-9C3E-1B7A2D4E6F80}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersionText}
AppPublisher=sydy
AppPublisherURL=https://github.com/sydy/CursorTokenTray
AppSupportURL=https://github.com/sydy/CursorTokenTray/issues
AppUpdatesURL=https://github.com/sydy/CursorTokenTray/releases/tag/latest
DefaultDirName={localappdata}\Programs\CursorRemain
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=CursorRemain-windows-setup
SetupIconFile={#RepoRoot}\assets\app_icon.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
AppMutex={#MyAppMutex}
SetupMutex=Local\CursorRemain_Setup
CloseApplications=yes
RestartApplications=no
UsedUserAreasWarning=no
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription={#MyAppName} Windows 安装程序
VersionInfoCompany=sydy
AllowNoIcons=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl,ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#AppPublishDir}\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\windows\packaging\首次运行.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

function HasDotNet8Folder(const BaseDir: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if not DirExists(BaseDir) then
    Exit;
  if FindFirst(AddBackslash(BaseDir) + '8.*', FindRec) then
  begin
    try
      repeat
        if FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function RegistryHasDotNet8: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      if Pos('8.', Names[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
  if RegGetSubkeyNames(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      if Pos('8.', Names[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function IsDotNetDesktop8Installed: Boolean;
begin
  if RegistryHasDotNet8 then
  begin
    Result := True;
    Exit;
  end;
  if HasDotNet8Folder(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App')) then
  begin
    Result := True;
    Exit;
  end;
  if HasDotNet8Folder(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App')) then
  begin
    Result := True;
    Exit;
  end;
  if HasDotNet8Folder(ExpandConstant('{localappdata}\Programs\dotnet\shared\Microsoft.WindowsDesktop.App')) then
  begin
    Result := True;
    Exit;
  end;
  Result := False;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

function ConfirmContinueWithoutRuntime: Boolean;
begin
  Result := MsgBox(
    '未能安装或检测不到 .NET 8 Desktop Runtime（x64）。' + #13#10 +
    '本程序是框架依赖版，没有该运行时将无法启动。' + #13#10 + #13#10 +
    '仍要继续安装 Cursor 余量吗？也可稍后打开：' + #13#10 +
    '{#DotNetRuntimeUrl}',
    mbConfirmation, MB_YESNO) = IDYES;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ErrorCode: Integer;
  RuntimeSetup: String;
begin
  Result := '';
  NeedsRestart := False;
  if IsDotNetDesktop8Installed then
    Exit;

  DownloadPage.Clear;
  DownloadPage.Add('{#DotNetRuntimeUrl}', 'windowsdesktop-runtime-win-x64.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      RuntimeSetup := ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe');
      if Exec(RuntimeSetup, '/install /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ErrorCode) then
      begin
        if (ErrorCode = 3010) or (ErrorCode = 1641) then
          NeedsRestart := True;
      end;
    except
    end;
  finally
    DownloadPage.Hide;
  end;

  if IsDotNetDesktop8Installed then
    Exit;
  if not ConfirmContinueWithoutRuntime then
    Result := '需要先安装 .NET 8 Desktop Runtime（x64）才能继续。';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CursorRemain');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CursorTokenTray');
  end;
end;
