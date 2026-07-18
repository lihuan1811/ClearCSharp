#define MyAppName "C DiskGlow"
#define MyAppVersion "1.0"
#ifndef AppSourceX64
  #error AppSourceX64 must be provided by the build workflow
#endif
#ifndef AppSourceX86
  #error AppSourceX86 must be provided by the build workflow
#endif
#ifndef FxSource
  #error FxSource must be provided by the build workflow
#endif

[Setup]
AppId={{CBFD4A3E-D154-4C71-973D-B54E62B02E15}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\C DiskGlow
DefaultGroupName=C DiskGlow
OutputBaseFilename=C-DiskGlow-Windows7-SP1-x86-x64-setup
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=6.1sp1
SetupIconFile=..\ZyperWin++\zw+.ico
UninstallDisplayIcon={app}\C DiskGlow.exe
WizardStyle=modern
RestartIfNeededByRun=yes

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "{#AppSourceX64}"; DestDir: "{app}"; DestName: "C DiskGlow.exe"; Flags: ignoreversion; Check: IsWin64
Source: "{#AppSourceX86}"; DestDir: "{app}"; DestName: "C DiskGlow.exe"; Flags: ignoreversion; Check: not IsWin64
Source: "{#FxSource}"; DestDir: "{tmp}"; DestName: "ndp48-x86-x64-allos-enu.exe"; Flags: deleteafterinstall

[Icons]
Name: "{group}\C DiskGlow"; Filename: "{app}\C DiskGlow.exe"
Name: "{autodesktop}\C DiskGlow"; Filename: "{app}\C DiskGlow.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Run]
Filename: "{tmp}\ndp48-x86-x64-allos-enu.exe"; Parameters: "/q /norestart"; StatusMsg: "正在安装程序所需的 .NET Framework 4.8..."; Check: NeedsNetFx48; Flags: waituntilterminated
Filename: "{app}\C DiskGlow.exe"; Description: "运行 C DiskGlow"; Flags: nowait postinstall skipifsilent

[Code]
function NetFx48Installed: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32,
    'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
    'Release', Release) and (Release >= 528040);
  if (not Result) and IsWin64 then
    Result := RegQueryDWordValue(HKLM64,
      'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
      'Release', Release) and (Release >= 528040);
end;

function NeedsNetFx48: Boolean;
begin
  Result := not NetFx48Installed;
end;
