using Microsoft.VisualBasic.FileIO;
using System.Diagnostics;
using System.Text;
using LocalCaptureAgent.Models;
using LocalCaptureAgent.Services;
namespace LocalCaptureAgent;
public sealed partial class MainForm
{
    private void ConfigureLibraryListView()
    {
        _libraryListView.Columns.Clear();
        _libraryListView.Columns.Add("Datum", 132);
        _libraryListView.Columns.Add("Titel / sessie", 260);
        _libraryListView.Columns.Add("Bron", 92);
        _libraryListView.Columns.Add("Organisatie", 150);
        _libraryListView.Columns.Add("Project / dossier", 160);
        _libraryListView.Columns.Add("Status", 150);
        _libraryListView.Columns.Add("Fragment", 320);
        _libraryListView.DoubleClick += (_, _) => OpenSelectedLibraryTranscript();
    }
    private async Task RecoverInterruptedSessionsOnStartupAsync()
    {
        _recoveryStatusLabel.Text = "Recoverycontrole loopt op achtergrond";
        try
        {
            var settingsSnapshot = _appSettings;
            var recovery = await Task.Run(() =>
            {
                var interrupted = SessionRecoveryService.RecoverInterruptedSessions(settingsSnapshot, TimeSpan.FromMinutes(2));
                var transcriptions = SessionRecoveryService.RecoverInterruptedTranscriptions(settingsSnapshot, TimeSpan.FromSeconds(30));
                var reviewPacks = 0;
                return (interrupted, transcriptions, reviewPacks);
            });
            if (IsDisposed)
            {
                return;
            }

            var recoveredText = recovery.interrupted > 0
                ? $"{recovery.interrupted} opname(s) onderbroken"
                : "geen onderbroken opnames";
            var transcriptionText = recovery.transcriptions.RequeuedSessions > 0 || recovery.transcriptions.CompletedFromExistingOutputs > 0
                ? $"{recovery.transcriptions.RequeuedSessions} transcriptie(s) opnieuw klaargezet, {recovery.transcriptions.CompletedFromExistingOutputs} completed uit bestaande output"
                : "geen incomplete transcripties";
            _recoveryStatusLabel.Text = $"{recoveredText}; {transcriptionText}";
            if (recovery.interrupted > 0 || recovery.transcriptions.RequeuedSessions > 0 || recovery.transcriptions.CompletedFromExistingOutputs > 0 || recovery.reviewPacks > 0)
            {
                await PollBackgroundStateAsync();
                if (_libraryPanelBuilt && _mainTabs?.SelectedTab == _libraryTabPage)
                {
                    await RunLibraryUiActionAsync("Recovery bibliotheek vernieuwen", ct => RefreshLibraryAsync(ct));
                }
            }
        }
        catch (Exception ex)
        {
            _recoveryStatusLabel.Text = "Recoverycontrole mislukt";
            AppRuntime.LogException("Session recovery startup failed", ex);
        }
    }
    private Task RefreshLibraryAsync() => RefreshLibraryAsync(CancellationToken.None);

    private async Task RefreshLibraryAsync(CancellationToken cancellationToken)
    {
        var refreshWatch = Stopwatch.StartNew();
        var query = _librarySearchTextBox.Text?.Trim();
        var tagFilter = GetSelectedLibraryTagFilter();
        var contextFilter = GetSelectedLibraryContextFilter();
        var subcontextFilter = GetSelectedLibrarySubcontextFilter();
        var meetingTypeFilter = GetSelectedLibraryMeetingTypeFilter();
        var currentSelectionKey = GetSelectedLibraryEntry()?.DirectoryPath;
        var hasFilter = !string.IsNullOrWhiteSpace(query)
            || !string.IsNullOrWhiteSpace(tagFilter)
            || !string.IsNullOrWhiteSpace(contextFilter)
            || !string.IsNullOrWhiteSpace(subcontextFilter)
            || !string.IsNullOrWhiteSpace(meetingTypeFilter);
        var requiresFullTextScan = !string.IsNullOrWhiteSpace(query);
        var refreshId = Interlocked.Increment(ref _libraryRefreshVersion);
        cancellationToken.ThrowIfCancellationRequested();

        _queueStatusLabel.Text = requiresFullTextScan
            ? "Bibliotheek zoekt in transcripties..."
            : "Bibliotheekmetadata laden...";

        var metadataWatch = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        var allEntries = await Task.Run(() => TranscriptLibraryService.Scan(_appSettings, null, null, null, null, null, cancellationToken: cancellationToken), cancellationToken);
        metadataWatch.Stop();

        IReadOnlyList<TranscriptLibraryEntry> entries;
        if (!hasFilter)
        {
            entries = allEntries;
        }
        else if (requiresFullTextScan)
        {
            var searchWatch = Stopwatch.StartNew();
            entries = await Task.Run(() => TranscriptLibraryService.Scan(_appSettings, query, tagFilter, contextFilter, subcontextFilter, meetingTypeFilter, cancellationToken: cancellationToken), cancellationToken);
            searchWatch.Stop();
            cancellationToken.ThrowIfCancellationRequested();
            AppRuntime.LogMessage($"Library timing: fulltext zoekscan afgerond in {searchWatch.ElapsedMilliseconds} ms; resultaten={entries.Count}.");
        }
        else
        {
            entries = allEntries
                .Where(entry => EntryMatchesLibraryMetadataFilters(entry, tagFilter, contextFilter, subcontextFilter, meetingTypeFilter))
                .ToList();
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (refreshId != _libraryRefreshVersion)
        {
            return;
        }

        _libraryListView.BeginUpdate();
        try
        {
            _libraryListView.Items.Clear();
            foreach (var entry in entries)
            {
                var status = string.IsNullOrWhiteSpace(entry.TranscriptionStatus)
                    ? entry.Status
                    : $"{entry.Status} / {entry.TranscriptionStatus}";
                var item = new ListViewItem(entry.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm"));
                item.SubItems.Add(GetLibraryDisplayTitle(entry));
                item.SubItems.Add(string.IsNullOrWhiteSpace(entry.SourceType) ? "recording" : entry.SourceType);
                item.SubItems.Add(string.IsNullOrWhiteSpace(entry.ContextName) ? "—" : entry.ContextName);
                item.SubItems.Add(string.IsNullOrWhiteSpace(entry.SubcontextName) ? "—" : entry.SubcontextName);
                item.SubItems.Add(status);
                item.SubItems.Add(entry.SearchSnippet ?? string.Empty);
                item.Tag = entry;
                _libraryListView.Items.Add(item);
                if (!string.IsNullOrWhiteSpace(currentSelectionKey)
                    && string.Equals(currentSelectionKey, entry.DirectoryPath, StringComparison.OrdinalIgnoreCase))
                {
                    item.Selected = true;
                }
            }
        }
        finally
        {
            _libraryListView.EndUpdate();
        }

        RefreshLibraryFilters(allEntries, entries);
        UpdateLibraryFilterSummary(allEntries.Count, entries.Count);
        RefreshLibraryActionStates();
        RefreshLibraryDetailPane();
        UpdateRecorderOverviewState();

        refreshWatch.Stop();
        _queueStatusLabel.Text = requiresFullTextScan
            ? $"Zoekscan afgerond: {entries.Count} sessie(s)"
            : $"Metadata geladen: {entries.Count} sessie(s)";
        AppRuntime.LogMessage($"Library timing: metadata scan {metadataWatch.ElapsedMilliseconds} ms; totaal refresh {refreshWatch.ElapsedMilliseconds} ms; alle={allEntries.Count}; zichtbaar={entries.Count}; fulltext={requiresFullTextScan}.");
    }

    private static bool EntryMatchesLibraryMetadataFilters(
        TranscriptLibraryEntry entry,
        string? tagFilter,
        string? contextFilter,
        string? subcontextFilter,
        string? meetingTypeFilter)
    {
        var activeTag = (tagFilter ?? string.Empty).Trim();
        var activeContext = (contextFilter ?? string.Empty).Trim();
        var activeSubcontext = (subcontextFilter ?? string.Empty).Trim();
        var activeMeetingType = (meetingTypeFilter ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(activeContext)
            && !string.Equals(entry.ContextName, activeContext, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(activeSubcontext)
            && !string.Equals(entry.SubcontextName, activeSubcontext, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(activeMeetingType)
            && !string.Equals(entry.MeetingType, activeMeetingType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(activeTag)
            && !entry.Tags.Any(x => string.Equals(x, activeTag, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private static string GetLibraryDisplayTitle(TranscriptLibraryEntry entry)
        => !string.IsNullOrWhiteSpace(entry.DisplayTitle)
            ? entry.DisplayTitle
            : !string.IsNullOrWhiteSpace(entry.CalendarSubject)
                ? entry.CalendarSubject
                : entry.MeetingId;

    // Tijdens een actie vanaf de pagina Overleggen wijst de selectie naar het daar gekozen overleg.
    private TranscriptLibraryEntry? GetSelectedLibraryEntry()
        => _libraryEntryOverride ?? (_libraryListView.SelectedItems.Count > 0 ? _libraryListView.SelectedItems[0].Tag as TranscriptLibraryEntry : null);

    private List<TranscriptLibraryEntry> GetSelectedLibraryEntries()
        => _libraryEntryOverride is { } chosen ? new List<TranscriptLibraryEntry> { chosen } : _libraryListView.SelectedItems.Cast<ListViewItem>()
            .Select(item => item.Tag as TranscriptLibraryEntry)
            .Where(entry => entry is not null)
            .Cast<TranscriptLibraryEntry>()
            .ToList();
    private void OpenSelectedLibrarySessionFolder()
    {
        var entry = GetSelectedLibraryEntry();
        if (entry is null)
        {
            ShowOwnedMessage(this, "Selecteer eerst een sessie in de bibliotheek.", "Geen sessie geselecteerd", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = entry.DirectoryPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowOwnedMessage(this, ex.Message, "Sessiemap openen mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private void OpenSelectedLibraryTranscript()
    {
        var entry = GetSelectedLibraryEntry();
        if (entry is null)
        {
            ShowOwnedMessage(this, "Selecteer eerst een sessie in de bibliotheek.", "Geen sessie geselecteerd", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var candidate = File.Exists(entry.TranscriptTextPath) ? entry.TranscriptTextPath : entry.TranscriptJsonPath;
        if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
        {
            ShowOwnedMessage(this, "Voor deze sessie is nog geen transcriptbestand beschikbaar.", "Transcript ontbreekt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = candidate,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowOwnedMessage(this, ex.Message, "Transcript openen mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private void ShowTranscriptReadyActionsDialog(SessionManifest manifest)
    {
        if (IsDisposed || string.IsNullOrWhiteSpace(manifest.MeetingId) || !_appSettings.Ui.Notifications.TranscriptReady.WindowsEnabled)
        {
            return;
        }
        var sessionDirectory = manifest.OutputDirectory;
        var transcriptPath = ResolveTranscriptPath(manifest.TranscriptTextPath, manifest.TranscriptJsonPath, sessionDirectory);
        var name = MeetingDisplayName(manifest, sessionDirectory);
        var shown = WindowsToastNotificationService.TryShowTranscriptReady(name, transcriptPath, sessionDirectory);
        // Ook in de lokale geschiedenis (pagina Meldingen); de ballon bij het systeemvak alleen als de Windows-melding niet lukte.
        _ = _notificationDispatcher.NotifyAsync(NotificationEventType.TranscriptReady, "Transcript gereed", UiText.F("Transcript voor {0} is aangemaakt.", name), ToolTipIcon.Info, includeWindows: !shown);
    }

    // De naam waaronder de gebruiker het overleg kent: de gegeven titel, anders de naam van het geimporteerde bestand,
    // anders "opname van <tijd>". Nooit de mapnaam.
    private static string MeetingDisplayName(SessionManifest manifest, string? sessionDirectory)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(sessionDirectory)
                && LibrarySessionMetadataService.Load(sessionDirectory).DisplayTitle is { Length: > 0 } title)
            {
                return title;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
        }

        if (!string.IsNullOrWhiteSpace(manifest.SourceFileName))
        {
            return Path.GetFileNameWithoutExtension(manifest.SourceFileName.Trim());
        }

        return UiText.F("opname van {0}", manifest.CreatedAt.ToLocalTime().ToString("HH:mm", UiText.Culture));
    }
    private static string? ResolveTranscriptPath(string? transcriptTextPath, string? transcriptJsonPath, string? sessionDirectory)
    {
        if (!string.IsNullOrWhiteSpace(transcriptTextPath) && File.Exists(transcriptTextPath))
        {
            return transcriptTextPath;
        }
        if (!string.IsNullOrWhiteSpace(transcriptJsonPath) && File.Exists(transcriptJsonPath))
        {
            return transcriptJsonPath;
        }
        if (!string.IsNullOrWhiteSpace(sessionDirectory))
        {
            var fallbackTxt = SessionArtifactPathService.ResolveTranscriptTextPath(sessionDirectory);
            if (File.Exists(fallbackTxt))
            {
                return fallbackTxt;
            }
            var fallbackJson = SessionArtifactPathService.ResolveTranscriptJsonPath(sessionDirectory);
            if (File.Exists(fallbackJson))
            {
                return fallbackJson;
            }
        }
        return null;
    }
    private void OpenPathInShell(string path, string errorCaption)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowOwnedMessage(this, ex.Message, errorCaption, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private void CopySelectedLibraryTranscript()
    {
        var entry = GetSelectedLibraryEntry();
        if (entry is null)
        {
            ShowOwnedMessage(this, "Selecteer eerst een sessie in de bibliotheek.", "Geen sessie geselecteerd", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var text = TranscriptExportService.ReadTranscriptText(entry.DirectoryPath);
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowOwnedMessage(this, "Voor deze sessie is geen transcripttekst beschikbaar.", "Transcript ontbreekt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Clipboard.SetText(text);
        SetStatus("Transcript gekopieerd.", $"Transcript uit sessie {entry.MeetingId} staat nu op het klembord.");
    }
    private void ExportSelectedLibraryTranscript(string format)
    {
        var entry = GetSelectedLibraryEntry();
        if (entry is null)
        {
            ShowOwnedMessage(this, "Selecteer eerst een sessie in de bibliotheek.", "Geen sessie geselecteerd", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (format.Equals("txt", StringComparison.OrdinalIgnoreCase))
        {
            ExportLibraryTranscriptText(entry);
            return;
        }
        if (string.IsNullOrWhiteSpace(entry.TranscriptJsonPath) || !File.Exists(entry.TranscriptJsonPath))
        {
            ShowOwnedMessage(this, "Voor export is transcript.json vereist. Deze sessie heeft dat bestand nog niet.", "Export niet mogelijk", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = format.Equals("vtt", StringComparison.OrdinalIgnoreCase) ? "Exporteer transcript als WebVTT" : "Exporteer transcript als SRT",
            FileName = Path.GetFileNameWithoutExtension(entry.TranscriptJsonPath) + (format.Equals("vtt", StringComparison.OrdinalIgnoreCase) ? ".vtt" : ".srt"),
            Filter = format.Equals("vtt", StringComparison.OrdinalIgnoreCase) ? "WebVTT (*.vtt)|*.vtt" : "SubRip (*.srt)|*.srt",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            if (format.Equals("vtt", StringComparison.OrdinalIgnoreCase))
            {
                TranscriptExportService.ExportVtt(entry.TranscriptJsonPath!, dialog.FileName);
            }
            else
            {
                TranscriptExportService.ExportSrt(entry.TranscriptJsonPath!, dialog.FileName);
            }
            SetStatus("Transcript geëxporteerd.", $"Export opgeslagen op:{Environment.NewLine}{dialog.FileName}");
        }
        catch (Exception ex)
        {
            ShowOwnedMessage(this, ex.Message, "Export mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    // De uitgeschreven tekst als tekstbestand opslaan (een kopie van transcript.txt, of de tekst uit transcript.json).
    private void ExportLibraryTranscriptText(TranscriptLibraryEntry entry)
    {
        var text = TranscriptExportService.ReadTranscriptText(entry.DirectoryPath);
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowOwnedMessage(this, "Voor deze sessie is geen transcripttekst beschikbaar.", "Transcript ontbreekt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = UiText.T("Uitgeschreven tekst opslaan"),
            FileName = string.Join("-", GetLibraryDisplayTitle(entry).Split(Path.GetInvalidFileNameChars())) + ".txt",
            Filter = UiText.T("Tekstbestand (*.txt)|*.txt"),
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            File.WriteAllText(dialog.FileName, text, Encoding.UTF8);
            SetStatus("Uitgeschreven tekst opgeslagen.", $"Opgeslagen op:{Environment.NewLine}{dialog.FileName}");
        }
        catch (Exception ex)
        {
            ShowOwnedMessage(this, ex.Message, "Opslaan mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Opnieuw uitschrijven kan bij een afgeronde, onderbroken of mislukte opname met audio waar de worker nu niet mee bezig is.
    private bool CanRetranscribe(TranscriptLibraryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.DirectoryPath) || !File.Exists(Path.Combine(entry.DirectoryPath, "audio_mix.wav")))
        {
            return false;
        }
        if ((_session is not null || _stopFinalizeInProgress) && _manifest is not null && string.Equals(_manifest.OutputDirectory?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), entry.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var transcriptionStatus = (entry.TranscriptionStatus ?? string.Empty).Trim().ToLowerInvariant();
        return SessionRecoveryService.IsRetranscribableStatus(entry.Status)
            && transcriptionStatus is not "started" and not "processing" and not "running"
            && !File.Exists(Path.Combine(entry.DirectoryPath, ".transcription.claim"));
    }

    private async Task RetranscribeSelectedLibrarySessionAsync()
    {
        var entry = GetSelectedLibraryEntry();
        if (entry is null)
        {
            ShowOwnedMessage(this, "Selecteer eerst een sessie in de bibliotheek.", "Geen sessie geselecteerd", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!CanRetranscribe(entry))
        {
            ShowOwnedMessage(this, "Deze opname kan nu niet opnieuw worden uitgeschreven: ze loopt nog, de audio ontbreekt of ze wordt op dit moment uitgeschreven.", "Opnieuw uitschrijven", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var hasTranscript = File.Exists(SessionArtifactPathService.ResolveTranscriptTextPath(entry.DirectoryPath)) || File.Exists(SessionArtifactPathService.ResolveTranscriptJsonPath(entry.DirectoryPath));
        if (hasTranscript && ShowOwnedMessage(this, $"'{MeetingListService.Title(entry)}' opnieuw uitschrijven?{Environment.NewLine}{Environment.NewLine}De huidige tekst blijft als reservekopie in de map van de opname staan.", "Opnieuw uitschrijven", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        string reason;
        bool requeued;
        try
        {
            requeued = SessionRecoveryService.TryRequeueTranscription(entry.DirectoryPath, out reason);
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Opnieuw uitschrijven klaarzetten mislukt", ex);
            requeued = false;
            reason = ex.Message;
        }
        if (!requeued)
        {
            ShowOwnedMessage(this, reason, "Opnieuw uitschrijven", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        SetStatus("Opnieuw uitschrijven klaargezet.", $"{GetLibraryDisplayTitle(entry)} wordt opnieuw uitgeschreven zodra de worker zover is.");
        EnsureWorkerProcessState();
        await PollBackgroundStateAsync();
    }

    private async Task DeleteSelectedLibrarySessionAsync()
    {
        var entry = GetSelectedLibraryEntry();
        if (entry is null)
        {
            ShowOwnedMessage(this, "Selecteer eerst een sessie in de bibliotheek.", "Geen sessie geselecteerd", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!Directory.Exists(entry.DirectoryPath))
        {
            ShowOwnedMessage(this, "De sessiemap bestaat niet meer op schijf.", "Sessiemap ontbreekt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await RunLibraryUiActionAsync("Bibliotheek vernieuwen", ct => RefreshLibraryAsync(ct));
            return;
        }
        if (!CanDeleteLibraryEntry(entry, out var reason))
        {
            ShowOwnedMessage(this, reason, "Verwijderen niet toegestaan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var folderName = Path.GetFileName(entry.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var confirmation = ShowOwnedMessage(this, $"Weet je zeker dat je '{GetLibraryDisplayTitle(entry)}' ({entry.CreatedAt.LocalDateTime:d-M-yyyy HH:mm}, map {folderName}) naar de Prullenbak wilt verplaatsen?{Environment.NewLine}{Environment.NewLine}De opname, het transcript en de andere bestanden van dit overleg gaan mee naar de Prullenbak; daar kun je ze nog terugzetten.", "Overleg verwijderen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }
        try
        {
            FileSystem.DeleteDirectory(entry.DirectoryPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
            SetStatus("Sessiemap verplaatst naar Prullenbak.", $"Verwijderd: {folderName}");
            await RunLibraryUiActionAsync("Bibliotheek vernieuwen", ct => RefreshLibraryAsync(ct));
            await PollBackgroundStateAsync();
        }
        catch (OperationCanceledException)
        {
            SetStatus("Verwijderen geannuleerd.", $"Sessiemap {folderName} is niet verwijderd.");
        }
        catch (Exception ex)
        {
            ShowOwnedMessage(this, ex.Message, "Sessiemap verwijderen mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private bool CanDeleteLibraryEntry(TranscriptLibraryEntry entry, out string reason)
    {
        if ((_session is not null || _stopFinalizeInProgress) && _manifest is not null && string.Equals(_manifest.OutputDirectory?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), entry.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            reason = "De momenteel actieve sessie kan niet worden verwijderd.";
            return false;
        }
        var recorderStatus = (entry.Status ?? string.Empty).Trim().ToLowerInvariant();
        var transcriptionStatus = (entry.TranscriptionStatus ?? string.Empty).Trim().ToLowerInvariant();
        var readyMarker = Path.Combine(entry.DirectoryPath, "READY");
        var claimMarker = Path.Combine(entry.DirectoryPath, ".transcription.claim");
        if (File.Exists(claimMarker) || transcriptionStatus is "started" or "processing")
        {
            reason = "Deze sessie wordt op dit moment verwerkt door de worker en kan daarom niet worden verwijderd.";
            return false;
        }
        if (File.Exists(readyMarker) && transcriptionStatus is not "completed" and not "failed" and not "error")
        {
            reason = "Deze sessie staat nog in de wachtrij voor transcriptie en kan daarom niet worden verwijderd.";
            return false;
        }
        if (recorderStatus is not "completed" and not "failed" and not "error")
        {
            reason = "Alleen afgeronde of gefaalde sessies kunnen vanuit de bibliotheek worden verwijderd.";
            return false;
        }
        reason = string.Empty;
        return true;
    }
    private async Task ImportAudioFromDialogAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = UiText.T("Audio of opname importeren"),
            Filter = UiText.T("Ondersteunde bestanden|*.m4a;*.mp3;*.wav;*.mp4;*.m4v;*.aac|Alle bestanden|*.*"),
            Multiselect = false,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        var manifest = await Task.Run(() => ImportSessionService.ImportAudio(dialog.FileName, _appSettings, fromWatchFolder: false, consumeSource: false));
        SetStatus("Audio geïmporteerd.", $"Importsessie aangemaakt:{Environment.NewLine}{manifest.OutputDirectory}{Environment.NewLine}READY marker is geschreven; de worker kan de transcriptie nu oppakken.");
        await RunLibraryUiActionAsync("Bibliotheek vernieuwen", ct => RefreshLibraryAsync(ct));
        await PollBackgroundStateAsync();
    }
    private void ShowWatchFoldersDialog()
    {
        if (!IsFeatureEnabled(EditionFeatureCatalog.WatchFolders))
        {
            ShowPremiumFeatureRequiredMessage("Watch folders");
            return;
        }
        using var dialog = new Form
        {
            Text = $"{Branding.DisplayName} · Watch folders",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(760, 520),
            MinimumSize = new Size(680, 460),
            BackColor = Color.White,
            Font = Font,
        };
        var listBox = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var folder in _appSettings.Worker.Import.WatchFolders)
        {
            listBox.Items.Add(folder);
        }
        var requestedWatchFolderEnabled = _appSettings.Worker.Import.RequestedWatchFolderEnabled || _appSettings.Worker.Import.WatchFolderEnabled;
        var enableCheck = new CheckBox { Text = "Watch folder verwerking inschakelen", AutoSize = true, Checked = requestedWatchFolderEnabled };
        var consumeCheck = new CheckBox { Text = "Bronbestanden na succesvolle ingest uit de watch folder verwijderen", AutoSize = true, Checked = _appSettings.Worker.Import.ConsumeWatchFiles };
        var stableAge = new NumericUpDown { Minimum = 5, Maximum = 3600, Value = _appSettings.Worker.Import.MinimumStableAgeSeconds, Width = 100 };
        var addButton = new Button { Text = "Map toevoegen", Width = 120, Height = 32 };
        var removeButton = new Button { Text = "Verwijderen", Width = 120, Height = 32 };
        var okButton = new Button { Text = "Opslaan", Width = 120, Height = 34, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Annuleren", Width = 120, Height = 34, DialogResult = DialogResult.Cancel };
        StyleButton(addButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(removeButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(okButton, AccentColor, AccentHoverColor, Color.White);
        StyleButton(cancelButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        addButton.Click += (_, _) =>
        {
            using var browser = new FolderBrowserDialog { Description = UiText.T("Kies een watch folder") };
            if (browser.ShowDialog(dialog) == DialogResult.OK && !string.IsNullOrWhiteSpace(browser.SelectedPath))
            {
                if (!listBox.Items.Contains(browser.SelectedPath))
                {
                    listBox.Items.Add(browser.SelectedPath);
                }
            }
        };
        removeButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is not null)
            {
                listBox.Items.Remove(listBox.SelectedItem);
            }
        };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14), AutoSize = false };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(enableCheck, 0, 0);
        root.Controls.Add(consumeCheck, 0, 1);
        var stablePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        stablePanel.Controls.Add(new Label { Text = "Minimale stabiele leeftijd (sec)", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
        stablePanel.Controls.Add(stableAge);
        root.Controls.Add(stablePanel, 0, 2);
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        center.Controls.Add(listBox, 0, 0);
        var rightButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(10, 0, 0, 0) };
        rightButtons.Controls.Add(addButton);
        rightButtons.Controls.Add(removeButton);
        center.Controls.Add(rightButtons, 1, 0);
        root.Controls.Add(center, 0, 3);
        var bottom = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Top };
        bottom.Controls.Add(okButton);
        bottom.Controls.Add(cancelButton);
        root.Controls.Add(bottom, 0, 4);
        dialog.Controls.Add(root);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        _appSettings.Worker.Import.RequestedWatchFolderEnabled = enableCheck.Checked;
        _appSettings.Worker.Import.WatchFolderEnabled = enableCheck.Checked && IsFeatureEnabled(EditionFeatureCatalog.WatchFolders);
        _appSettings.Worker.Import.ConsumeWatchFiles = consumeCheck.Checked;
        _appSettings.Worker.Import.MinimumStableAgeSeconds = (int)stableAge.Value;
        _appSettings.Worker.Import.WatchFolders = listBox.Items.Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        SettingsStore.Save(_appSettings);
        EnsureWorkerProcessState();
        SetStatus("Watch folders bijgewerkt.", $"Aantal watch folders: {_appSettings.Worker.Import.WatchFolders.Count}");
    }
    private void ShowVocabularyDialog()
    {
        var initial = string.Join(Environment.NewLine, _appSettings.Worker.CustomVocabulary.Select(x => x.Variants is { Count: > 0 }
            ? $"{x.Canonical} = {string.Join(", ", x.Variants)}"
            : x.Canonical));
        using var dialog = new Form
        {
            Text = $"{Branding.DisplayName} · Custom vocabulary",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(760, 560),
            MinimumSize = new Size(680, 480),
            BackColor = Color.White,
            Font = Font,
        };
        var info = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            Margin = new Padding(0, 0, 0, 10),
            Text = "Voer per regel één term in. Optioneel kun je varianten opgeven als: Canonical = variant 1, variant 2. Deze lijst wordt als contextprompt naar de transcriptieroute gestuurd en is geen harde garantie op herkenning."
        };
        var box = new TextBox { Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = initial };
        var countLabel = new Label { AutoSize = true, Text = $"Aantal termen: {_appSettings.Worker.CustomVocabulary.Count}", Margin = new Padding(0, 8, 0, 0) };
        void RefreshCount() => countLabel.Text = $"Aantal termen: {ParseVocabulary(box.Text).Count}";
        box.TextChanged += (_, _) => RefreshCount();
        var importButton = new Button { Text = "Importeren", Width = 120, Height = 32 };
        var exportButton = new Button { Text = "Exporteren", Width = 120, Height = 32 };
        var okButton = new Button { Text = "Opslaan", Width = 120, Height = 34, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Annuleren", Width = 120, Height = 34, DialogResult = DialogResult.Cancel };
        StyleButton(importButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(exportButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(okButton, AccentColor, AccentHoverColor, Color.White);
        StyleButton(cancelButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        importButton.Click += (_, _) =>
        {
            using var openDialog = new OpenFileDialog { Filter = UiText.T("Tekstbestanden (*.txt;*.md)|*.txt;*.md|Alle bestanden|*.*"), CheckFileExists = true };
            if (openDialog.ShowDialog(dialog) == DialogResult.OK)
            {
                box.Text = File.ReadAllText(openDialog.FileName, Encoding.UTF8);
            }
        };
        exportButton.Click += (_, _) =>
        {
            using var saveDialog = new SaveFileDialog { Filter = UiText.T("Tekstbestand (*.txt)|*.txt"), FileName = "captivox_custom_vocabulary.txt", AddExtension = true, OverwritePrompt = true };
            if (saveDialog.ShowDialog(dialog) == DialogResult.OK)
            {
                File.WriteAllText(saveDialog.FileName, box.Text, Encoding.UTF8);
            }
        };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(info, 0, 0);
        root.Controls.Add(countLabel, 0, 1);
        root.Controls.Add(box, 0, 2);
        var bottom = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Top };
        bottom.Controls.Add(okButton);
        bottom.Controls.Add(cancelButton);
        bottom.Controls.Add(exportButton);
        bottom.Controls.Add(importButton);
        root.Controls.Add(bottom, 0, 3);
        dialog.Controls.Add(root);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        _appSettings.Worker.CustomVocabulary = ParseVocabulary(box.Text);
        SettingsStore.Save(_appSettings);
        SetStatus("Custom vocabulary bijgewerkt.", $"Aantal termen: {_appSettings.Worker.CustomVocabulary.Count}");
    }
    private static List<CustomVocabularyEntry> ParseVocabulary(string raw)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }
            var parts = trimmed.Split('=', 2, StringSplitOptions.TrimEntries);
            var canonical = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(canonical))
            {
                continue;
            }
            if (!map.TryGetValue(canonical, out var variants))
            {
                variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[canonical] = variants;
            }
            if (parts.Length > 1)
            {
                foreach (var variant in parts[1]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    if (!string.Equals(variant, canonical, StringComparison.OrdinalIgnoreCase))
                    {
                        variants.Add(variant);
                    }
                }
            }
        }
        return map
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new CustomVocabularyEntry
            {
                Canonical = x.Key,
                Variants = x.Value.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList()
            })
            .ToList();
    }
}
