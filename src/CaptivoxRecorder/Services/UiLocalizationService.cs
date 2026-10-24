using System.Runtime.CompilerServices;
using System.Windows.Forms;
using LocalCaptureAgent.Models;

namespace LocalCaptureAgent.Services;

internal static partial class UiLocalizationService
{
    private sealed class TextSnapshot
    {
        public string? Text { get; init; }
        public string? PlaceholderText { get; init; }
    }

    private static readonly ConditionalWeakTable<object, TextSnapshot> TextCache = new();

    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Captivox Recorder · Instellingen en admin"] = "Captivox Recorder · Settings and admin",
        ["Instellingen en admin"] = "Settings and admin",
        ["Recorder"] = "Recorder",
        ["Status & worker"] = "Status & worker",
        ["Status & diagnostics"] = "Status & diagnostics",
        ["Sessiebibliotheek"] = "Session library",
        ["Recorderopties"] = "Recorder options",
        ["Applicatietaal"] = "Application language",
        ["Transcriptie"] = "Transcription",
        ["Gebruik deze instellingen voor opstartgedrag, applicatietaal en devicecontrole. Wijzigingen gelden voor nieuwe sessies."] = "Use these settings for startup behavior, application language and device checks. Changes apply to new sessions.",
        ["Publieke productlijn en ondersteuning"] = "Public product line and support",
        ["Meer informatie"] = "More information",
        ["Controleer devices"] = "Check devices",
        ["Meeting detectie"] = "Meeting detection",
        ["Transcriptiewerker"] = "Transcription worker",
        ["Monitoring en notificaties"] = "Monitoring and notifications",
        ["Retentie"] = "Retention",
        ["Import, enhancement en export"] = "Import, enhancement and export",
        ["Diagnostiek en beheer"] = "Diagnostics and administration",
        ["Opname starten"] = "Start recording",
        ["Opname stoppen"] = "Stop recording",
        ["Opname afronden..."] = "Finishing recording...",
        ["Open sessiemap"] = "Open session folder",
        ["Open map"] = "Open folder",
        ["Kies map"] = "Choose folder",
        ["Ververs"] = "Refresh",
        ["Ververs bibliotheek"] = "Refresh library",
        ["Open transcript"] = "Open transcript",
        ["Diagnostics export failed"] = "Diagnostics export failed",
        ["Het diagnosepakket bevat logbestanden, manifestgegevens, lokale paden en mogelijk meetingmetadata. Audio wordt niet standaard meegepakt. Deel dit pakket alleen met beheerders of ontwikkelaars die deze informatie mogen inzien."] = "The diagnostics package contains log files, manifest data, local paths and potentially meeting metadata. Audio is not included by default. Only share this package with administrators or developers who are allowed to view this information.",
        ["Wil je doorgaan met exporteren?"] = "Do you want to continue exporting?",
        ["Waarschuwing diagnosepakket"] = "Diagnostics package warning",
        ["ZIP-bestand (*.zip)|*.zip"] = "ZIP file (*.zip)|*.zip",
        ["Diagnosepakket opgeslagen op:{Environment.NewLine}{dialog.FileName}{Environment.NewLine}{Environment.NewLine}Niet alle bestanden konden live worden meegenomen. Bekijk in de ZIP het bestand export_warnings.txt voor details."] = "Diagnostics package saved to:{Environment.NewLine}{dialog.FileName}{Environment.NewLine}{Environment.NewLine}Not all files could be captured live. See export_warnings.txt inside the ZIP for details.",
        ["Diagnosepakket opgeslagen op:{Environment.NewLine}{dialog.FileName}"] = "Diagnostics package saved to:{Environment.NewLine}{dialog.FileName}",
        ["Diagnosepakket geëxporteerd"] = "Diagnostics package exported",
        ["Export diagnosepakket mislukt"] = "Export diagnostics package failed",
        ["Wijzigingen gelden voor nieuwe sessies. Workerinstellingen worden bij de volgende poll opnieuw ingelezen."] = "Changes apply to new sessions. Worker settings are reloaded on the next poll.",
        ["Watch folders beheren"] = "Manage watch folders",
        ["Watch folders beheren"] = "Manage watch folders",
        ["Watch folders configureren"] = "Configure watch folders",
        ["Watch folders zijn momenteel niet beschikbaar door lokale configuratie of sessiestatus."] = "Watch folders are currently unavailable because of local configuration or session status.",
        ["Preflight device-check"] = "Preflight device check",
        ["Watch folders, custom vocabulary en handmatige audio-import beheer je centraal in Instellingen. Exports worden automatisch naast transcript.json aangemaakt wanneer deze opties zijn ingeschakeld."] = "Manage watch folders, custom vocabulary and manual audio import centrally in Settings. Exports are created automatically next to transcript.json when these options are enabled.",
        ["Datum"] = "Date",
        ["Titel / sessie"] = "Title / session",
        ["Bron"] = "Source",
        ["Fragment"] = "Snippet",
        ["Recoverycontrole mislukt"] = "Recovery check failed",
        ["Selecteer eerst een sessie in de bibliotheek."] = "Select a session in the library first.",
        ["Geen sessie geselecteerd"] = "No session selected",
        ["Voor deze sessie is nog geen transcriptbestand beschikbaar."] = "No transcript file is available for this session yet.",
        ["Transcript ontbreekt"] = "Transcript missing",
        ["Voor deze sessie is geen transcripttekst beschikbaar."] = "No transcript text is available for this session.",
        ["Voor export is transcript.json vereist. Deze sessie heeft dat bestand nog niet."] = "Export requires transcript.json. This session does not have that file yet.",
        ["Export niet mogelijk"] = "Export not possible",
        ["De sessiemap bestaat niet meer op schijf."] = "The session folder no longer exists on disk.",
        ["Sessiemap ontbreekt"] = "Session folder missing",
        ["Watch folder verwerking inschakelen"] = "Enable watch folder processing",
        ["Bronbestanden na succesvolle ingest uit de watch folder verwijderen"] = "Remove source files from the watch folder after successful ingest",
        ["Map toevoegen"] = "Add folder",
        ["Minimale stabiele leeftijd (sec)"] = "Minimum stable age (sec)",
        ["Voer per regel één term in. Optioneel kun je varianten opgeven als: Canonical = variant 1, variant 2. Deze lijst wordt als contextprompt naar de transcriptieroute gestuurd en is geen harde garantie op herkenning."] = "Enter one term per line. Optionally, you can define variants as: Canonical = variant 1, variant 2. This list is sent as contextual prompting to the transcription route and is not a hard guarantee of recognition.",
        ["Watch folders, custom vocabulary, device-check en handmatige audio-import beheer je centraal via Instellingen. De recorder-GUI blijft hiermee gericht op opnemen en terugvinden."] = "Manage watch folders, custom vocabulary, device checks and manual audio import centrally via Settings. This keeps the recorder GUI focused on recording and finding sessions.",
        ["Advies: laat mic gain op 1.00 staan en verhoog system gain alleen als de tegenpartij structureel te zacht binnenkomt. Gebruik vóór een echte opname eerst de device-check en controleer expliciet of het gekozen renderdevice ook het werkelijke afspeelapparaat is."] = "Advice: keep mic gain at 1.00 and only increase system gain if the other side is consistently too quiet. Before a real recording, run the device check first and explicitly verify that the selected render device is the actual playback device.",
        ["Hotkeys: Ctrl+Shift+F9 start/stop opname · Ctrl+Shift+F10 venster tonen/verbergen"] = "Hotkeys: Ctrl+Shift+F9 start/stop recording · Ctrl+Shift+F10 show/hide window",
        ["Statusdetails verschijnen hier na starten of stoppen van een opname."] = "Status details appear here after starting or stopping a recording.",
        ["Uit"] = "Off",
        ["Vernieuwen"] = "Refresh",
        ["Basisopname en basistranscriptie beschikbaar."] = "Basic recording and basic transcription available.",
        ["Licentiecode"] = "License code",
        ["Hervalidatie"] = "Revalidate",
        ["Activatie wissen"] = "Clear activation",
        ["Editionstatus"] = "Edition status",
        ["Activatie"] = "Activation",
        ["Toewijzen aan organisatie"] = "Assign to organization",
        ["Toewijzen aan project / dossier"] = "Assign to project / case",
        ["Toewijzen aan overlegtype"] = "Assign to meeting type",
        ["Onderwerpen toewijzen…"] = "Assign topics…",
        ["Sessiegegevens laden mislukt."] = "Failed to load session data.",
        ["Verwijder geselecteerd"] = "Delete selected",
        ["Selecteer eerst één of meer sessies in de bibliotheek."] = "Select one or more sessions in the library first.",
        ["Nieuwe onderwerpen"] = "New topics",
        ["Bijvoorbeeld: audit, planning, risicoanalyse"] = "For example: audit, planning, risk analysis",
        ["Sessie nog niet gereed"] = "Session not ready yet",
        ["Nog niet beschikbaar"] = "Not available yet",
        ["Start opname"] = "Start recording",
        ["Negeren"] = "Dismiss",
        ["Kopieer transcript"] = "Copy transcript",
        ["Exporteer SRT"] = "Export SRT",
        ["Exporteer VTT"] = "Export VTT",
        ["Verwijderen"] = "Delete",
        ["Beheer indeling"] = "Manage taxonomy",
        ["Reset filters"] = "Reset filters",
        ["Opslaan"] = "Save",
        ["Annuleren"] = "Cancel",
        ["Sluiten"] = "Close",
        ["Toevoegen"] = "Add",
        ["Importeren"] = "Import",
        ["Exporteren"] = "Export",
        ["Leegmaken"] = "Clear",
        ["Kiezen"] = "Choose",
        ["Nog geen sessie geselecteerd."] = "No session selected yet.",
        ["Nog geen sessie"] = "No session yet",
        ["Nog geen opname gestart."] = "No recording started yet.",
        ["Nog niet geladen"] = "Not loaded yet",
        ["Nog niet gescand"] = "Not scanned yet",
        ["Nog niet gecontroleerd"] = "Not checked yet",
        ["Niet actief"] = "Not active",
        ["Geen actieve sessie"] = "No active session",
        ["Gereed."] = "Ready.",
        ["Klaar"] = "Ready",
        ["Onbekend"] = "Unknown",
        ["Ingeschakeld"] = "Enabled",
        ["Uitgeschakeld"] = "Disabled",
        ["Nog geen verwerking"] = "No processing yet",
        ["Niet geselecteerd"] = "Not selected",
        ["Zoek in sessies en transcripties"] = "Search sessions and transcripts",
        ["Opnameduur"] = "Recording duration",
        ["Venster tonen"] = "Show window",
        ["Venster verbergen"] = "Hide window",
        ["Uitvoermap openen"] = "Open output folder",
        ["Opnamemap openen"] = "Open recordings folder",
        ["Afsluiten"] = "Exit",
        ["Transcript laden..."] = "Loading transcript...",
        ["Details van geselecteerde sessie"] = "Details of selected session",
        ["Selecteer een sessie om transcriptdetails te bekijken."] = "Select a session to view transcript details.",
        ["Instellingen bijgewerkt."] = "Settings updated.",
        ["Recorder- en workerinstellingen zijn bijgewerkt voor nieuwe sessies."] = "Recorder and worker settings were updated for new sessions.",
        ["Start applicatie bij aanmelden op Windows"] = "Start application when signing in to Windows",
        ["Geminimaliseerd starten"] = "Start minimized",
        ["Minimaliseren naar tray"] = "Minimize to tray",
        ["Meeting detectie inschakelen"] = "Enable meeting detection",
        ["Browser meetings (Meet/Teams/Zoom)"] = "Browser meetings (Meet/Teams/Zoom)",
        ["Uitgebreide logging / diagnosemodus"] = "Extended logging / diagnostics mode",
        ["Bewaar worker-uitvoer (stdout/stderr) bij diagnosemodus"] = "Keep worker output (stdout/stderr) in diagnostics mode",
        ["WhisperX ingeschakeld"] = "WhisperX enabled",
        ["Gebruik productieprofiel (aanbevolen)"] = "Use production profile (recommended)",
        ["Opname health monitor inschakelen"] = "Enable recording health monitor",
        ["Silence detection inschakelen"] = "Enable silence detection",
        ["Retentie inschakelen voor completed sessies"] = "Enable retention for completed sessions",
        ["Audio enhancement inschakelen"] = "Enable audio enhancement",
        ["Alleen voor import/watch folder"] = "Only for import/watch folder",
        ["Maak ook transcript.srt"] = "Also create transcript.srt",
        ["Maak ook transcript.vtt"] = "Also create transcript.vtt",
        ["Verbinding testen"] = "Test connection",
        ["Exporteer diagnosepakket"] = "Export diagnostics package",
        ["Session library"] = "Session library",
        ["Alle organisaties"] = "All organizations",
        ["Alle projecten / dossiers"] = "All projects / cases",
        ["Alle overlegtypen"] = "All meeting types",
        ["Alle onderwerpen"] = "All topics",
        ["Alle sessies zichtbaar."] = "All sessions visible.",
        ["Wijs indeling toe via rechtermuisknop op een sessie."] = "Assign taxonomy via right-click on a session.",
        ["Geselecteerde sessie:"] = "Selected session:",
        ["sessies geselecteerd. Toewijzingen via rechtermuisknop worden op alle geselecteerde sessies toegepast."] = "sessions selected. Assignments made via right-click will be applied to all selected sessions.",
        ["Nog niet toegewezen"] = "Not assigned yet",
        ["Geen onderwerpen"] = "No topics",
        ["Nog geen status"] = "No status yet",
        ["Nog geen artefactstatus"] = "No artifact status yet",
        ["Artefactstatus niet beschikbaar bij meerdere geselecteerde sessies"] = "Artifact status is unavailable when multiple sessions are selected",
        ["Meerdere waarden"] = "Multiple values",
        ["Meerdere sessies geselecteerd"] = "Multiple sessions selected",
        ["Gemeenschappelijk:"] = "Common:",
        ["Geen gemeenschappelijke onderwerpen"] = "No shared topics",
        ["Actieve filters:"] = "Active filters:",
        ["Er zijn nu 0 sessies zichtbaar, terwijl de bibliotheek"] = "There are currently 0 sessions visible while the library contains",
        ["sessie(s) bevat. Gebruik Reset filters om alles weer te tonen."] = "session(s). Use Reset filters to show everything again.",
        ["Zichtbaar:"] = "Visible:",
        ["van"] = "of",
        ["in huidige weergave."] = "in the current view.",
        ["organisatie ="] = "organization =",
        ["project / dossier ="] = "project / case =",
        ["overlegtype ="] = "meeting type =",
        ["onderwerp ="] = "topic =",
        ["Organisatie"] = "Organization",
        ["Project / dossier"] = "Project / case",
        ["Overlegtype"] = "Meeting type",
        ["Onderwerpen"] = "Topics",
        ["Projecten / dossiers"] = "Projects / cases",
        ["Beheer organisaties, projecten/dossiers, overlegtypen en onderwerpen voor de bibliotheek."] = "Manage organizations, projects/cases, meeting types and topics for the library.",
        ["Zet organisatie-, project-, overlegtype- en onderwerpfilters terug naar alle sessies."] = "Reset organization, project, meeting type and topic filters to all sessions.",
        ["Status"] = "Status",
        ["Meeting detectie werkt als assistentlaag: Captivox detecteert waarschijnlijk Teams/Zoom/Meet-gebruik en stelt dan voor om de opname te starten. Er wordt niet automatisch opgenomen."] = "Meeting detection works as an assistant layer: Captivox detects likely Teams/Zoom/Meet usage and then suggests starting the recording. Recording does not start automatically.",
        ["Detectievertraging (sec)"] = "Detection delay (sec)",
        ["Cooldown na negeren (min)"] = "Cooldown after dismissing (min)",
        ["Organisaties"] = "Organizations",
        ["Overlegtypen"] = "Meeting types",
        ["Nieuwe organisatie"] = "New organization",
        ["Bijvoorbeeld: Gemeente Voorbeeld"] = "For example: Example Municipality",
        ["Nieuw project / dossier"] = "New project / case",
        ["Bijvoorbeeld: NIS2 implementatie"] = "For example: NIS2 implementation",
        ["Nieuw overlegtype"] = "New meeting type",
        ["Bijvoorbeeld: Teamvergadering"] = "For example: Team meeting",
        ["Nieuw onderwerp"] = "New topic",
        ["Bijvoorbeeld: MT, NIS2, incident"] = "For example: MT, NIS2, incident",
        ["Beheer hier de bibliotheekbrede indeling. Gebruik Organisatie voor je eigen organisatie, een opdrachtgever of een andere hoofdcontext, Project / dossier voor nadere afbakening en Onderwerpen voor trefwoorden. Toewijzing aan een specifieke sessie gebeurt via rechtermuisknop op een sessie in de lijst."] = "Manage the library-wide taxonomy here. Use Organization for your own organization, a client or another main context, Project / case for a narrower scope, and Topics for keywords. Assignment to a specific session is done via right-click on a session in the list.",
        ["Gebruik Organisatie als harde scheiding tussen organisaties, opdrachtgevers of andere hoofdcontexten."] = "Use Organization as a hard separation between organizations, clients or other main contexts.",
        ["Gebruik Project / dossier voor projecten, dossiers of teams binnen een organisatie."] = "Use Project / case for projects, cases or teams within an organization.",
        ["Beschikbaarheid"] = "Availability",
        ["Runtime"] = "Runtime",
        ["Python/venv-pad"] = "Python/venv path",
        ["WhisperX model"] = "WhisperX model",
        ["Compute type"] = "Compute type",
        ["Transcriptietaal"] = "Transcription language",
        ["Batch size"] = "Batch size",
        ["Transcript-output"] = "Transcript output",
        ["Poll-interval (sec)"] = "Poll interval (sec)",
        ["Min READY-age (sec)"] = "Min READY age (sec)",
        ["Stale started na (min)"] = "Stale started after (min)",
        ["Max retries (0=onbeperkt)"] = "Max retries (0=unlimited)",
        ["Retry backoff (min)"] = "Retry backoff (min)",
        ["ffmpeg-pad"] = "ffmpeg path",
        ["Silence threshold (%)"] = "Silence threshold (%)",
        ["Mic stilte (sec)"] = "Mic silence (sec)",
        ["System stilte (sec)"] = "System silence (sec)",
        ["Beide stil (sec)"] = "Both silent (sec)",
        ["Cooldown waarschuwingen (sec)"] = "Warning cooldown (sec)",
        ["Opname afgerond"] = "Recording finished",
        ["Transcript gereed"] = "Transcript ready",
        ["Transcriptie mislukt"] = "Transcription failed",
        ["Stilte-waarschuwing"] = "Silence warning",
        ["Health warning"] = "Health warning",
        ["Audio (dagen)"] = "Audio (days)",
        ["Transcripten (dagen)"] = "Transcripts (days)",
        ["Sessiegegevens/logs (dagen)"] = "Session data/logs (days)",
        ["Retentie verwijdert alleen completed sessies. Audio kan eerder worden verwijderd dan transcripten. Sessiegegevens/logs worden niet eerder verwijderd dan transcripten. De uitvoering gebeurt via de workerroute, niet via de GUI zelf."] = "Retention only removes completed sessions. Audio can be deleted earlier than transcripts. Session data/logs are never removed earlier than transcripts. Execution happens via the worker path, not via the GUI itself.",
        ["Enhancementprofiel"] = "Enhancement profile",
        ["Custom vocabulary"] = "Custom vocabulary",
        ["Audio importeren"] = "Import audio",
        ["Beheer"] = "Manage",
        ["Watch folders zijn actief binnen de huidige editie. Custom vocabulary blijft onderdeel van de basisworkflow. Exports worden automatisch naast transcript.json aangemaakt wanneer deze opties zijn ingeschakeld."] = "Watch folders are active in the current edition. Custom vocabulary remains part of the base workflow. Exports are automatically created next to transcript.json when these options are enabled.",
        ["Automatisch"] = "Automatic",
        ["Verwerkprofiel"] = "Processing profile",
        ["Detecteer hardware"] = "Detect hardware",
        ["Timeout (sec)"] = "Timeout (sec)",
        ["Prompttemplate"] = "Prompt template",
        ["Extra instructie"] = "Extra instruction",
        ["Templatebeheer"] = "Template management",
        ["Activeren"] = "Enable",
        ["Exportmap"] = "Export folder",
        ["Artefacten"] = "Artifacts",
        ["session.log blijft altijd aan. diagnostics.log en worker stdout/stderr worden alleen gebruikt wanneer diagnosemodus actief is. Diagnose-export neemt standaard alleen de huidige sessie plus de bijbehorende daglogs mee."] = "session.log always remains enabled. diagnostics.log and worker stdout/stderr are only used when diagnostics mode is active. By default, diagnostics export only includes the current session plus the corresponding day logs.",
        ["Instellingen opslaan mislukt"] = "Saving settings failed",
        ["Cloudprovider verbonden"] = "Cloud provider connected",
        ["Test draait..."] = "Test running...",
    };
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LanguageOverrides =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["de"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Detecteer hardware"] = "Hardware erkennen",

                ["Instellingen en admin"] = "Einstellungen und Verwaltung",
                ["Recorder"] = "Recorder",
                ["Status & worker"] = "Status & Worker",
                ["Status & diagnostics"] = "Status & Diagnostik",
                ["Sessiebibliotheek"] = "Sitzungsbibliothek",
                ["Applicatietaal"] = "Anwendungssprache",
                ["Transcriptie"] = "Transkription",
                ["Recorderopties"] = "Recorder-Optionen",
                ["Meeting detectie"] = "Besprechungserkennung",
                ["Transcriptiewerker"] = "Transkriptions-Worker",
                ["Monitoring en notificaties"] = "Monitoring und Benachrichtigungen",
                ["Retentie"] = "Aufbewahrung",
                ["Import, enhancement en export"] = "Import, Aufbereitung und Export",
                ["Diagnostiek en beheer"] = "Diagnostik und Verwaltung",
                ["Opname starten"] = "Aufnahme starten",
                ["Open map"] = "Ordner öffnen",
                ["Opslaan"] = "Speichern",
                ["Annuleren"] = "Abbrechen",
                ["Verwijderen"] = "Löschen",
                ["Toevoegen"] = "Hinzufügen",
                ["Venster tonen"] = "Fenster anzeigen",
                ["Venster verbergen"] = "Fenster ausblenden",
                ["Gereed."] = "Bereit.",
                ["Klaar"] = "Bereit",
                ["Zoek in sessies en transcripties"] = "In Sitzungen und Transkripten suchen",
            },
            ["fr"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Detecteer hardware"] = "Détecter le matériel",

                ["Instellingen en admin"] = "Paramètres et administration",
                ["Sessiebibliotheek"] = "Bibliothèque des sessions",
                ["Applicatietaal"] = "Langue de l'application",
                ["Transcriptie"] = "Transcription",
                ["Recorderopties"] = "Options de l'enregistreur",
                ["Meeting detectie"] = "Détection de réunion",
                ["Transcriptiewerker"] = "Worker de transcription",
                ["Monitoring en notificaties"] = "Surveillance et notifications",
                ["Retentie"] = "Rétention",
                ["Import, enhancement en export"] = "Import, amélioration et export",
                ["Diagnostiek en beheer"] = "Diagnostic et administration",
                ["Opname starten"] = "Démarrer l'enregistrement",
                ["Open map"] = "Ouvrir le dossier",
                ["Opslaan"] = "Enregistrer",
                ["Annuleren"] = "Annuler",
                ["Verwijderen"] = "Supprimer",
                ["Toevoegen"] = "Ajouter",
                ["Venster tonen"] = "Afficher la fenêtre",
                ["Venster verbergen"] = "Masquer la fenêtre",
                ["Gereed."] = "Prêt.",
                ["Klaar"] = "Prêt",
            },
            ["it"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Detecteer hardware"] = "Rileva hardware",

                ["Instellingen en admin"] = "Impostazioni e amministrazione",
                ["Sessiebibliotheek"] = "Libreria sessioni",
                ["Applicatietaal"] = "Lingua dell'applicazione",
                ["Transcriptie"] = "Trascrizione",
                ["Recorderopties"] = "Opzioni registratore",
                ["Meeting detectie"] = "Rilevamento riunioni",
                ["Transcriptiewerker"] = "Worker di trascrizione",
                ["Monitoring en notificaties"] = "Monitoraggio e notifiche",
                ["Retentie"] = "Conservazione",
                ["Import, enhancement en export"] = "Importazione, miglioramento ed esportazione",
                ["Diagnostiek en beheer"] = "Diagnostica e gestione",
                ["Opname starten"] = "Avvia registrazione",
                ["Open map"] = "Apri cartella",
                ["Opslaan"] = "Salva",
                ["Annuleren"] = "Annulla",
                ["Verwijderen"] = "Elimina",
                ["Toevoegen"] = "Aggiungi",
                ["Venster tonen"] = "Mostra finestra",
                ["Venster verbergen"] = "Nascondi finestra",
                ["Gereed."] = "Pronto.",
                ["Klaar"] = "Pronto",
            },
            ["es"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Detecteer hardware"] = "Detectar hardware",

                ["Instellingen en admin"] = "Configuración y administración",
                ["Sessiebibliotheek"] = "Biblioteca de sesiones",
                ["Applicatietaal"] = "Idioma de la aplicación",
                ["Transcriptie"] = "Transcripción",
                ["Recorderopties"] = "Opciones del grabador",
                ["Meeting detectie"] = "Detección de reuniones",
                ["Transcriptiewerker"] = "Worker de transcripción",
                ["Monitoring en notificaties"] = "Monitorización y notificaciones",
                ["Retentie"] = "Retención",
                ["Import, enhancement en export"] = "Importación, mejora y exportación",
                ["Diagnostiek en beheer"] = "Diagnóstico y administración",
                ["Opname starten"] = "Iniciar grabación",
                ["Open map"] = "Abrir carpeta",
                ["Opslaan"] = "Guardar",
                ["Annuleren"] = "Cancelar",
                ["Verwijderen"] = "Eliminar",
                ["Toevoegen"] = "Añadir",
                ["Venster tonen"] = "Mostrar ventana",
                ["Venster verbergen"] = "Ocultar ventana",
                ["Gereed."] = "Listo.",
                ["Klaar"] = "Listo",
            },
            ["zh"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Detecteer hardware"] = "检测硬件",

                ["Instellingen en admin"] = "设置和管理",
                ["Sessiebibliotheek"] = "会话库",
                ["Applicatietaal"] = "应用语言",
                ["Transcriptie"] = "转录",
                ["Recorderopties"] = "录音选项",
                ["Meeting detectie"] = "会议检测",
                ["Transcriptiewerker"] = "转录工作器",
                ["Monitoring en notificaties"] = "监控和通知",
                ["Retentie"] = "保留策略",
                ["Import, enhancement en export"] = "导入、增强和导出",
                ["Diagnostiek en beheer"] = "诊断和管理",
                ["Opname starten"] = "开始录音",
                ["Open map"] = "打开文件夹",
                ["Opslaan"] = "保存",
                ["Annuleren"] = "取消",
                ["Verwijderen"] = "删除",
                ["Toevoegen"] = "添加",
                ["Venster tonen"] = "显示窗口",
                ["Venster verbergen"] = "隐藏窗口",
                ["Gereed."] = "就绪。",
                ["Klaar"] = "就绪",
            },
            ["ja"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Detecteer hardware"] = "ハードウェアを検出",

                ["Instellingen en admin"] = "設定と管理",
                ["Sessiebibliotheek"] = "セッションライブラリ",
                ["Applicatietaal"] = "アプリ言語",
                ["Transcriptie"] = "文字起こし",
                ["Recorderopties"] = "録音オプション",
                ["Meeting detectie"] = "会議検出",
                ["Transcriptiewerker"] = "文字起こしワーカー",
                ["Monitoring en notificaties"] = "監視と通知",
                ["Retentie"] = "保持",
                ["Import, enhancement en export"] = "インポート・強調・エクスポート",
                ["Diagnostiek en beheer"] = "診断と管理",
                ["Opname starten"] = "録音開始",
                ["Open map"] = "フォルダーを開く",
                ["Opslaan"] = "保存",
                ["Annuleren"] = "キャンセル",
                ["Verwijderen"] = "削除",
                ["Toevoegen"] = "追加",
                ["Venster tonen"] = "ウィンドウを表示",
                ["Venster verbergen"] = "ウィンドウを非表示",
                ["Gereed."] = "準備完了。",
                ["Klaar"] = "準備完了",
            },
        };

    // De taal van de interface. Het taalprofiel kiest de taal van de opnames (transcriptie); het verandert de interface niet.
    // Zolang er geen aparte, expliciete instelling voor de interfacetaal is, blijft de interface Nederlands.
    public static string ResolveUiLanguage(string? languageProfile)
        => "nl";

    public static string TranslateText(string? languageProfile, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        var languageId = ResolveUiLanguage(languageProfile);
        if (languageId == "nl")
        {
            return text;
        }

        if (LanguageOverrides.TryGetValue(languageId, out var overrides) && overrides.TryGetValue(text, out var localized))
        {
            return localized;
        }

        if (English.TryGetValue(text, out var english))
        {
            return english;
        }


        var composite = TranslateComposite(text);
        return composite ?? text;
    }

    private static string? TranslateComposite(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }


        return null;
    }

    public static void ApplyToControlTree(Control root, string? languageProfile)
    {
        ApplyToObject(root, languageProfile);
        ApplyToOwnedMenu(root.ContextMenuStrip, languageProfile);

        foreach (Control child in root.Controls)
        {
            ApplyToControlTree(child, languageProfile);
        }

        if (root is TabControl tabControl)
        {
            foreach (TabPage page in tabControl.TabPages)
            {
                ApplyToObject(page, languageProfile);
                foreach (Control child in page.Controls)
                {
                    ApplyToControlTree(child, languageProfile);
                }
            }
        }

        if (root is ListView listView)
        {
            foreach (ColumnHeader header in listView.Columns)
            {
                ApplyToObject(header, languageProfile);
            }
        }

        if (root is ToolStrip toolStrip)
        {
            ApplyToItems(toolStrip.Items, languageProfile);
        }
    }

    private static void ApplyToOwnedMenu(ContextMenuStrip? menu, string? languageProfile)
    {
        if (menu is null)
        {
            return;
        }

        ApplyToObject(menu, languageProfile);
        ApplyToItems(menu.Items, languageProfile);
    }

    private static void ApplyToItems(ToolStripItemCollection items, string? languageProfile)
    {
        foreach (ToolStripItem item in items)
        {
            ApplyToObject(item, languageProfile);
            if (item is ToolStripDropDownItem dropdown)
            {
                ApplyToItems(dropdown.DropDownItems, languageProfile);
            }
        }
    }

    private static void ApplyToObject(object instance, string? languageProfile)
    {
        switch (instance)
        {
            case ComboBox:
                return;
            case Control control:
            {
                if (string.Equals(control.AccessibleDescription, "skip-ui-localization", StringComparison.Ordinal))
                {
                    return;
                }

                var snapshot = TextCache.GetValue(control, static key =>
                {
                    var sourceControl = (Control)key;
                    return new TextSnapshot
                    {
                        Text = sourceControl.Text,
                        PlaceholderText = sourceControl is TextBox textBox ? textBox.PlaceholderText : null
                    };
                });

                if (control is TextBox textBox)
                {
                    if (snapshot.PlaceholderText is not null)
                    {
                        textBox.PlaceholderText = TranslateText(languageProfile, snapshot.PlaceholderText);
                    }
                }
                else
                {
                    control.Text = TranslateText(languageProfile, snapshot.Text);
                }
                break;
            }
            case ToolStripItem item:
            {
                if (string.Equals(item.AccessibleDescription, "skip-ui-localization", StringComparison.Ordinal))
                {
                    return;
                }

                var snapshot = TextCache.GetValue(item, static key => new TextSnapshot { Text = ((ToolStripItem)key).Text });
                item.Text = TranslateText(languageProfile, snapshot.Text);
                break;
            }
            case ColumnHeader header:
            {
                var snapshot = TextCache.GetValue(header, static key => new TextSnapshot { Text = ((ColumnHeader)key).Text });
                header.Text = TranslateText(languageProfile, snapshot.Text);
                break;
            }
        }
    }
}
