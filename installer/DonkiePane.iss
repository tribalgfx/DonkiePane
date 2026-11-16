; Inno Setup-script voor DonkiePane. Bouwen met scripts\build-installer.ps1 (dat publiceert eerst het programma
; naar artifacts\publish en geeft AppVersion en SourceDir mee).
;
; Per gebruiker, geen beheerdersrechten: DonkiePane komt in %LOCALAPPDATA%\Programs\DonkiePane, net als
; scripts\install.ps1. Je panes, taken en instellingen (in %APPDATA%\DonkiePane) blijven bij een update staan.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

[Setup]
AppId={{CC5D6995-47D1-4015-B1E6-D25463DEB7F2}
AppName=DonkiePane
AppVersion={#AppVersion}
AppPublisher=DonkiePane
DefaultDirName={localappdata}\Programs\DonkiePane
DefaultGroupName=DonkiePane
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=DonkiePane-Setup-{#AppVersion}
SetupIconFile=..\src\DonkiePane.App\Assets\DonkiePane.ico
UninstallDisplayIcon={app}\DonkiePane.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; DonkiePane heeft geen hoofdvenster; we sluiten het zelf netjes af (zie [Code]).
CloseApplications=no

[Languages]
Name: "nl"; MessagesFile: "compiler:Languages\Dutch.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
; Bewust geen snelkoppeling op het bureaublad: die zou als item in je Inbox-pane terechtkomen.
Name: "{autoprograms}\DonkiePane"; Filename: "{app}\DonkiePane.exe"

[Run]
Filename: "{app}\DonkiePane.exe"; Description: "DonkiePane nu starten"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  SingleInstanceMutex = 'Local\DonkiePane.SingleInstance';

function IsRunning: Boolean;
begin
  Result := CheckForMutexes(SingleInstanceMutex);
end;

{ Vraagt een draaiende DonkiePane (uit deze map) zich netjes af te sluiten. Dan zet die de Windows-bureaubladiconen
  terug als hij ze verborgen had; een harde stop zou ze verborgen laten. Geeft False als hij niet stopt. }
function QuitRunningDonkiePane: Boolean;
var
  Exe: String;
  ResultCode, I: Integer;
begin
  Result := True;
  Exe := ExpandConstant('{app}\DonkiePane.exe');
  if not FileExists(Exe) then exit; { nog niets geinstalleerd in deze map }
  if not IsRunning then exit;

  Exec(Exe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  for I := 1 to 40 do
  begin
    if not IsRunning then break;
    Sleep(250);
  end;
  Result := not IsRunning;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not QuitRunningDonkiePane then
    Result := 'DonkiePane draait nog en wil niet stoppen. Sluit het af via het icoon bij de klok (Afsluiten) en probeer het opnieuw.';
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  if not QuitRunningDonkiePane then
    Result := SuppressibleMsgBox('DonkiePane draait nog en wil niet stoppen. Sluit het af via het icoon bij de klok (Afsluiten). ' +
      'Doorgaan kan de Windows-bureaubladiconen verborgen laten.' + #13#10#13#10 + 'Toch doorgaan?',
      mbConfirmation, MB_YESNO, IDNO) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usPostUninstall then exit;

  { "Starten met Windows" weghalen. }
  RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'DonkiePane');

  { Panes, taken en instellingen blijven standaard staan (ook bij een stille verwijdering). }
  if SuppressibleMsgBox('Wil je ook je panes, taken en instellingen verwijderen?' + #13#10 +
      'Dat zijn de gegevens in ' + ExpandConstant('{userappdata}\DonkiePane') + '. Je bureaubladbestanden blijven altijd staan.',
      mbConfirmation, MB_YESNO, IDNO) = IDYES then
    DelTree(ExpandConstant('{userappdata}\DonkiePane'), True, True, True);
end;
