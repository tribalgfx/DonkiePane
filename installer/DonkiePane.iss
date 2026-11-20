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
AppCopyright=Copyright (C) 2026 tribalgfx. GPL-3.0-or-later.
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
; Niet twee installers tegelijk (bijv. een zelf gestarte en een via Bijwerken).
SetupMutex=DonkiePane.Setup
; Nederlands of Engels, net als de taal van Windows; alleen bij twijfel vraagt de installer het.
ShowLanguageDialog=auto

[InstallDelete]
; Plug-ins en foutopsporingsbestanden van een vorige versie: een oude plug-in-DLL zou anders blijven staan en geladen worden.
Type: filesandordirs; Name: "{app}\plugins"
Type: files; Name: "{app}\*.pdb"

[UninstallDelete]
; Wat de update-functie heeft gedownload.
Type: filesandordirs; Name: "{%TEMP}\DonkiePane-update"

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "nl"; MessagesFile: "compiler:Languages\Dutch.isl"

[CustomMessages]
en.StartNow=Start DonkiePane now
nl.StartNow=DonkiePane nu starten
en.StillRunning=DonkiePane is still running and won't stop. Close it with the icon next to the clock (Exit) and try again.
nl.StillRunning=DonkiePane draait nog en wil niet stoppen. Sluit het af via het icoon bij de klok (Afsluiten) en probeer het opnieuw.
en.StillRunningUninstall=DonkiePane is still running and won't stop. Close it with the icon next to the clock (Exit). Continuing may leave the Windows desktop icons hidden.%n%nContinue anyway?
nl.StillRunningUninstall=DonkiePane draait nog en wil niet stoppen. Sluit het af via het icoon bij de klok (Afsluiten). Doorgaan kan de Windows-bureaubladiconen verborgen laten.%n%nToch doorgaan?
en.DeleteData=Do you also want to delete your panes, tasks and settings?%nThat is the data in %1. Your desktop files always stay.
nl.DeleteData=Wil je ook je panes, taken en instellingen verwijderen?%nDat zijn de gegevens in %1. Je bureaubladbestanden blijven altijd staan.

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
; De GPL vraagt de licentietekst mee te geven met het programma.
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
; Bewust geen snelkoppeling op het bureaublad: die zou als item in je Inbox-pane terechtkomen.
Name: "{autoprograms}\DonkiePane"; Filename: "{app}\DonkiePane.exe"

[Run]
Filename: "{app}\DonkiePane.exe"; Description: "{cm:StartNow}"; Flags: nowait postinstall skipifsilent
; Bijwerken vanuit DonkiePane zelf start de installer met /SILENT /STARTAFTER=1: daarna DonkiePane weer starten.
Filename: "{app}\DonkiePane.exe"; Flags: nowait; Check: StartAfterUpdate

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  ApprovedKey = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run';
  SingleInstanceMutex = 'Local\DonkiePane.SingleInstance';

function FindWindowEx(Parent, ChildAfter: HWND; ClassName: String; WindowName: Longint): HWND;
  external 'FindWindowExW@user32.dll stdcall';
function IsWindowVisible(Wnd: HWND): Boolean;
  external 'IsWindowVisible@user32.dll stdcall';

{ Het venster met de iconenweergave van het bureaublad: meestal onder Progman, soms onder een WorkerW. }
function FindDesktopDefView: HWND;
var
  Host: HWND;
begin
  Result := FindWindowEx(FindWindowByClassName('Progman'), 0, 'SHELLDLL_DefView', 0);
  Host := 0;
  while Result = 0 do
  begin
    Host := FindWindowEx(0, Host, 'WorkerW', 0);
    if Host = 0 then exit;
    Result := FindWindowEx(Host, 0, 'SHELLDLL_DefView', 0);
  end;
end;

{ Zet de Windows-bureaubladiconen terug als ze verborgen zijn. Ook als DonkiePane niet draait: na een harde stop
  of crash staan ze nog uit, en zonder dit blijft het bureaublad na het verwijderen leeg. Dezelfde opdracht als
  rechtsklik -> Beeld -> Bureaubladpictogrammen weergeven; zonder iconenweergave doen we niets. }
procedure RestoreDesktopIcons;
var
  DefView, List: HWND;
begin
  DefView := FindDesktopDefView;
  if DefView = 0 then exit;
  List := FindWindowEx(DefView, 0, 'SysListView32', 0);
  if (List <> 0) and not IsWindowVisible(List) then
    SendMessage(DefView, $0111 { WM_COMMAND }, $7402, 0);
end;

function StartAfterUpdate: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:STARTAFTER|0}') = '1');
end;

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
    Result := CustomMessage('StillRunning');
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  if not QuitRunningDonkiePane then
    Result := SuppressibleMsgBox(CustomMessage('StillRunningUninstall'), mbConfirmation, MB_YESNO, IDNO) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usPostUninstall then exit;

  RestoreDesktopIcons;

  { "Starten met Windows" weghalen. }
  RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'DonkiePane');
  RegDeleteValue(HKEY_CURRENT_USER, ApprovedKey, 'DonkiePane');

  { Panes, taken en instellingen blijven standaard staan (ook bij een stille verwijdering). }
  if SuppressibleMsgBox(FmtMessage(CustomMessage('DeleteData'), [ExpandConstant('{userappdata}\DonkiePane')]),
      mbConfirmation, MB_YESNO, IDNO) = IDYES then
    DelTree(ExpandConstant('{userappdata}\DonkiePane'), True, True, True);
end;
