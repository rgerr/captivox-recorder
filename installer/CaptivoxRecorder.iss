; Installatieprogramma voor Captivox Recorder (gratis versie), voor Inno Setup 6.
; build.ps1 roept dit aan met /DAppVersion=<versie> /DSourceDir=<map met de gebouwde release>.
; Installeert per gebruiker, zonder beheerdersrechten, in %LOCALAPPDATA%\Programs\CaptivoxRecorderFree.

#ifndef AppVersion
  #error Geef de versie mee: /DAppVersion=1.0.0
#endif
#ifndef SourceDir
  #error Geef de map met de gebouwde release mee: /DSourceDir=...
#endif

#define AppName "Captivox Recorder"
#define AppDisplayName "Captivox Recorder (gratis versie)"
#define ShortcutName "Captivox Recorder (gratis)"
#define AppPublisher "rgerr"
#define AppUrl "https://github.com/rgerr/captivox-recorder"

[Setup]
; Eigen AppId, map en snelkoppeling: een andere Captivox-installatie op dezelfde pc blijft ongemoeid.
AppId={{D3F5A798-DC16-45B6-8E1A-ADEE295E0CD0}
AppName={#AppDisplayName}
AppVersion={#AppVersion}
AppVerName={#AppDisplayName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Copyright (c) 2026 rgerr. GPL-3.0-or-later.
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppDisplayName} installatie
DefaultDirName={autopf}\CaptivoxRecorderFree
DefaultGroupName={#ShortcutName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
LicenseFile={#SourceDir}\LICENSE
SetupIconFile=..\src\CaptivoxRecorder\Assets\captivox-recorder.ico
UninstallDisplayIcon={app}\CaptivoxRecorder.exe
UninstallDisplayName={#AppDisplayName}
WizardStyle=modern
; Alleen naar de taal vragen als Windows geen Nederlands of Engels is.
ShowLanguageDialog=auto
Compression=lzma2/ultra64
SolidCompression=yes
OutputBaseFilename=CaptivoxRecorder-{#AppVersion}-setup
; Een draaiende recorder eerst laten afsluiten (zelfde naam als in SingleInstanceGuard.cs).
AppMutex=Local\CaptivoxRecorderFree.SingleInstance
CloseApplications=yes

[Languages]
Name: "nl"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
nl.RuntimeTask=Transcriptie-omgeving nu installeren (eenmalig, ongeveer 1 GB downloaden, 10 tot 30 minuten)
en.RuntimeTask=Install the transcription runtime now (one-time, about 1 GB download, 10 to 30 minutes)
nl.RuntimeHint=Er opent een PowerShell-venster. Vraagt het om Python of FFmpeg te installeren, antwoord dan met Enter. Sluit het venster als het klaar is.
en.RuntimeHint=A PowerShell window opens. If it asks to install Python or FFmpeg, press Enter. Close the window when it is done.
nl.StartTask={#ShortcutName} starten
en.StartTask=Start {#ShortcutName}
nl.RemoveDataQuestion=Ook de transcriptie-omgeving, de instellingen en de logboeken van Captivox verwijderen (de map CaptivoxRecorderFree in je lokale AppData)?%n%nJe opnames en transcripten blijven altijd staan.
en.RemoveDataQuestion=Also remove the Captivox transcription runtime, settings and logs (the CaptivoxRecorderFree folder in your local AppData)?%n%nYour recordings and transcripts are always kept.
nl.RuntimeShortcut=Transcriptie-omgeving installeren of herstellen
en.RuntimeShortcut=Install or repair the transcription runtime

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\CaptivoxRecorder.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\install-prereqs.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\workers\*"; DestDir: "{app}\workers"; Flags: ignoreversion recursesubdirs
Source: "{#SourceDir}\tools\*"; DestDir: "{app}\tools"; Flags: ignoreversion recursesubdirs

[InstallDelete]
; Een oude vastgezette pakketlijst hoort bij de vorige versie; install-prereqs.ps1 maakt een nieuwe.
Type: files; Name: "{app}\workers\requirements.windows.realized.lock.txt"

[Icons]
Name: "{group}\{#ShortcutName}"; Filename: "{app}\CaptivoxRecorder.exe"
Name: "{group}\{cm:RuntimeShortcut}"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -NoExit -File ""{app}\install-prereqs.ps1"""; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#ShortcutName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#ShortcutName}"; Filename: "{app}\CaptivoxRecorder.exe"; Tasks: desktopicon

[Registry]
; De recorder zet zelf een opstartregel als je dat in Instellingen kiest; bij verwijderen gaat die mee weg.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "CaptivoxRecorderFree"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -NoExit -File ""{app}\install-prereqs.ps1"""; WorkingDir: "{app}"; Description: "{cm:RuntimeTask}"; StatusMsg: "{cm:RuntimeHint}"; Flags: postinstall nowait skipifsilent
Filename: "{app}\CaptivoxRecorder.exe"; Description: "{cm:StartTask}"; Flags: postinstall nowait skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\CaptivoxRecorderFree');
    if DirExists(DataDir) and not UninstallSilent then
    begin
      if MsgBox(CustomMessage('RemoveDataQuestion'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
