#ifndef AppVersion
  #define AppVersion "1.26.4"
#endif
#ifndef SourceDir
  #define SourceDir "..\MultronWinCleaner\bin\Release\net8.0-windows"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "Multron Win Cleaner"
#define AppExe "MultronWinCleaner.exe"
#define AppPublisher "Multron"
#define AppUrl "https://github.com/Multron-Community/MultronWcleaner"

[Setup]
AppId={{82C7E0F4-6EAE-434E-B06B-45D11A2ED2D0}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=mwc_setup
SetupIconFile=..\MultronWinCleaner\Assets\mwc_icon.ico
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{app}\Updater.exe"
Type: files; Name: "{app}\Updater.dll"
Type: files; Name: "{app}\Updater.deps.json"
Type: files; Name: "{app}\Updater.runtimeconfig.json"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Update"
Type: filesandordirs; Name: "{app}\Logs"
Type: files; Name: "{app}\*.mwcold"
Type: files; Name: "{app}\excluded.txt"
Type: files; Name: "{app}\settings.txt"
Type: files; Name: "{app}\selections.txt"
Type: files; Name: "{app}\database.txt"
Type: files; Name: "{app}\databaseversion.txt"
Type: dirifempty; Name: "{app}"
