using System.Diagnostics;
using Captivox.Recorder.Controls;
using Captivox.Recorder.Models;
using Captivox.Recorder.Services;

namespace Captivox.Recorder;

internal sealed class MainForm : Form
{
    private const int HotkeyId = 0x4356;
    private static readonly Color Ink = Color.FromArgb(31, 41, 55);
    private static readonly Color Muted = Color.FromArgb(100, 116, 139);
    private static readonly Color Surface = Color.FromArgb(246, 248, 251);
    private static readonly Color Danger = Color.FromArgb(185, 28, 28);
    private static readonly System.Globalization.CultureInfo Dutch = System.Globalization.CultureInfo.GetCultureInfo("nl-NL");

    private readonly bool _launchMinimized;
    private readonly WorkerProcessManager _worker = new();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private readonly Stopwatch _recordingClock = new();
    private ApplicationSettings _settings;

    private AudioCaptureSession? _session;
    private SessionManifest? _manifest;
    private bool _stopInProgress;
    private bool _closeAfterStop;
    private bool _reallyExit;
    private bool _hotkeyRegistered;
    private DateTimeOffset _lastSystemSignalAt;
    private DateTimeOffset _lastMicSignalAt;
    private int _tickCount;
    private string _lastLibrarySignature = string.Empty;
    private string? _shownTranscriptDirectory;
    private DateTime _settingsStamp;

    private readonly ComboBox _micCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly ComboBox _systemCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly LinkLabel _refreshDevicesLink = new() { Text = "Apparaten opnieuw zoeken", AutoSize = true };
    private readonly LevelMeterControl _micMeter = new() { Width = 300 };
    private readonly LevelMeterControl _systemMeter = new() { Width = 300 };
    private readonly PrimaryActionButton _recordButton = new() { Width = 300, Height = 52 };
    private readonly Label _elapsedLabel = new() { Text = "00:00:00", AutoSize = true, Font = new Font("Segoe UI", 20F, FontStyle.Bold), ForeColor = Ink };
    private readonly Label _statusLabel = new() { AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = Ink };
    private readonly Button _importButton = new() { Text = "Audiobestand toevoegen...", Width = 300, Height = 32 };
    private readonly Label _workerLabel = new() { AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = Muted };

    private readonly ListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle,
    };
    private readonly TextBox _transcriptBox = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        BackColor = Color.White,
        Font = new Font("Segoe UI", 10F),
        BorderStyle = BorderStyle.FixedSingle,
    };
    // Vaste hoogte met ... aan het eind: een lang pad mag het transcriptvak niet breder maken dan het venster.
    private readonly Label _detailLabel = new() { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Height = 26, ForeColor = Muted, Padding = new Padding(0, 4, 0, 4) };
    private readonly Button _openFolderButton = new() { Text = "Map openen", AutoSize = true };
    private readonly Button _copyButton = new() { Text = "Tekst kopieren", AutoSize = true };
    private readonly Button _saveAsButton = new() { Text = "Opslaan als...", AutoSize = true };
    private readonly Button _renameButton = new() { Text = "Naam geven...", AutoSize = true };
    private readonly Button _retranscribeButton = new() { Text = "Opnieuw uitschrijven", AutoSize = true };
    private readonly Button _deleteButton = new() { Text = "Naar prullenbak", AutoSize = true };

    private readonly NotifyIcon _trayIcon = new();
    private readonly ToolStripMenuItem _trayRecordItem = new("Opname starten");
    private readonly ToolStripMenuItem _trayShowItem = new("Venster tonen");

    public MainForm(bool launchMinimized)
    {
        _launchMinimized = launchMinimized;
        _settings = SettingsStore.Load();
        SaveSettings();

        Text = $"{Branding.DisplayName} {AppRuntime.GetProductVersionTag()}";
        Icon = Branding.GetAppIconOrDefault();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 620);
        Size = new Size(1180, 760);
        BackColor = Surface;
        Font = new Font("Segoe UI", 9.5F);
        KeyPreview = true;

        BuildLayout();
        BuildTray();
        WireEvents();
        SetRunningState(false);
    }

    private void BuildLayout()
    {
        var menu = new MenuStrip { BackColor = Color.White };
        var fileMenu = new ToolStripMenuItem("Bestand");
        fileMenu.DropDownItems.Add("Audiobestand toevoegen...", null, async (_, _) => await ImportAudioAsync());
        fileMenu.DropDownItems.Add("Opslagmap openen", null, (_, _) => OpenFolder(_settings.Ui.OutputRoot));
        fileMenu.DropDownItems.Add("Instellingen...", null, (_, _) => ShowSettings());
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("Afsluiten", null, (_, _) => ExitApplication());
        var helpMenu = new ToolStripMenuItem("Help");
        helpMenu.DropDownItems.Add("Handleiding op GitHub", null, (_, _) => OpenUrl(Branding.ProjectUrl));
        helpMenu.DropDownItems.Add("Logbestanden openen", null, (_, _) => OpenFolder(AppRuntime.GetLogDirectory()));
        helpMenu.DropDownItems.Add("Over Captivox Recorder", null, (_, _) => ShowAbout());
        menu.Items.Add(fileMenu);
        menu.Items.Add(helpMenu);
        MainMenuStrip = menu;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(16), BackColor = Surface };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildRecordPanel(), 0, 0);
        root.Controls.Add(BuildLibraryPanel(), 1, 0);

        Controls.Add(root);
        Controls.Add(menu);
    }

    private Control BuildRecordPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 0, 16, 0),
        };

        var header = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(0, 0, 0, 12), WrapContents = false };
        header.Controls.Add(new PictureBox { Image = Branding.CreateHeaderLogoImage(96), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(44, 44), Margin = new Padding(0, 0, 10, 0) });
        var titleStack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        titleStack.Controls.Add(new Label { Text = Branding.DisplayName, AutoSize = true, Font = new Font("Segoe UI", 14F, FontStyle.Bold), ForeColor = Ink, Margin = new Padding(0) });
        titleStack.Controls.Add(new Label { Text = "Opnemen en uitschrijven op je eigen pc", AutoSize = true, ForeColor = Muted, Margin = new Padding(0) });
        header.Controls.Add(titleStack);
        panel.Controls.Add(header);

        panel.Controls.Add(SectionLabel("Microfoon (jouw stem)"));
        panel.Controls.Add(_micCombo);
        panel.Controls.Add(_micMeter);
        panel.Controls.Add(SectionLabel("Geluid van de pc (de anderen in het gesprek)"));
        panel.Controls.Add(_systemCombo);
        panel.Controls.Add(_systemMeter);
        _refreshDevicesLink.Margin = new Padding(0, 0, 0, 14);
        panel.Controls.Add(_refreshDevicesLink);

        _recordButton.Margin = new Padding(0, 0, 0, 8);
        panel.Controls.Add(_recordButton);
        panel.Controls.Add(_elapsedLabel);
        _statusLabel.Margin = new Padding(0, 4, 0, 16);
        panel.Controls.Add(_statusLabel);

        _importButton.Margin = new Padding(0, 0, 0, 16);
        panel.Controls.Add(_importButton);
        panel.Controls.Add(SectionLabel("Uitschrijven"));
        panel.Controls.Add(_workerLabel);
        return panel;
    }

    private Control BuildLibraryPanel()
    {
        _list.Columns.Add("Naam", 300);
        _list.Columns.Add("Datum", 150);
        _list.Columns.Add("Duur", 80);
        _list.Columns.Add("Stand", 170);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 6, 0, 0) };
        buttons.Controls.AddRange(new Control[] { _openFolderButton, _copyButton, _saveAsButton, _renameButton, _retranscribeButton, _deleteButton });

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        top.Controls.Add(SectionLabel("Opnames"), 0, 0);
        top.Controls.Add(_list, 0, 1);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottom.Controls.Add(buttons, 0, 0);
        bottom.Controls.Add(_detailLabel, 0, 1);
        bottom.Controls.Add(_transcriptBox, 0, 2);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, BackColor = Surface };
        split.Panel1.Controls.Add(top);
        split.Panel2.Controls.Add(bottom);
        split.HandleCreated += (_, _) => split.SplitterDistance = Math.Max(160, split.Height / 3);
        return split;
    }

    private static Label SectionLabel(string text)
        => new() { Text = text, AutoSize = true, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), ForeColor = Ink, Margin = new Padding(0, 6, 0, 4) };

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_trayRecordItem);
        menu.Items.Add(_trayShowItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Afsluiten", null, (_, _) => ExitApplication());
        _trayRecordItem.Click += async (_, _) => await ToggleRecordingAsync();
        _trayShowItem.Click += (_, _) => ToggleWindow();
        _trayIcon.Icon = Branding.GetAppIconOrDefault();
        _trayIcon.Text = Branding.DisplayName;
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void WireEvents()
    {
        _recordButton.Click += async (_, _) => await ToggleRecordingAsync();
        _refreshDevicesLink.LinkClicked += (_, _) => LoadDevices();
        _importButton.Click += async (_, _) => await ImportAudioAsync();
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        _list.DoubleClick += (_, _) => { if (SelectedEntry() is { } e) OpenFolder(e.Directory); };
        _openFolderButton.Click += (_, _) => { if (SelectedEntry() is { } e) OpenFolder(e.Directory); };
        _copyButton.Click += (_, _) => CopyTranscript();
        _saveAsButton.Click += (_, _) => SaveTranscriptAs();
        _renameButton.Click += (_, _) => RenameSelected();
        _retranscribeButton.Click += (_, _) => RetranscribeSelected();
        _deleteButton.Click += (_, _) => DeleteSelected();
        _micCombo.SelectedIndexChanged += (_, _) => RememberDevices();
        _systemCombo.SelectedIndexChanged += (_, _) => RememberDevices();
        _tick.Tick += (_, _) => OnTick();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LoadDevices();
        RefreshLibrary(force: true);
        UpdateWorkerLabel();
        _worker.EnsureRunning(_settings);
        _tick.Start();
        _hotkeyRegistered = NativeMethods.RegisterHotKey(Handle, HotkeyId, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, NativeMethods.VK_F9);
        SetStatus(_hotkeyRegistered ? "Klaar om op te nemen. Sneltoets: Ctrl+Shift+F9." : "Klaar om op te nemen.");
        if (_launchMinimized)
        {
            BeginInvoke(new Action(Hide));
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            _ = ToggleRecordingAsync();
            return;
        }

        base.WndProc(ref m);
    }

    // ---- Apparaten ----

    private void LoadDevices()
    {
        try
        {
            var mics = DeviceCatalog.GetCaptureDevices();
            var renders = DeviceCatalog.GetRenderDevices();
            FillDeviceCombo(_micCombo, mics, _settings.Ui.PreferredMicDeviceId);
            FillDeviceCombo(_systemCombo, renders, _settings.Ui.PreferredSystemDeviceId);
            if (mics.Count == 0 || renders.Count == 0)
            {
                SetStatus(mics.Count == 0 ? "Geen microfoon gevonden. Sluit er een aan en kies Apparaten opnieuw zoeken." : "Geen afspeelapparaat gevonden.", isError: true);
            }
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Apparaten laden mislukt", ex);
            SetStatus("De geluidsapparaten konden niet worden gelezen: " + ex.Message, isError: true);
        }

        SetRunningState(_session is not null);
    }

    private static void FillDeviceCombo(ComboBox combo, IReadOnlyList<DeviceInfo> devices, string? preferredId)
    {
        combo.BeginUpdate();
        combo.Items.Clear();
        foreach (var device in devices)
        {
            combo.Items.Add(device);
        }

        var preferred = devices.FirstOrDefault(d => d.Id == preferredId) ?? devices.FirstOrDefault(d => d.IsDefault) ?? devices.FirstOrDefault();
        combo.SelectedItem = preferred;
        combo.EndUpdate();
    }

    private void RememberDevices()
    {
        if (_micCombo.SelectedItem is DeviceInfo mic)
        {
            _settings.Ui.PreferredMicDeviceId = mic.Id;
        }

        if (_systemCombo.SelectedItem is DeviceInfo system)
        {
            _settings.Ui.PreferredSystemDeviceId = system.Id;
        }

        SaveSettingsQuietly();
    }

    // ---- Opnemen ----

    private async Task ToggleRecordingAsync()
    {
        if (_stopInProgress)
        {
            return;
        }

        if (_session is null)
        {
            await StartRecordingAsync();
        }
        else
        {
            await StopRecordingAsync();
        }
    }

    private async Task StartRecordingAsync()
    {
        if (_micCombo.SelectedItem is not DeviceInfo mic || _systemCombo.SelectedItem is not DeviceInfo system)
        {
            SetStatus("Kies eerst een microfoon en het geluid van de pc.", isError: true);
            return;
        }

        _recordButton.Enabled = false;
        try
        {
            var outputRoot = _settings.Ui.OutputRoot;
            var preflight = await Task.Run(() => PreflightCheckService.Run(outputRoot, mic.Id, system.Id));
            if (!preflight.Success)
            {
                SetStatus("Opnemen kan nu niet.", isError: true);
                MessageBox.Show(this, preflight.Details, "Opnemen kan nu niet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var options = new AppOptions
            {
                OutputRoot = outputRoot,
                MicDeviceId = mic.Id,
                SystemDeviceId = system.Id,
                WriteStems = false,
                CreateReadyMarker = true,
                DiagnosticsMode = _settings.Ui.DiagnosticsMode,
                MicGain = _settings.Ui.MicGain,
                SystemGain = _settings.Ui.SystemGain,
                DeviceProfile = AudioDeviceRiskAnalyzer.DetermineDeviceProfile(mic.FriendlyName, system.FriendlyName),
                SameDeviceRisk = AudioDeviceRiskAnalyzer.IsSamePhysicalDeviceRisk(mic.FriendlyName, system.FriendlyName),
            };

            var session = new AudioCaptureSession(options);
            session.LevelsUpdated += OnLevelsUpdated;
            _session = session;
            _manifest = await session.StartAsync();
            _recordingClock.Restart();
            _lastMicSignalAt = DateTimeOffset.Now;
            _lastSystemSignalAt = DateTimeOffset.Now;
            SetRunningState(true);
            var warning = preflight.HasWarnings ? " Let op: " + string.Join(" ", preflight.Lines.Where(l => l.StartsWith("- ", StringComparison.Ordinal)).Select(l => l[2..])) : string.Empty;
            SetStatus("De opname loopt." + warning);
            _trayIcon.ShowBalloonTip(2000, Branding.DisplayName, "Opname gestart.", ToolTipIcon.Info);
            RefreshLibrary(force: true);
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Opname starten mislukt", ex);
            if (_session is not null)
            {
                _session.LevelsUpdated -= OnLevelsUpdated;
                _session.Dispose();
            }

            _session = null;
            _manifest = null;
            _recordingClock.Reset();
            SetRunningState(false);
            SetStatus("De opname kon niet starten: " + ex.Message, isError: true);
        }
        finally
        {
            _recordButton.Enabled = !_stopInProgress;
        }
    }

    private async Task StopRecordingAsync()
    {
        if (_session is null || _stopInProgress)
        {
            return;
        }

        var session = _session;
        _session = null;
        _stopInProgress = true;
        session.LevelsUpdated -= OnLevelsUpdated;
        _recordingClock.Stop();
        ResetMeters();
        SetRunningState(false);
        SetStatus("De opname wordt afgerond (geluid samenvoegen)...");

        SessionManifest? completed = null;
        Exception? failure = null;
        try
        {
            completed = await session.StopAsync();
        }
        catch (Exception ex)
        {
            failure = ex;
            AppRuntime.LogException("Opname stoppen mislukt", ex);
        }

        _stopInProgress = false;
        _recordingClock.Reset();
        UpdateElapsed();
        SetRunningState(false);
        if (failure is null && completed is not null)
        {
            _manifest = completed;
            SetStatus(_settings.Worker.WhisperXEnabled
                ? "Opname bewaard. Hij wordt nu op de achtergrond uitgeschreven."
                : "Opname bewaard.");
            _trayIcon.ShowBalloonTip(2000, Branding.DisplayName, "Opname bewaard.", ToolTipIcon.Info);
            _worker.EnsureRunning(_settings);
        }
        else
        {
            SetStatus("Het afronden van de opname is mislukt: " + (failure?.Message ?? "onbekende fout") + " Wat al was opgenomen staat in de opnamemap.", isError: true);
        }

        RefreshLibrary(force: true);
        if (_closeAfterStop)
        {
            _closeAfterStop = false;
            _reallyExit = true;
            Close();
        }
    }

    private void OnLevelsUpdated(object? sender, CaptureLevelsEventArgs e)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(new Action(() =>
        {
            _micMeter.Value = e.MicPeak;
            _systemMeter.Value = e.SystemPeak;
            if (e.MicPeak > 0.01f)
            {
                _lastMicSignalAt = DateTimeOffset.Now;
            }

            if (e.SystemPeak > 0.005f)
            {
                _lastSystemSignalAt = DateTimeOffset.Now;
            }
        }));
    }

    private void ResetMeters()
    {
        _micMeter.Value = 0;
        _systemMeter.Value = 0;
    }

    private void UpdateElapsed()
        => _elapsedLabel.Text = _recordingClock.Elapsed.ToString(@"hh\:mm\:ss");

    private void SetRunningState(bool running)
    {
        if (running)
        {
            _recordButton.Text = "Opname stoppen";
            _recordButton.VisualState = PrimaryActionVisualState.Stop;
        }
        else if (_stopInProgress)
        {
            _recordButton.Text = "Opname afronden...";
            _recordButton.VisualState = PrimaryActionVisualState.Busy;
        }
        else
        {
            _recordButton.Text = "Opname starten";
            _recordButton.VisualState = PrimaryActionVisualState.Start;
        }

        _recordButton.Enabled = !_stopInProgress && (running || (_micCombo.SelectedItem is not null && _systemCombo.SelectedItem is not null));
        _recordButton.AccessibleName = _recordButton.Text;
        _micCombo.Enabled = !running && !_stopInProgress;
        _systemCombo.Enabled = !running && !_stopInProgress;
        _refreshDevicesLink.Enabled = !running && !_stopInProgress;
        _trayRecordItem.Text = _recordButton.Text;
        _trayRecordItem.Enabled = _recordButton.Enabled;
        _trayIcon.Text = running ? $"{Branding.DisplayName} - opname loopt" : Branding.DisplayName;
    }

    private void SetStatus(string text, bool isError = false)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = isError ? Danger : Ink;
    }

    // ---- Elke seconde: tijd, bewaking van de opname, worker en lijst ----

    private void OnTick()
    {
        _tickCount++;
        if (_session is not null)
        {
            UpdateElapsed();
            WatchRecordingHealth(_session);
        }

        if (_tickCount % 10 == 0)
        {
            ReloadSettingsIfChangedElsewhere();
            _worker.EnsureRunning(_settings);
            UpdateWorkerLabel();
        }

        if (_tickCount % 4 == 0)
        {
            RefreshLibrary(force: false);
        }
    }

    private void WatchRecordingHealth(AudioCaptureSession session)
    {
        if (session.CaptureFailed)
        {
            SetStatus("De opname is gestopt door een fout met een geluidsapparaat (" + session.CaptureFailureSummary + "). Druk op Opname stoppen om te bewaren wat er is.", isError: true);
            return;
        }

        if (session.CapacityReached)
        {
            SetStatus("De opname heeft de grootste bestandsgrootte bereikt; nieuw geluid wordt niet meer bewaard. Stop en start een nieuwe opname.", isError: true);
            return;
        }

        if (session.RemainingCapacity is TimeSpan remaining && remaining < TimeSpan.FromMinutes(15))
        {
            SetStatus($"Nog ongeveer {Math.Max(1, (int)remaining.TotalMinutes)} minuten ruimte in deze opname. Start daarna een nieuwe.", isError: true);
            return;
        }

        var now = DateTimeOffset.Now;
        if (now - _lastMicSignalAt > TimeSpan.FromMinutes(2))
        {
            SetStatus("Al twee minuten geen geluid van de microfoon. Staat hij gedempt of is het verkeerde apparaat gekozen?", isError: true);
        }
        else if (now - _lastSystemSignalAt > TimeSpan.FromMinutes(5))
        {
            SetStatus("Al vijf minuten geen geluid van de pc. Hoor je de anderen via een ander apparaat? Dat kan pas bij de volgende opname worden gekozen.");
        }
        else if (_statusLabel.ForeColor == Danger)
        {
            SetStatus("De opname loopt.");
        }
    }

    private void UpdateWorkerLabel()
    {
        var (ok, text) = PreflightCheckService.CheckTranscriptionRuntime(_settings);
        var lines = new List<string> { text };
        if (ok && _settings.Worker.WhisperXEnabled)
        {
            if (_worker.LastProblem is { } problem)
            {
                lines.Add(problem);
            }
            else if (WorkerProcessManager.ReadRuntimeState() is { } state && !string.IsNullOrWhiteSpace(state.ActiveSessionDir) && string.Equals(state.State, "processing", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("Bezig met: " + Path.GetFileName(state.ActiveSessionDir));
            }
        }

        _workerLabel.Text = string.Join(Environment.NewLine, lines);
        _workerLabel.ForeColor = ok && _worker.LastProblem is null ? Muted : Danger;
    }

    // ---- Opnamelijst ----

    private IEnumerable<string> LibraryRoots()
        => new[] { _settings.Ui.OutputRoot }.Concat(_settings.Worker.KnownSessionRoots).Distinct(StringComparer.OrdinalIgnoreCase);

    private void RefreshLibrary(bool force)
    {
        List<RecordingEntry> entries;
        try
        {
            entries = RecordingLibrary.Scan(LibraryRoots(), _manifest is not null && _session is not null ? _manifest.OutputDirectory : null);
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Opnamelijst lezen mislukt", ex);
            return;
        }

        var signature = string.Join("|", entries.Select(e => $"{e.Directory}:{e.Title}:{e.State}:{e.Duration?.TotalSeconds:0}"));
        if (!force && signature == _lastLibrarySignature)
        {
            return;
        }

        _lastLibrarySignature = signature;
        var selected = SelectedEntry()?.Directory;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var entry in entries)
        {
            var item = new ListViewItem(entry.Title) { Tag = entry };
            item.SubItems.Add(entry.StartedAt.ToString("ddd d MMM yyyy HH:mm", Dutch));
            item.SubItems.Add(entry.Duration is TimeSpan d ? (d.TotalHours >= 1 ? d.ToString(@"h\:mm\:ss") : d.ToString(@"m\:ss")) : string.Empty);
            item.SubItems.Add(entry.StatusText);
            if (entry.State is RecordingState.Failed or RecordingState.Interrupted)
            {
                item.ForeColor = Danger;
            }
            else if (entry.State != RecordingState.Done)
            {
                item.ForeColor = Muted;
            }

            _list.Items.Add(item);
            if (string.Equals(entry.Directory, selected, StringComparison.OrdinalIgnoreCase))
            {
                item.Selected = true;
            }
        }

        _list.EndUpdate();
        if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0 && selected is null)
        {
            _list.Items[0].Selected = true;
        }

        ShowSelected(reloadTranscript: true);
    }

    private RecordingEntry? SelectedEntry()
        => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as RecordingEntry : null;

    private void ShowSelected() => ShowSelected(reloadTranscript: true);

    private void ShowSelected(bool reloadTranscript)
    {
        var entry = SelectedEntry();
        var hasEntry = entry is not null;
        _openFolderButton.Enabled = hasEntry;
        _renameButton.Enabled = hasEntry;
        _deleteButton.Enabled = hasEntry && entry!.State is not (RecordingState.Recording or RecordingState.Transcribing);
        _retranscribeButton.Enabled = hasEntry && entry!.State is RecordingState.Done or RecordingState.Failed;
        if (entry is null)
        {
            _detailLabel.Text = string.Empty;
            _transcriptBox.Text = _list.Items.Count == 0 ? "Nog geen opnames. Kies links de apparaten en druk op Opname starten, of voeg een audiobestand toe." : string.Empty;
            _copyButton.Enabled = _saveAsButton.Enabled = false;
            _shownTranscriptDirectory = null;
            return;
        }

        _detailLabel.Text = entry.Problem is { Length: > 0 } problem ? $"{entry.StatusText}: {problem}" : $"{entry.StatusText} - {entry.Directory}";
        _detailLabel.ForeColor = entry.Problem is { Length: > 0 } ? Danger : Muted;
        if (!reloadTranscript && string.Equals(_shownTranscriptDirectory, entry.Directory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var transcript = entry.State == RecordingState.Done ? RecordingLibrary.ReadTranscript(entry.Directory) : null;
        // De worker schrijft regeleinden zoals Linux (\n); een Windows-tekstvak toont die alleen als \r\n.
        _transcriptBox.Text = transcript?.Replace("\r\n", "\n").Replace("\n", "\r\n") ?? entry.State switch
        {
            RecordingState.Recording => "De opname loopt nog.",
            RecordingState.Waiting => "Deze opname wacht op uitschrijven. Dat gebeurt vanzelf zodra de transcriptie-omgeving klaar is.",
            RecordingState.Transcribing => "Deze opname wordt nu uitgeschreven. Dat duurt ongeveer even lang als de opname zelf, met een videokaart vaak veel korter.",
            _ => "Er is (nog) geen transcript.",
        };
        _copyButton.Enabled = _saveAsButton.Enabled = transcript is not null;
        _shownTranscriptDirectory = entry.Directory;
    }

    private void CopyTranscript()
    {
        if (SelectedEntry() is { } entry && RecordingLibrary.ReadTranscript(entry.Directory) is { } text)
        {
            Clipboard.SetText(text);
            SetStatus("Transcript gekopieerd.");
        }
    }

    private void SaveTranscriptAs()
    {
        if (SelectedEntry() is not { } entry || RecordingLibrary.ReadTranscript(entry.Directory) is not { } text)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "Tekstbestand (*.txt)|*.txt|Ondertitels (*.srt)|*.srt|Webondertitels (*.vtt)|*.vtt",
            FileName = MakeSafeFileName(entry.Title) + ".txt",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var jsonPath = SessionArtifactPathService.ResolveTranscriptJsonPath(entry.Directory);
            switch (dialog.FilterIndex)
            {
                case 2 when File.Exists(jsonPath):
                    TranscriptExportService.ExportSrt(jsonPath, dialog.FileName);
                    break;
                case 3 when File.Exists(jsonPath):
                    TranscriptExportService.ExportVtt(jsonPath, dialog.FileName);
                    break;
                default:
                    File.WriteAllText(dialog.FileName, text, System.Text.Encoding.UTF8);
                    break;
            }

            SetStatus("Opgeslagen: " + dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Opslaan mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "transcript" : safe;
    }

    private void RenameSelected()
    {
        if (SelectedEntry() is not { } entry)
        {
            return;
        }

        var name = Prompt("Naam geven", "Nieuwe naam voor deze opname:", entry.Title);
        if (name is null)
        {
            return;
        }

        try
        {
            RecordingLibrary.Rename(entry.Directory, name);
            RefreshLibrary(force: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Naam geven mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RetranscribeSelected()
    {
        if (SelectedEntry() is not { } entry)
        {
            return;
        }

        if (MessageBox.Show(this, "Deze opname opnieuw uitschrijven met de huidige instellingen? Het huidige transcript gaat naar de submap vorige_transcripties.", "Opnieuw uitschrijven", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            RecordingLibrary.QueueRetranscription(entry.Directory);
            _worker.EnsureRunning(_settings);
            RefreshLibrary(force: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Opnieuw uitschrijven", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // Naar de prullenbak van Windows, zodat een vergissing te herstellen is.
    private void DeleteSelected()
    {
        if (SelectedEntry() is not { } entry)
        {
            return;
        }

        if (MessageBox.Show(this, $"\"{entry.Title}\" met geluid en transcript naar de prullenbak verplaatsen?", "Naar prullenbak", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(entry.Directory, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            RefreshLibrary(force: true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Verplaatsen mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ImportAudioAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Audiobestand toevoegen",
            Filter = "Geluid en video (*.wav;*.mp3;*.m4a;*.aac;*.mp4;*.m4v)|*.wav;*.mp3;*.m4a;*.aac;*.mp4;*.m4v|Alle bestanden (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _importButton.Enabled = false;
        SetStatus("Bestand wordt toegevoegd...");
        try
        {
            var settings = _settings;
            var source = dialog.FileName;
            await Task.Run(() => ImportSessionService.ImportAudio(source, settings));
            SetStatus("Bestand toegevoegd. Het wordt op de achtergrond uitgeschreven.");
            _worker.EnsureRunning(_settings);
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Import mislukt", ex);
            SetStatus("Toevoegen mislukt: " + ex.Message + (ex is System.ComponentModel.Win32Exception ? " (FFmpeg niet gevonden; voer install-prereqs.ps1 uit.)" : string.Empty), isError: true);
        }
        finally
        {
            _importButton.Enabled = true;
            RefreshLibrary(force: true);
        }
    }

    // ---- Instellingen, venster en afsluiten ----

    private void ShowSettings()
    {
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var workerRelevantChanged = form.WorkerSettingsChanged;
        _settings = form.Result;
        SaveSettings();
        try
        {
            WindowsStartupService.Apply(_settings.Ui.StartWithWindows, startMinimized: true);
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Starten met Windows instellen mislukt", ex);
        }

        if (workerRelevantChanged)
        {
            // De worker leest de instellingen bij het starten; herstarten neemt ze meteen mee.
            _worker.Stop();
            _worker.EnsureRunning(_settings);
        }

        UpdateWorkerLabel();
        RefreshLibrary(force: true);
    }

    private void SaveSettings()
    {
        SettingsStore.Save(_settings);
        _settingsStamp = File.GetLastWriteTimeUtc(SettingsStore.GetSettingsPath());
    }

    // install-prereqs.ps1 zet de paden van Python en FFmpeg in hetzelfde instellingenbestand, vaak terwijl de recorder
    // al open staat (de installer start beide). Zonder herladen bleef de oude stand in beeld en overschreef de recorder
    // die paden bij de volgende keer bewaren.
    private void ReloadSettingsIfChangedElsewhere()
    {
        try
        {
            var path = SettingsStore.GetSettingsPath();
            if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) == _settingsStamp)
            {
                return;
            }

            var before = System.Text.Json.JsonSerializer.Serialize(_settings.Worker);
            _settings = SettingsStore.Load();
            _settingsStamp = File.GetLastWriteTimeUtc(path);
            if (System.Text.Json.JsonSerializer.Serialize(_settings.Worker) != before)
            {
                _worker.Stop();
            }

            AppRuntime.LogMessage("Instellingen opnieuw gelezen na een wijziging buiten de recorder.");
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Instellingen opnieuw lezen mislukt", ex);
        }
    }

    private void SaveSettingsQuietly()
    {
        try
        {
            SaveSettings();
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Instellingen bewaren mislukt", ex);
        }
    }

    private void ShowAbout()
    {
        MessageBox.Show(this,
            $"{Branding.DisplayName} {AppRuntime.GetProductVersionTag()}{Environment.NewLine}{Environment.NewLine}" +
            "Neemt je microfoon en het geluid van de pc op en schrijft het gesprek uit op je eigen pc (WhisperX, pyannote). Er gaat niets naar de cloud." + Environment.NewLine + Environment.NewLine +
            "Gratis software onder de GNU General Public License versie 3. Zonder enige garantie." + Environment.NewLine +
            "Copyright (c) 2026 rgerr" + Environment.NewLine + Branding.ProjectUrl,
            "Over " + Branding.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppRuntime.LogException($"Map openen mislukt: {path}", ex);
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppRuntime.LogException($"Link openen mislukt: {url}", ex);
        }
    }

    private string? Prompt(string title, string label, string value)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(420, 120),
            Font = Font,
        };
        var text = new TextBox { Text = value, Left = 12, Top = 36, Width = 396 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 252, Top = 76, Width = 75 };
        var cancel = new Button { Text = "Annuleren", DialogResult = DialogResult.Cancel, Left = 333, Top = 76, Width = 75 };
        form.Controls.AddRange(new Control[] { new Label { Text = label, Left = 12, Top = 12, AutoSize = true }, text, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog(this) == DialogResult.OK ? text.Text : null;
    }

    public void ShowFromOtherInstance(Action acknowledge)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(new Action(() =>
        {
            ShowFromTray();
            acknowledge();
        }));
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
        _trayShowItem.Text = "Venster verbergen";
    }

    private void ToggleWindow()
    {
        if (Visible)
        {
            Hide();
            _trayShowItem.Text = "Venster tonen";
        }
        else
        {
            ShowFromTray();
        }
    }

    private void ExitApplication()
    {
        _reallyExit = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyExit && _settings.Ui.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _trayShowItem.Text = "Venster tonen";
            _trayIcon.ShowBalloonTip(2000, Branding.DisplayName, "Captivox draait door in het systeemvak.", ToolTipIcon.Info);
            return;
        }

        if (_session is not null || _stopInProgress)
        {
            e.Cancel = true;
            _reallyExit = false;
            if (_stopInProgress)
            {
                _closeAfterStop = true;
                return;
            }

            ShowFromTray();
            if (MessageBox.Show(this, "Er loopt een opname. Stoppen, bewaren en dan afsluiten?", Branding.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _closeAfterStop = true;
                _ = StopRecordingAsync();
            }

            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _tick.Stop();
        if (_hotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
        }

        _worker.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnFormClosed(e);
    }
}
