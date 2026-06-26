#define MyAppName "盔盔游戏助手"
#define MyAppEnglishName "KuikuiGameAssistant"
#define MyAppPublisher "chenkkano-sketch"
#define MyAppExeName "KuikuiGameAssistant.exe"

#ifndef AppVersion
#define AppVersion "0.1.0"
#endif

#ifndef SourceDir
#define SourceDir "..\artifacts\publish\win-x64"
#endif

#ifndef OutputDir
#define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{9F6DD9B1-24C1-4F22-ACD8-64F92F3F2E49}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/chenkkano-sketch/kuikui-gameAssistant
AppSupportURL=https://github.com/chenkkano-sketch/kuikui-gameAssistant/issues
AppUpdatesURL=https://github.com/chenkkano-sketch/kuikui-gameAssistant/releases
DefaultDirName={autopf}\{#MyAppEnglishName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=KuikuiGameAssistant-{#AppVersion}-setup
SetupIconFile=..\src\KuikuiGameAssistant\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
CloseApplications=yes

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{cmd}"; Parameters: "/C sc stop KuikuiTelemetryService >nul 2>&1 & sc delete KuikuiTelemetryService >nul 2>&1 & exit /b 0"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "create KuikuiTelemetryService binPath= ""{app}\service\KuikuiTelemetryService.exe"" start= auto DisplayName= ""Kuikui Telemetry Service"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure KuikuiTelemetryService reset= 60 actions= restart/3000/restart/10000/none/0"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start KuikuiTelemetryService"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C sc stop KuikuiTelemetryService >nul 2>&1 & sc delete KuikuiTelemetryService >nul 2>&1 & exit /b 0"; Flags: runhidden waituntilterminated
