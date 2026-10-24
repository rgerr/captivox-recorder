; Installatieprogramma voor Captivox Recorder (gratis versie), voor Inno Setup 6.
; build.ps1 roept dit aan met /DAppVersion=<versie> /DSourceDir=<map met de gebouwde release>.
; Installeert per gebruiker, zonder beheerdersrechten, in %LOCALAPPDATA%\Programs\CaptivoxRecorderFree.
; Na het kopieren van de bestanden richt het onzichtbaar de transcriptie-omgeving in (install-prereqs.ps1 -Unattended):
; eigen Python, eigen FFmpeg, de pakketten en de spraakmodellen. Opnieuw starten van deze setup = herstellen.

#ifndef AppVersion
  #error Geef de versie mee: /DAppVersion=1.0.0
#endif
#ifndef SourceDir
  #error Geef de map met de gebouwde release mee: /DSourceDir=...
#endif

#define AppName "Captivox Recorder"
#define AppDisplayName "Captivox Recorder Free"
#define ShortcutName "Captivox Recorder Free"
#define AppPublisher "rgerr"
#define AppUrl "https://github.com/rgerr/captivox-recorder"
#define AppExe "CaptivoxRecorder.exe"
#define DataFolder "CaptivoxRecorderFree"

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
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppDisplayName}
WizardStyle=modern
; Het logo van Captivox in plaats van het standaardplaatje (PNG, per schermschaal een maat).
WizardImageFile=wizard-large-164.png,wizard-large-205.png,wizard-large-246.png,wizard-large-328.png
WizardSmallImageFile=wizard-small-55.png,wizard-small-69.png,wizard-small-83.png,wizard-small-110.png
; Na het kopieren komen de transcriptie-omgeving en de spraakmodellen (ongeveer 10 GB); zo toont de mapkeuze de echte ruimte.
ExtraDiskSpaceRequired=10737418240
; Alleen naar de taal vragen als Windows geen Engels, Nederlands, Duits of Frans is.
ShowLanguageDialog=auto
Compression=lzma2/ultra64
SolidCompression=yes
OutputBaseFilename=CaptivoxRecorder-{#AppVersion}-setup
CloseApplications=yes
; Een draaiende Captivox sluit Windows zelf netjes af (alleen de app uit deze programmamap); daarna start hij via het vinkje aan het eind.
RestartApplications=no

[Languages]
; Engels eerst: dat is de standaard als de taal van Windows er niet bij staat.
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "nl"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
en.StartTask=Start {#ShortcutName}
nl.StartTask={#ShortcutName} starten
de.StartTask={#ShortcutName} starten
fr.StartTask=Démarrer {#ShortcutName}
en.RemoveDataQuestion=Also remove the Captivox transcription runtime, settings and logs (the {#DataFolder} folder in your local AppData)?%n%nYour recordings and transcripts are always kept. The speech models are in the shared .cache\huggingface folder in your user folder and are kept as well.
nl.RemoveDataQuestion=Ook de transcriptie-omgeving, de instellingen en de logboeken van Captivox verwijderen (de map {#DataFolder} in je lokale AppData)?%n%nJe opnames en transcripten blijven altijd staan. De spraakmodellen staan in de gedeelde map .cache\huggingface in je gebruikersmap en blijven ook staan.
de.RemoveDataQuestion=Auch die Transkriptionsumgebung, die Einstellungen und die Protokolle von Captivox entfernen (den Ordner {#DataFolder} in Ihrem lokalen AppData)?%n%nIhre Aufnahmen und Transkripte bleiben immer erhalten. Die Sprachmodelle liegen im gemeinsamen Ordner .cache\huggingface in Ihrem Benutzerordner und bleiben ebenfalls erhalten.
fr.RemoveDataQuestion=Supprimer aussi l'environnement de transcription, les paramètres et les journaux de Captivox (le dossier {#DataFolder} dans votre AppData local) ?%n%nVos enregistrements et transcriptions sont toujours conservés. Les modèles vocaux se trouvent dans le dossier partagé .cache\huggingface de votre dossier utilisateur et sont également conservés.
en.RuntimePageCaption=Installing the transcription runtime
nl.RuntimePageCaption=Transcriptie-omgeving installeren
de.RuntimePageCaption=Transkriptionsumgebung wird installiert
fr.RuntimePageCaption=Installation de l'environnement de transcription
en.RuntimePageDescription=Downloading Python, FFmpeg and the speech models: about 5 GB, 10 to 30 minutes.
nl.RuntimePageDescription=Python, FFmpeg en de spraakmodellen ophalen: ongeveer 5 GB, 10 tot 30 minuten.
de.RuntimePageDescription=Python, FFmpeg und die Sprachmodelle werden heruntergeladen: etwa 5 GB, 10 bis 30 Minuten.
fr.RuntimePageDescription=Téléchargement de Python, FFmpeg et des modèles vocaux : environ 5 Go, 10 à 30 minutes.
en.ReadyWhatHappens=What happens next:
nl.ReadyWhatHappens=Wat er daarna gebeurt:
de.ReadyWhatHappens=Was danach passiert:
fr.ReadyWhatHappens=Ce qui se passe ensuite :
en.ReadyDownloads=Captivox downloads Python, FFmpeg and the speech models itself: about 5 GB (about 9 GB with an NVIDIA graphics card).
nl.ReadyDownloads=Captivox haalt zelf Python, FFmpeg en de spraakmodellen op: ongeveer 5 GB (met een NVIDIA-videokaart ongeveer 9 GB).
de.ReadyDownloads=Captivox lädt Python, FFmpeg und die Sprachmodelle selbst herunter: etwa 5 GB (mit einer NVIDIA-Grafikkarte etwa 9 GB).
fr.ReadyDownloads=Captivox télécharge lui-même Python, FFmpeg et les modèles vocaux : environ 5 Go (environ 9 Go avec une carte graphique NVIDIA).
en.ReadyDuration=This takes 10 to 30 minutes; a progress bar shows how far it is. You can keep working in the meantime.
nl.ReadyDuration=Dat duurt 10 tot 30 minuten; een balk toont de voortgang. Je kunt intussen doorwerken.
de.ReadyDuration=Das dauert 10 bis 30 Minuten; ein Balken zeigt den Fortschritt. Sie können währenddessen weiterarbeiten.
fr.ReadyDuration=Cela prend 10 à 30 minutes ; une barre indique la progression. Vous pouvez continuer à travailler pendant ce temps.
en.ReadyNoAdmin=No administrator rights are needed: everything goes into your own user folder.
nl.ReadyNoAdmin=Er zijn geen beheerdersrechten nodig: alles komt in je eigen gebruikersmap.
de.ReadyNoAdmin=Es sind keine Administratorrechte nötig: Alles kommt in Ihren eigenen Benutzerordner.
fr.ReadyNoAdmin=Aucun droit d'administrateur n'est nécessaire : tout est placé dans votre propre dossier utilisateur.
en.RuntimeStarting=Starting...
nl.RuntimeStarting=Starten...
de.RuntimeStarting=Wird gestartet...
fr.RuntimeStarting=Démarrage...
en.RuntimeElapsed=Running: %1 min. You can keep working in the meantime.
nl.RuntimeElapsed=Bezig: %1 min. Je kunt intussen gewoon doorwerken.
de.RuntimeElapsed=Läuft seit %1 Min. Sie können währenddessen einfach weiterarbeiten.
fr.RuntimeElapsed=En cours depuis %1 min. Vous pouvez continuer à travailler pendant ce temps.
en.RuntimeStopButton=Stop
nl.RuntimeStopButton=Stoppen
de.RuntimeStopButton=Anhalten
fr.RuntimeStopButton=Arrêter
en.RuntimeStopConfirm=Stop installing the transcription runtime?%n%nCaptivox stays installed. Recording works, but transcription only after you run this setup again.
nl.RuntimeStopConfirm=Het installeren van de transcriptie-omgeving stoppen?%n%nCaptivox blijft geinstalleerd. Opnemen werkt, maar uitschrijven pas nadat je deze installatie (setup) opnieuw hebt gestart.
de.RuntimeStopConfirm=Die Installation der Transkriptionsumgebung anhalten?%n%nCaptivox bleibt installiert. Aufnehmen funktioniert, Transkribieren aber erst, nachdem Sie dieses Setup erneut gestartet haben.
fr.RuntimeStopConfirm=Arrêter l'installation de l'environnement de transcription ?%n%nCaptivox reste installé. L'enregistrement fonctionne, mais la transcription seulement après avoir relancé ce programme d'installation.
en.RuntimeCancelled=Installing the transcription runtime was stopped. Captivox itself is installed.%n%nRun this setup again later to finish the transcription runtime. Whatever is already done will not be downloaded again.
nl.RuntimeCancelled=Het installeren van de transcriptie-omgeving is gestopt. Captivox zelf is geinstalleerd.%n%nStart deze installatie (setup) later opnieuw om de transcriptie-omgeving af te maken. Wat al klaar is, wordt dan niet opnieuw gedownload.
de.RuntimeCancelled=Die Installation der Transkriptionsumgebung wurde angehalten. Captivox selbst ist installiert.%n%nStarten Sie dieses Setup später erneut, um die Transkriptionsumgebung fertigzustellen. Was bereits fertig ist, wird dann nicht erneut heruntergeladen.
fr.RuntimeCancelled=L'installation de l'environnement de transcription a été arrêtée. Captivox lui-même est installé.%n%nRelancez ce programme d'installation plus tard pour terminer l'environnement de transcription. Ce qui est déjà prêt ne sera pas retéléchargé.
en.RuntimeFailed=The transcription runtime could not be completed:%n%n%1%n%nCaptivox itself is installed. Run this setup again to retry.%n%nLog: %2
nl.RuntimeFailed=De transcriptie-omgeving kon niet worden voltooid:%n%n%1%n%nCaptivox zelf is geinstalleerd. Start deze installatie (setup) opnieuw om het nog eens te proberen.%n%nLogboek: %2
de.RuntimeFailed=Die Transkriptionsumgebung konnte nicht fertiggestellt werden:%n%n%1%n%nCaptivox selbst ist installiert. Starten Sie dieses Setup erneut, um es noch einmal zu versuchen.%n%nProtokoll: %2
fr.RuntimeFailed=L'environnement de transcription n'a pas pu être terminé :%n%n%1%n%nCaptivox lui-même est installé. Relancez ce programme d'installation pour réessayer.%n%nJournal : %2
en.RuntimeNotStarted=The installation of the transcription runtime could not be started.
nl.RuntimeNotStarted=De installatie van de transcriptie-omgeving kon niet worden gestart.
de.RuntimeNotStarted=Die Installation der Transkriptionsumgebung konnte nicht gestartet werden.
fr.RuntimeNotStarted=L'installation de l'environnement de transcription n'a pas pu démarrer.
en.RuntimeStoppedUnexpectedly=The installation of the transcription runtime stopped unexpectedly.
nl.RuntimeStoppedUnexpectedly=De installatie van de transcriptie-omgeving is onverwacht gestopt.
de.RuntimeStoppedUnexpectedly=Die Installation der Transkriptionsumgebung wurde unerwartet beendet.
fr.RuntimeStoppedUnexpectedly=L'installation de l'environnement de transcription s'est arrêtée de manière inattendue.
en.DiskSpaceWarning=The drive of your user folder has %1 GB free. Captivox needs about 10 GB for the transcription runtime and the speech models (about 15 GB with an NVIDIA graphics card).%n%nFree up space first, or continue anyway (the installation may then stop halfway). Continue anyway?
nl.DiskSpaceWarning=Op de schijf van je gebruikersmap is %1 GB vrij. Captivox heeft ongeveer 10 GB nodig voor de transcriptie-omgeving en de spraakmodellen (met een NVIDIA-videokaart ongeveer 15 GB).%n%nMaak eerst ruimte vrij, of ga toch door (de installatie kan dan halverwege stoppen). Toch doorgaan?
de.DiskSpaceWarning=Auf dem Laufwerk Ihres Benutzerordners sind %1 GB frei. Captivox benötigt etwa 10 GB für die Transkriptionsumgebung und die Sprachmodelle (mit einer NVIDIA-Grafikkarte etwa 15 GB).%n%nSchaffen Sie zuerst Platz oder fahren Sie trotzdem fort (die Installation kann dann mittendrin abbrechen). Trotzdem fortfahren?
fr.DiskSpaceWarning=Le lecteur de votre dossier utilisateur dispose de %1 Go libres. Captivox a besoin d'environ 10 Go pour l'environnement de transcription et les modèles vocaux (environ 15 Go avec une carte graphique NVIDIA).%n%nLibérez d'abord de l'espace ou continuez quand même (l'installation peut alors s'arrêter en cours de route). Continuer quand même ?

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\install-prereqs.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\workers\*"; DestDir: "{app}\workers"; Flags: ignoreversion recursesubdirs
Source: "{#SourceDir}\tools\*"; DestDir: "{app}\tools"; Flags: ignoreversion recursesubdirs

[InstallDelete]
; Een oude vastgezette pakketlijst hoort bij de vorige versie; install-prereqs.ps1 maakt een nieuwe.
Type: files; Name: "{app}\workers\requirements.windows.realized.lock.txt"
; De oude snelkoppeling voor het herstellen van de transcriptie-omgeving; herstellen = deze setup opnieuw starten.
Type: files; Name: "{group}\Transcriptie-omgeving installeren of herstellen.lnk"
Type: files; Name: "{group}\Install or repair the transcription runtime.lnk"

[UninstallDelete]
; Python legt naast de worker tussenbestanden aan; die gaan mee weg met het programma.
Type: filesandordirs; Name: "{app}\workers\__pycache__"
Type: filesandordirs; Name: "{app}\workers\_WorkerLogs"
Type: files; Name: "{app}\workers\requirements.windows.realized.lock.txt"

[Icons]
Name: "{group}\{#ShortcutName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#ShortcutName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#ShortcutName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; De recorder zet zelf een opstartregel als je dat in Instellingen kiest; bij verwijderen gaat die mee weg.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "CaptivoxRecorderFree"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:StartTask}"; Flags: postinstall nowait skipifsilent

[Code]
const
  SYNCHRONIZE = $00100000;
  PROCESS_QUERY_LIMITED_INFORMATION = $1000;
  WAIT_OBJECT_0 = 0;
  // Eigen exitcodes van setup (alleen als de rest van de installatie wel slaagde).
  RuntimeExitFailed = 20;
  RuntimeExitCancelled = 21;
  NeededFreeGB = 10;

var
  RuntimePage: TOutputProgressWizardPage;
  RuntimeStopButton: TNewButton;
  RuntimeStopRequested: Boolean;
  RuntimeExitCode: Integer;

function OpenProcess(dwDesiredAccess: Cardinal; bInheritHandle: Boolean; dwProcessId: Cardinal): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(hHandle: THandle; dwMilliseconds: Cardinal): Cardinal;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function GetTickCount: DWord;
  external 'GetTickCount@kernel32.dll stdcall';

procedure RuntimeStopButtonClick(Sender: TObject);
begin
  if MsgBox(CustomMessage('RuntimeStopConfirm'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    RuntimeStopRequested := True;
end;

procedure InitializeWizard;
begin
  RuntimePage := CreateOutputProgressPage(CustomMessage('RuntimePageCaption'), CustomMessage('RuntimePageDescription'));

  // Eigen stopknop op de plek van Annuleren: na het kopieren van de bestanden kan Inno niet meer annuleren.
  RuntimeStopButton := TNewButton.Create(WizardForm);
  RuntimeStopButton.Parent := WizardForm;
  RuntimeStopButton.Left := WizardForm.CancelButton.Left;
  RuntimeStopButton.Top := WizardForm.CancelButton.Top;
  RuntimeStopButton.Width := WizardForm.CancelButton.Width;
  RuntimeStopButton.Height := WizardForm.CancelButton.Height;
  RuntimeStopButton.Anchors := WizardForm.CancelButton.Anchors;
  RuntimeStopButton.Caption := CustomMessage('RuntimeStopButton');
  RuntimeStopButton.OnClick := @RuntimeStopButtonClick;
  RuntimeStopButton.Visible := False;
end;

function GetFreeGigabytes(const Path: String): Integer;
var
  FreeBytes, TotalBytes: Int64;
begin
  Result := -1;
  if GetSpaceOnDisk64(Path, FreeBytes, TotalBytes) then
    Result := FreeBytes div (1024 * 1024 * 1024);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  FreeGB: Integer;
begin
  Result := True;
  if CurPageID = wpReady then
  begin
    FreeGB := GetFreeGigabytes(ExpandConstant('{localappdata}'));
    Log(Format('Vrije ruimte op de schijf van %s: %d GB', [ExpandConstant('{localappdata}'), FreeGB]));
    if (FreeGB >= 0) and (FreeGB < NeededFreeGB) and not WizardSilent then
      Result := MsgBox(FmtMessage(CustomMessage('DiskSpaceWarning'), [IntToStr(FreeGB)]), mbError, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  end;
end;

// De pagina Klaar om te installeren: naast de map en de taken ook wat er na het kopieren gebeurt.
// Breekt een regel af op ongeveer MaxLen tekens (op spaties), voor het vaste vak van het overzichtsscherm.
function WrapText(const Text, Indent: String; MaxLen: Integer): String;
var
  Rest, Line: String;
  Cut: Integer;
begin
  Result := '';
  Rest := Text;
  while Length(Rest) > MaxLen do
  begin
    Cut := MaxLen;
    while (Cut > 1) and (Rest[Cut] <> ' ') do
      Cut := Cut - 1;
    if Cut <= 1 then
      Cut := MaxLen;
    Line := Copy(Rest, 1, Cut - 1);
    Result := Result + Indent + Line + #13#10;
    Rest := Copy(Rest, Cut + 1, Length(Rest));
  end;
  Result := Result + Indent + Rest;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := '';
  if MemoDirInfo <> '' then Result := Result + MemoDirInfo + NewLine + NewLine;
  if MemoGroupInfo <> '' then Result := Result + MemoGroupInfo + NewLine + NewLine;
  if MemoTasksInfo <> '' then Result := Result + MemoTasksInfo + NewLine + NewLine;
  Result := Result + CustomMessage('ReadyWhatHappens') + NewLine
    + WrapText(CustomMessage('ReadyDownloads'), Space, 70) + NewLine
    + WrapText(CustomMessage('ReadyDuration'), Space, 70) + NewLine
    + WrapText(CustomMessage('ReadyNoAdmin'), Space, 70);
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  // Het venster sluiten tijdens de transcriptie-omgeving werkt als de knop Stoppen.
  if (RuntimePage <> nil) and (CurPageID = RuntimePage.ID) then
  begin
    Cancel := False;
    Confirm := False;
    RuntimeStopButtonClick(nil);
  end;
end;

// Haalt het eerste veld (tot '|') van S af en geeft het terug.
function TakeField(var S: String): String;
var
  P: Integer;
begin
  P := Pos('|', S);
  if P = 0 then
  begin
    Result := S;
    S := '';
  end
  else
  begin
    Result := Copy(S, 1, P - 1);
    Delete(S, 1, P);
  end;
end;

function ReadTextFile(const FileName: String; var Text: String): Boolean;
var
  Raw: AnsiString;
begin
  Result := False;
  Text := '';
  if FileExists(FileName) and LoadStringFromFile(FileName, Raw) then
  begin
    Text := Trim(String(Raw));
    Result := Text <> '';
  end;
end;

// install-prereqs.ps1 meldt zijn voortgang in het Nederlands en het Engels; Duits en Frans krijgen de Engelse tekst.
function PickLanguage(const Dutch, English: String): String;
begin
  if (ActiveLanguage = 'nl') or (English = '') then
    Result := Dutch
  else
    Result := English;
end;

// Legt bij een nieuwe installatie de gekozen taal vast; Captivox neemt die bij de eerste start over als taal van de
// interface (SettingsStore.InstallLanguageFileName). Een bestaande installatie houdt haar eigen instelling.
procedure WriteInstallLanguage;
var
  SettingsDir, SettingsFile: String;
begin
  SettingsDir := ExpandConstant('{localappdata}\{#DataFolder}\settings');
  SettingsFile := SettingsDir + '\application_settings.json';
  if FileExists(SettingsFile) then
  begin
    Log('Taal van de installatie niet vastgelegd: er zijn al instellingen.');
    Exit;
  end;
  if ForceDirectories(SettingsDir) and SaveStringToFile(SettingsDir + '\install-language.txt', ActiveLanguage, False) then
    Log('Taal van de installatie vastgelegd: ' + ActiveLanguage)
  else
    Log('Taal van de installatie kon niet worden vastgelegd.');
end;

function ElapsedText(StartTick: DWord): String;
begin
  Result := FmtMessage(CustomMessage('RuntimeElapsed'), [IntToStr((GetTickCount - StartTick) div 60000)]);
end;

procedure StopProcessTree(Pid: Cardinal);
var
  ResultCode: Integer;
begin
  if Pid = 0 then
    Exit;
  Log(Format('Transcriptie-omgeving: proces %d en onderliggende processen stoppen.', [Pid]));
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/PID ' + IntToStr(Pid) + ' /T /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure ReportRuntimeFailure(const Reason, LogPath: String; ShowUi: Boolean);
begin
  RuntimeExitCode := RuntimeExitFailed;
  Log('Transcriptie-omgeving mislukt: ' + Reason + ' Logboek: ' + LogPath);
  if ShowUi then
    MsgBox(FmtMessage(CustomMessage('RuntimeFailed'), [Reason, LogPath]), mbError, MB_OK);
end;

// Start install-prereqs.ps1 onzichtbaar en volgt het statusbestand tot de regel 'done|...'.
// In /SILENT en /VERYSILENT zonder scherm en zonder vragen; het resultaat gaat naar het setup-logboek en de exitcode.
procedure RunRuntimeSetup;
var
  StatusFile, PidFile, Params, Line, Rest, Field, DutchText, EnglishText, LogPath, DefaultLogPath, PidText: String;
  ResultCode, Percent, FinalCode, I: Integer;
  Pid: Cardinal;
  ProcessHandle: THandle;
  StartTick, EndedTick: DWord;
  Finished, ProcessEnded, ShowUi: Boolean;
begin
  RuntimeExitCode := 0;
  RuntimeStopRequested := False;
  ShowUi := not WizardSilent;
  StatusFile := ExpandConstant('{tmp}\captivox-runtime-status.txt');
  PidFile := StatusFile + '.pid';
  DefaultLogPath := ExpandConstant('{localappdata}\{#DataFolder}\logs\install');
  LogPath := DefaultLogPath;
  DeleteFile(StatusFile);
  DeleteFile(PidFile);
  Params := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + ExpandConstant('{app}\install-prereqs.ps1') + '" -Unattended -StatusFile "' + StatusFile + '"';
  Log('Transcriptie-omgeving starten: powershell.exe ' + Params);

  Pid := 0;
  ProcessHandle := 0;
  Percent := 0;
  FinalCode := -1;
  DutchText := '';
  EnglishText := '';
  if ShowUi then
  begin
    RuntimePage.SetText(CustomMessage('RuntimeStarting'), '');
    RuntimePage.SetProgress(0, 100);
    RuntimePage.Show;
    WizardForm.CancelButton.Visible := False;
    RuntimeStopButton.Visible := True;
  end;
  try
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Params, ExpandConstant('{app}'), SW_HIDE, ewNoWait, ResultCode) then
    begin
      ReportRuntimeFailure(CustomMessage('RuntimeNotStarted') + ' ' + SysErrorMessage(ResultCode), LogPath, ShowUi);
      Exit;
    end;

    StartTick := GetTickCount;
    EndedTick := 0;
    Finished := False;
    ProcessEnded := False;
    while not Finished do
    begin
      if ProcessHandle = 0 then
      begin
        if ReadTextFile(PidFile, PidText) then
        begin
          Pid := StrToIntDef(PidText, 0);
          if Pid <> 0 then
            ProcessHandle := OpenProcess(SYNCHRONIZE or PROCESS_QUERY_LIMITED_INFORMATION, False, Pid);
        end;
      end;

      if ReadTextFile(StatusFile, Line) then
      begin
        Rest := Line;
        Field := TakeField(Rest);
        if Field = 'done' then
        begin
          FinalCode := StrToIntDef(TakeField(Rest), 1);
          DutchText := TakeField(Rest);
          EnglishText := TakeField(Rest);
          if Rest <> '' then
            LogPath := Rest;
          Finished := True;
        end
        else
        begin
          Percent := StrToIntDef(Field, Percent);
          DutchText := TakeField(Rest);
          EnglishText := Rest;
          if ShowUi then
            RuntimePage.SetText(PickLanguage(DutchText, EnglishText), ElapsedText(StartTick));
        end;
      end;

      if not Finished then
      begin
        if RuntimeStopRequested then
        begin
          StopProcessTree(Pid);
          RuntimeExitCode := RuntimeExitCancelled;
          Log('Transcriptie-omgeving: gestopt door de gebruiker.');
          if ShowUi then
            MsgBox(CustomMessage('RuntimeCancelled'), mbInformation, MB_OK);
          Exit;
        end;

        if (ProcessHandle <> 0) and not ProcessEnded and (WaitForSingleObject(ProcessHandle, 0) = WAIT_OBJECT_0) then
        begin
          ProcessEnded := True;
          EndedTick := GetTickCount;
        end;
        // Proces weg zonder eindregel (na een korte marge voor de laatste schrijfactie): onverwacht gestopt.
        if ProcessEnded and (GetTickCount - EndedTick > 5000) then
        begin
          ReportRuntimeFailure(CustomMessage('RuntimeStoppedUnexpectedly'), LogPath, ShowUi);
          Exit;
        end;
        // Na twee minuten nog geen proces-id: PowerShell is niet goed gestart.
        if (ProcessHandle = 0) and (GetTickCount - StartTick > 120000) then
        begin
          ReportRuntimeFailure(CustomMessage('RuntimeNotStarted'), LogPath, ShowUi);
          Exit;
        end;

        for I := 1 to 5 do
        begin
          if ShowUi then
            RuntimePage.SetProgress(Percent, 100);
          Sleep(100);
        end;
      end;
    end;

    if FinalCode = 0 then
    begin
      Log('Transcriptie-omgeving klaar: ' + DutchText + ' Logboek: ' + LogPath);
      if ShowUi then
        RuntimePage.SetProgress(100, 100);
    end
    else
      ReportRuntimeFailure(PickLanguage(DutchText, EnglishText), LogPath, ShowUi);
  finally
    if ProcessHandle <> 0 then
      CloseHandle(ProcessHandle);
    if ShowUi then
    begin
      RuntimeStopButton.Visible := False;
      WizardForm.CancelButton.Visible := True;
      RuntimePage.Hide;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    WriteInstallLanguage;
    if WizardSilent then
      Log(Format('Vrije ruimte op de schijf van %s: %d GB', [ExpandConstant('{localappdata}'), GetFreeGigabytes(ExpandConstant('{localappdata}'))]));
    RunRuntimeSetup;
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  Result := RuntimeExitCode;
end;

// Na de bevestiging, vóór het verwijderen (stap usAppMutexCheck): een draaiende Captivox uit deze programmamap netjes sluiten, zodat de gebruiker dat niet zelf
// via het systeemvak hoeft te doen. Een andere Captivox op dezelfde pc blijft ongemoeid (zie tools\Close-Captivox.ps1).
procedure CloseRunningCaptivox();
var
  Script: String;
  ResultCode: Integer;
begin
  Script := ExpandConstant('{app}\tools\Close-Captivox.ps1');
  if FileExists(Script) then
    Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + Script + '" -AppDir "' + ExpandConstant('{app}') + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usAppMutexCheck then
    CloseRunningCaptivox();
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\{#DataFolder}');
    if DirExists(DataDir) and not UninstallSilent then
    begin
      if MsgBox(CustomMessage('RemoveDataQuestion'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
