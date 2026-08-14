#ifndef MyAppVersion
  #define MyAppVersion "1.8.0"
#endif

#define MyAppName "CryptoSigTool"
#define MyAppPublisher "ydadev"
#define MyAppExeName "CryptoSigTool.exe"

[Setup]
AppId={{A6B673CC-DB99-49C0-B3B5-F9929536E141}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
SetupIconFile=..\CryptoSigTool\Assets\CryptoSigTool.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
InfoBeforeFile=..\DISCLAIMER-RU.txt

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе для всех пользователей"; GroupDescription: "Дополнительные значки:"; Flags: checkedonce

[Files]
Source: "Bundle\CryptoSigTool.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Bundle\INSTRUCTIONS-RU.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "Bundle\DISCLAIMER-RU.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "Bundle\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "Bundle\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{commonprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser
