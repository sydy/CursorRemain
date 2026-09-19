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
  #define AppVersion "2.1.1"
#endif
#ifdef SourceRevisionId
  #define AppVersionText AppVersion + " (" + Copy(SourceRevisionId, 1, 7) + ")"
#else
  #define AppVersionText AppVersion
#endif

#define MyAppName "Cursor 余量"
#define MyAppExe "CursorRemain.exe"
#define MyAppMutex "Local\CursorRemain_SingleInstance_v2"

[Setup]
AppId={{E8C4A1B7-5D29-4F6A-9C3E-1B7A2D4E6F80}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersionText}
AppPublisher=sydy
AppPublisherURL=https://github.com/sydy/CursorRemain
AppSupportURL=https://github.com/sydy/CursorRemain/issues
AppUpdatesURL=https://github.com/sydy/CursorRemain/releases/latest
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
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CursorRemain');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CursorTokenTray');
  end;
end;
