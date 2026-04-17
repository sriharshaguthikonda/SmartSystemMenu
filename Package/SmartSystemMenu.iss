[Setup]
AppName=SmartSystemMenu
AppVersion=2.16.0
AppPublisher=AlexanderPro
AppPublisherURL=https://github.com/AlexanderPro/SmartSystemMenu
AppSupportURL=https://github.com/AlexanderPro/SmartSystemMenu/issues
AppUpdatesURL=https://github.com/AlexanderPro/SmartSystemMenu/releases
DefaultDirName={pf}\SmartSystemMenu
DefaultGroupName=SmartSystemMenu
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=.
OutputBaseFilename=SmartSystemMenu-Setup
SetupIconFile=..\SmartSystemMenu\Images\SmartSystemMenu.ico
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x86 x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; OnlyBelowVersion: 6.1

[Files]
Source: "..\Application\SmartSystemMenu.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Application\SmartSystemMenu64.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Application\SmartSystemMenuHook.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Application\SmartSystemMenuHook64.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Application\SmartSystemMenu.xml"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Application\Language.xml"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\SmartSystemMenu\Images\SmartSystemMenu.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\SmartSystemMenu"; Filename: "{app}\SmartSystemMenu.exe"; IconFilename: "{app}\SmartSystemMenu.ico"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,SmartSystemMenu}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\SmartSystemMenu"; Filename: "{app}\SmartSystemMenu.exe"; IconFilename: "{app}\SmartSystemMenu.ico"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\SmartSystemMenu"; Filename: "{app}\SmartSystemMenu.exe"; IconFilename: "{app}\SmartSystemMenu.ico"; WorkingDir: "{app}"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\SmartSystemMenu.exe"; Description: "{cm:LaunchProgram,SmartSystemMenu}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function GetUninstallString(): String;
var
  sUnInstPath: String;
  sUnInstallString: String;
begin
  sUnInstPath := ExpandConstant('Software\Microsoft\Windows\CurrentVersion\Uninstall\{#emit SetupSetting("AppId")}_is1');
  sUnInstallString := '';
  if not RegQueryStringValue(HKLM, sUnInstPath, 'UninstallString', sUnInstallString) then
    RegQueryStringValue(HKCU, sUnInstPath, 'UninstallString', sUnInstallString);
  Result := sUnInstallString;
end;

function IsUpgrade(): Boolean;
begin
  Result := (GetUninstallString() <> '');
end;

function UnInstallOldVersion(): Integer;
var
  sUnInstallString: String;
  iResultCode: Integer;
begin
  Result := 0;
  sUnInstallString := GetUninstallString();
  if sUnInstallString <> '' then begin
    sUnInstallString := RemoveQuotes(sUnInstallString);
    if Exec(sUnInstallString, '/SILENT /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, iResultCode) then
      Result := 3
    else
      Result := 2;
  end else
    Result := 1;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep=ssInstall) then
  begin
    if (IsUpgrade()) then
    begin
      UnInstallOldVersion();
    end;
  end;
end;
