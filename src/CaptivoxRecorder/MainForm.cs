using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Management;
using LocalCaptureAgent.Controls;
using LocalCaptureAgent.Models;
using LocalCaptureAgent.Services;

namespace LocalCaptureAgent;

public sealed partial class MainForm : Form
{
    private enum UiStatusSource
    {
        General,
        AiPostProcessing,
    }

    private const int RecordToggleHotKeyId = 1001;
    private const int ShowHideHotKeyId = 1002;

    private static readonly Color AppBackground = Color.FromArgb(245, 247, 250);
    private static readonly Color CardBackground = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(223, 228, 235);
    private static readonly Color AccentColor = Color.FromArgb(14, 99, 156);
    private static readonly Color AccentHoverColor = Color.FromArgb(11, 86, 136);
    private static readonly Color StopColor = Color.FromArgb(183, 28, 28);
    private static readonly Color StopHoverColor = Color.FromArgb(153, 22, 22);
    private static readonly Color NeutralButton = Color.FromArgb(236, 240, 244);
    private static readonly Color NeutralButtonHover = Color.FromArgb(226, 232, 238);
    private static readonly Font SubtitleFont = new("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font SectionFont = new("Segoe UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point);

    private readonly TextBox _outputRootTextBox = new() { Text = Branding.DefaultRecordingsRoot, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly ComboBox _micComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly ComboBox _systemComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly NumericUpDown _micGainNumeric = new() { DecimalPlaces = 2, Increment = 0.05M, Minimum = 0.10M, Maximum = 4M, Value = 1.00M, Width = 110 };
    private readonly NumericUpDown _systemGainNumeric = new() { DecimalPlaces = 2, Increment = 0.05M, Minimum = 0.10M, Maximum = 4M, Value = 1.00M, Width = 110 };
    private readonly CheckBox _minimizeToTrayCheckbox = new() { Text = "Minimaliseren naar tray", Checked = false, AutoSize = true };
    private readonly Button _browseButton = new() { Text = "Kies map", Width = 108, Height = 32 };
    private readonly Button _refreshDevicesButton = new() { Text = "Ververs", Width = 108, Height = 32 };
    private readonly PrimaryActionButton _recordToggleButton = new() { Text = "Opname starten", Width = 212, Height = 46, VisualState = PrimaryActionVisualState.Start };
    private readonly Button _openFolderButton = new() { Text = "Open sessiemap", Width = 148, Height = 34 };
    private readonly Button _settingsButton = new() { Text = "\u2699", Width = 36, Height = 34 };
    private readonly Label _qualityLabel = new()
    {
        AutoSize = true,
        MaximumSize = new Size(900, 0),
        Text = "Watch folders, custom vocabulary, device-check en handmatige audio-import beheer je centraal via Instellingen. De recorder-GUI blijft hiermee gericht op opnemen en terugvinden."
    };
    private readonly Label _statusLabel = new() { AutoSize = true, Text = "Gereed." };
    private readonly Label _sessionLabel = new() { AutoSize = true, Text = "Nog geen opname gestart." };
    private readonly Label _elapsedLabel = new() { AutoSize = true, Text = "00:00:00" };
    private readonly Label _headerElapsedLabel = new() { AutoSize = true, Text = "00:00:00" };
    private readonly Label _selectedMicLabel = new() { AutoSize = true, Text = "Nog niet geladen" };
    private readonly Label _selectedSystemLabel = new() { AutoSize = true, Text = "Nog niet geladen" };
    private readonly Label _profileLabel = new() { AutoSize = true, Text = "speech-first-windowed-mix-v5" };
    private readonly Label _workerStatusLabel = new() { AutoSize = true, Text = "Nog niet gescand" };
    private readonly Label _queueStatusLabel = new() { AutoSize = true, Text = "Nog niet gescand" };
    private readonly Label _workerActivityLabel = new() { AutoSize = true, Text = "Nog niet gescand" };
    private readonly Label _workerPhaseLabel = new() { AutoSize = true, Text = "Nog niet gescand" };
    private readonly Label _workerSessionLabel = new() { AutoSize = true, Text = "Geen actieve sessie" };
    private readonly ProgressBar _workerProgressBar = new() { Width = 220, Height = 18, Style = ProgressBarStyle.Blocks, MarqueeAnimationSpeed = 28, Value = 0 };
    private readonly Label _retentionStatusLabel = new() { AutoSize = true, Text = "Nog niet gescand" };
    private DateTimeOffset _lastCaptureFailureNotificationAt = DateTimeOffset.MinValue;
    private int _capacityWarningStage;
    private readonly Label _recoveryStatusLabel = new() { AutoSize = true, Text = "Nog niet gecontroleerd" };
    private readonly Label _recordingHealthLabel = new() { AutoSize = true, Text = "Niet actief" };
    private readonly Label _micHealthLabel = new() { AutoSize = true, Text = "Niet actief" };
    private readonly Label _systemHealthLabel = new() { AutoSize = true, Text = "Niet actief" };
    private readonly Label _silenceStateLabel = new() { AutoSize = true, Text = "Niet actief" };
    private readonly Label _mixAdviceLabel = new()
    {
        AutoSize = true,
        MaximumSize = new Size(900, 0),
        Text = "Advies: laat mic gain op 1.00 staan en verhoog system gain alleen als de tegenpartij structureel te zacht binnenkomt. Gebruik voor een echte opname eerst de device-check en controleer expliciet of het gekozen renderdevice ook het werkelijke afspeelapparaat is."
    };
    private readonly Label _hotkeyLabel = new()
    {
        AutoSize = true,
        MaximumSize = new Size(900, 0),
        Text = "Hotkeys: Ctrl+Shift+F9 start/stop opname | Ctrl+Shift+F10 venster tonen/verbergen"
    };
    private readonly LevelMeterControl _micLevelMeter = new();
    private readonly LevelMeterControl _systemLevelMeter = new();
    private readonly SignalRibbonControl _headerSignalRibbon = new();
    private Panel? _headerShellCard;
    private Control? _headerSignalStrip;
    private Label? _headerElapsedSubtitleLabel;
    private Control? _headerSignalFooter;
    private TabControl? _mainTabs;
    private TabPage? _libraryTabPage;
    private TabPage? _statusTabPage;
    private bool _libraryPanelBuilt;
    private CancellationTokenSource? _libraryRecordingRefreshCts;
    private bool _audioDevicesLoaded;
    private long _startupConstructorElapsedMs;
    private readonly Label _micLevelValueLabel = new() { AutoSize = true, Text = "0%" };
    private readonly Label _systemLevelValueLabel = new() { AutoSize = true, Text = "0%" };
    private readonly TextBox _librarySearchTextBox = new() { PlaceholderText = "Zoek in sessies en transcripties", Dock = DockStyle.Top };
    private readonly Button _libraryRefreshButton = new() { Text = "Ververs bibliotheek", Width = 148, Height = 32 };
    private readonly Button _libraryOpenFolderButton = new() { Text = "Map", Width = 120, Height = 32 };
    private readonly Button _libraryOpenTranscriptButton = new() { Text = "Transcript", Width = 148, Height = 32 };
    private readonly Button _libraryCopyTranscriptButton = new() { Text = "Kopieer", Width = 148, Height = 32 };
    private readonly Button _libraryExportSrtButton = new() { Text = "Exporteer SRT", Width = 132, Height = 32 };
    private readonly Button _libraryExportVttButton = new() { Text = "Exporteer VTT", Width = 132, Height = 32 };
    private readonly Button _libraryDeleteButton = new() { Text = "Verwijderen", Width = 132, Height = 32 };
    private readonly ListView _libraryListView = new() { Dock = DockStyle.Top, Height = 240, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false };
    private readonly ContextMenuStrip _libraryContextMenu = new();
    private readonly ToolStripMenuItem _libraryContextOpenTranscriptItem = new("Open transcript");
    private readonly ToolStripMenuItem _libraryContextOpenFolderItem = new("Open map");
    private readonly ToolStripMenuItem _libraryContextEditTitleItem = new("Titel toevoegen/wijzigen");
    private readonly ToolStripMenuItem _libraryContextEditRecordedAtItem = new("Opnametijd wijzigen");
    private readonly ToolStripMenuItem _libraryContextCopyTranscriptItem = new("Kopieer transcript");
    private readonly ToolStripMenuItem _libraryContextExportSrtItem = new("Exporteer SRT");
    private readonly ToolStripMenuItem _libraryContextExportVttItem = new("Exporteer VTT");
    private readonly ToolStripMenuItem _libraryContextDeleteItem = new("Verwijderen");
    private readonly TextBox _detailsTextBox = new()
    {
        ReadOnly = true,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Top,
        Height = 220,
        BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        Text = "Statusdetails verschijnen hier na starten of stoppen van een opname."
    };
    private readonly StatusStrip _statusStrip = new() { SizingGrip = false, RenderMode = ToolStripRenderMode.System };
    private readonly ToolStripStatusLabel _statusStripLabel = new() { Text = "Klaar", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripProgressBar _footerWorkerProgressBar = new() { AutoSize = false, Width = 120, Minimum = 0, Maximum = 100, Value = 0, Style = ProgressBarStyle.Blocks, MarqueeAnimationSpeed = 0, ToolTipText = "Workerstatus" };
    private readonly ToolStripStatusLabel _elapsedStatusLabel = new() { AutoSize = false, Width = 76, Text = "00:00:00", TextAlign = ContentAlignment.MiddleRight, Visible = false };
    private readonly Label _overviewLastSessionValueLabel = new() { AutoSize = true, MaximumSize = new Size(240, 0), Text = "Nog geen sessie" };
    private readonly Label _overviewLibraryCountValueLabel = new() { AutoSize = true, MaximumSize = new Size(240, 0), Text = "0" };
    private readonly Label _overviewWorkerStatusValueLabel = new() { AutoSize = true, MaximumSize = new Size(240, 0), Text = "Onbekend" };
    private readonly Label _overviewLastProcessingValueLabel = new() { AutoSize = true, MaximumSize = new Size(240, 0), Text = "Nog geen verwerking" };
    private readonly Label _overviewAutomationValueLabel = new() { AutoSize = true, MaximumSize = new Size(240, 0), Text = "Uit" };
    private readonly Label _overviewLastOrganizationValueLabel = new() { AutoSize = true, MaximumSize = new Size(240, 0), Text = "-" };
    private readonly Label _overviewLastProjectValueLabel = new() { AutoSize = true, MaximumSize = new Size(240, 0), Text = "-" };
    private readonly Label _recorderReadinessStatusValueLabel = new() { AutoSize = true, MaximumSize = new Size(260, 0), Text = "Gereed." };
    private readonly Label _recorderReadinessSessionValueLabel = new() { AutoSize = true, MaximumSize = new Size(260, 0), Text = "Nog geen opname gestart." };
    private readonly Label _recorderReadinessMicValueLabel = new() { AutoSize = true, MaximumSize = new Size(260, 0), Text = "Nog niet geladen" };
    private readonly Label _recorderReadinessSystemValueLabel = new() { AutoSize = true, MaximumSize = new Size(260, 0), Text = "Nog niet geladen" };
    private readonly Label _recorderReadinessProfileValueLabel = new() { AutoSize = true, MaximumSize = new Size(260, 0), Text = "speech-first-windowed-mix-v5" };
    private readonly Label _recorderReadinessWorkerValueLabel = new() { AutoSize = true, MaximumSize = new Size(260, 0), Text = "Nog niet gescand" };
    private readonly ToolStripStatusLabel _versionStatusLabel = new() { Text = GetDisplayVersion(), TextAlign = ContentAlignment.MiddleRight };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly Stopwatch _recordingStopwatch = new();
    private readonly System.Windows.Forms.Timer _notificationPollTimer = new() { Interval = 5000 };
    private readonly NotifyIcon _notifyIcon = new() { Visible = false, Text = Branding.DisplayName };
    // AutoPopDelay: een lange tekst lees je niet in de standaard vijf seconden.
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 20000 };
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolStripMenuItem _trayShowHideItem = new("Venster tonen");
    private readonly ToolStripMenuItem _trayToggleRecordItem = new("Opname starten");
    private readonly ToolStripMenuItem _trayOpenFolderItem = new("Opnamemap openen");
    private readonly ToolStripMenuItem _trayExitItem = new("Afsluiten");

    private ApplicationSettings _appSettings = new();
    private string? _preferredMicDeviceId;
    private string? _preferredSystemDeviceId;
    private AudioCaptureSession? _session;
    private SessionManifest? _manifest;
    private UiStatusSource _statusSource = UiStatusSource.General;
    private string? _statusMeetingId;
    private DateTimeOffset? _startedAt;
    private bool _closingAfterStop;
    private bool _isBusy;
    private bool _recordingBusy;
    private bool _audioDeviceLoading;
    private CancellationTokenSource? _librarySearchDebounceCts;
    private CancellationTokenSource? _libraryRefreshCts;
    private int _libraryRefreshVersion;
    private bool _hotkeysRegistered;
    private bool _recordHotkeyRegistered;
    private bool _showHideHotkeyRegistered;
    private bool _trayBalloonShown;
    private bool _uiResourcesCleaned;
    private readonly object _workerProcessSync = new();
    private Process? _workerProcess;
    private readonly NotificationDispatcher _notificationDispatcher;
    private readonly Dictionary<string, string> _sessionNotificationStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Form> _transcriptReadyDialogs = new(StringComparer.OrdinalIgnoreCase);
    private bool _notificationStatesPrimed;
    private bool _backgroundPollBusy;
    private DateTimeOffset _lastWorkerStartAttemptAt = DateTimeOffset.MinValue;
    private string? _lastIgnoredWorkerStatusVersion;
    private DateTimeOffset _lastIgnoredWorkerStatusLogAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan WorkerRestartCooldown = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StaleWorkerStatusLogInterval = TimeSpan.FromMinutes(5);
    private DateTimeOffset _lastMicSignalAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSystemSignalAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSilenceNotificationAt = DateTimeOffset.MinValue;
    private bool _lastStopFailed;
    private bool _stopFinalizeInProgress;
    private bool _closeAfterFinalize;
    private CancellationTokenSource? _libraryDetailRefreshCts;
    private int _libraryDetailRefreshVersion;

    public MainForm(bool launchMinimized = false)
    {
        var constructorWatch = Stopwatch.StartNew();
        _launchMinimizedRequested = launchMinimized;
        _notificationDispatcher = new NotificationDispatcher(_notifyIcon, () => _appSettings);
        SuspendLayout();
        // Zo vroeg mogelijk: alles wat hierna aan het venster wordt toegevoegd, krijgt meteen de taal van de interface, nog
        // voordat de indeling wordt berekend (anders bleven kaarten op de maat van de Nederlandse tekst).
        UiTextBinder.Attach(this);

        Text = Branding.DisplayName;
        Icon = Branding.GetAppIconOrDefault();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 820);
        Size = new Size(1120, 1000);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = AppBackground;
        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0),
            BackColor = ShellPalette.Ground,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // De nieuwe schil (bovenbalk met de opnameknop, menu links, pagina's zonder tabbladkoppen).
        root.Controls.Add(BuildShellTopBar(), 0, 0);
        root.Controls.Add(BuildMeetingDetectionBanner(), 0, 1);
        root.Controls.Add(BuildShellBody(BuildMainTabs()), 0, 2);

        _statusStrip.Items.Add(_statusStripLabel);
        _statusStrip.Items.Add(_footerWorkerProgressBar);
        _statusStrip.Items.Add(_editionStatusStripLabel);
        _editionStatusStripLabel.Visible = false;
        _statusStrip.Items.Add(_versionStatusLabel);
        root.Controls.Add(_statusStrip, 0, 3);

        Controls.Add(root);

        LoadPersistedSettings();
        RenderToday();
        RefreshEditionStateAndUi(attemptRevalidation: false, persistSettings: false);

        StyleInputs();
        ConfigureCompactShellLabels();
        ConfigureLibraryEnhancements();
        ConfigureAssistiveFeatures();
        StyleButton(_browseButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(_refreshDevicesButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        // De apparaatkeuze staat onder Instellingen, Opnemen; daar de opmaak van de nieuwe schil (na StyleInputs).
        StyleMovedDeviceControls();
        _recordToggleButton.ForeColor = Color.White;
        _recordToggleButton.AccessibleName = "Opname starten of stoppen";
        _recordToggleButton.AccessibleDescription = "Start of stop de huidige opname.";
        StyleButton(_openFolderButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(_settingsButton, CardBackground, Color.FromArgb(241, 246, 250), AccentColor);
        StyleButton(_libraryRefreshButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(_libraryOpenFolderButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(_libraryOpenTranscriptButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(_libraryCopyTranscriptButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(_libraryExportSrtButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(_libraryExportVttButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(_libraryDeleteButton, Color.FromArgb(255, 241, 242), Color.FromArgb(254, 226, 226), Color.FromArgb(153, 27, 27));
        _settingsButton.Font = new Font("Segoe UI Symbol", 11F, FontStyle.Bold, GraphicsUnit.Point);
        _settingsButton.Margin = new Padding(0);
        _settingsButton.FlatAppearance.BorderColor = Color.FromArgb(214, 222, 230);
        SetUiToolTip(_settingsButton, "Instellingen en admin");
        _settingsButton.AccessibleName = "Instellingen en admin";
        _settingsButton.AccessibleDescription = "Open instellingen en beheeropties.";
        StyleShellSettingsButton();

        _browseButton.Click += BrowseButton_Click;
        _refreshDevicesButton.Click += async (_, _) => await RunBusyUiActionAsync("Apparaten verversen", () => LoadDevicesAsync(showMessageBoxOnFailure: true));
        _recordToggleButton.Click += async (_, _) => await RunRecordingUiActionAsync(_session is null ? "Opname starten" : "Opname stoppen", ToggleRecordingAsync);
        _openFolderButton.Click += (_, _) => OpenOutputFolder();
        _settingsButton.Click += (_, _) => ShowSettingsDialog();
        _libraryRefreshButton.Click += async (_, _) => await RunLibraryUiActionAsync("Sessiebibliotheek verversen", ct => RefreshLibraryAsync(ct));
        _libraryOpenFolderButton.Click += (_, _) => OpenSelectedLibrarySessionFolder();
        _libraryOpenTranscriptButton.Click += (_, _) => OpenSelectedLibraryTranscript();
        _libraryCopyTranscriptButton.Click += (_, _) => CopySelectedLibraryTranscript();
        _libraryExportSrtButton.Click += (_, _) => ExportSelectedLibraryTranscript("srt");
        _libraryExportVttButton.Click += (_, _) => ExportSelectedLibraryTranscript("vtt");
        _libraryDeleteButton.Click += async (_, _) => await RunLibraryUiActionAsync("Sessie verwijderen", DeleteSelectedLibrarySessionAsync);
        _librarySearchTextBox.TextChanged += (_, _) => ScheduleLibrarySearchRefresh();
        _libraryListView.SelectedIndexChanged += (_, _) => { RefreshLibraryActionStates(); RefreshLibraryDetailPane(); };
        _libraryContextOpenTranscriptItem.Click += (_, _) => OpenSelectedLibraryTranscript();
        _libraryContextOpenFolderItem.Click += (_, _) => OpenSelectedLibrarySessionFolder();
        _libraryContextEditTitleItem.Click += (_, _) => EditSelectedLibrarySessionTitle();
        _libraryContextEditRecordedAtItem.Click += (_, _) => EditSelectedImportedRecordingRecordedAt();
        _libraryContextCopyTranscriptItem.Click += (_, _) => CopySelectedLibraryTranscript();
        _libraryContextExportSrtItem.Click += (_, _) => ExportSelectedLibraryTranscript("srt");
        _libraryContextExportVttItem.Click += (_, _) => ExportSelectedLibraryTranscript("vtt");
        _libraryContextDeleteItem.Click += async (_, _) => await RunLibraryUiActionAsync("Sessie verwijderen", DeleteSelectedLibrarySessionAsync);
        _libraryContextMenu.Items.AddRange(new ToolStripItem[]
        {
            _libraryContextOpenTranscriptItem,
            _libraryContextOpenFolderItem,
            _libraryContextEditTitleItem,
            _libraryContextEditRecordedAtItem,
            new ToolStripSeparator(),
            _libraryContextCopyTranscriptItem,
            _libraryContextExportSrtItem,
            _libraryContextExportVttItem,
            new ToolStripSeparator(),
            _libraryContextDeleteItem,
        });
        _libraryListView.ContextMenuStrip = _libraryContextMenu;
        _libraryListView.MouseUp += LibraryListView_MouseUp;
        _libraryListView.Resize += (_, _) => AdjustLibraryListColumns();
        _micComboBox.SelectedIndexChanged += (_, _) => UpdateSelectedDeviceLabels();
        _systemComboBox.SelectedIndexChanged += (_, _) => UpdateSelectedDeviceLabels();
        _timer.Tick += (_, _) =>
        {
            UpdateElapsed();
            UpdateRecordingHealthState();
        };
        _notificationPollTimer.Tick += async (_, _) => await PollBackgroundStateAsync();
        Resize += MainForm_Resize;
        FormClosing += MainForm_FormClosing;
        Shown += MainForm_Shown;

        ConfigureTray();
        ConfigureLibraryListView();
        SetRecorderDeviceLoadingState(true);
        _recoveryStatusLabel.Text = "Recoverycontrole wacht tot na vensterstart";
        _selectedMicLabel.Text = "Audioapparaten laden...";
        _selectedSystemLabel.Text = "Audioapparaten laden...";
        _workerStatusLabel.Text = "Bibliotheekscan wacht tot na vensterstart";
        ApplyCurrentLanguageToUi();
        _startupConstructorElapsedMs = constructorWatch.ElapsedMilliseconds;
        ResumeLayout(true);
    }

    private Control CreateSharedBrandBannerStrip(Padding hostPadding)
    {
        return new BrandedHeaderStrip
        {
            Dock = DockStyle.Top,
            Margin = new Padding(0),
            Padding = hostPadding
        };
    }

    private Control BuildHeaderPanel()
    {
        var header = CreateRecorderSurfaceCard(autoSize: true);
        _headerShellCard = header;
        header.Padding = new Padding(18, 16, 18, 14);
        header.Margin = new Padding(0, 0, 0, 8);

        _headerElapsedLabel.Font = new Font("Segoe UI", 24F, FontStyle.Bold, GraphicsUnit.Point);
        _headerElapsedLabel.ForeColor = AccentColor;
        _headerElapsedLabel.Margin = new Padding(0);
        _headerElapsedLabel.AutoSize = true;

        var subtitle = new Label
        {
            Text = "Opnameduur",
            Font = SubtitleFont,
            AutoSize = true,
            ForeColor = Color.FromArgb(92, 101, 116),
            Margin = new Padding(0, 0, 0, 4),
        };
        _headerElapsedSubtitleLabel = subtitle;

        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = CardBackground,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildRecorderBrandRow(), 0, 0);

        var content = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = CardBackground,
            Margin = new Padding(0, 6, 0, 8),
            Padding = new Padding(0)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 236));

        var elapsedStack = new BufferedFlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = CardBackground,
            Dock = DockStyle.Fill
        };
        elapsedStack.Controls.Add(subtitle);
        elapsedStack.Controls.Add(_headerElapsedLabel);
        content.Controls.Add(elapsedStack, 0, 0);

        var actionHost = new Panel
        {
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = CardBackground,
            Dock = DockStyle.Fill,
            MinimumSize = new Size(224, 52),
        };
        _recordToggleButton.Margin = new Padding(0);
        _recordToggleButton.Size = new Size(218, 44);
        actionHost.Controls.Add(_recordToggleButton);
        actionHost.Resize += (_, _) =>
        {
            var x = Math.Max(0, actionHost.ClientSize.Width - _recordToggleButton.Width);
            var y = Math.Max(0, (actionHost.ClientSize.Height - _recordToggleButton.Height) / 2);
            _recordToggleButton.Location = new Point(x, y);
        };
        content.Controls.Add(actionHost, 1, 0);
        root.Controls.Add(content, 0, 1);

        _headerSignalStrip = BuildRecorderSignalStrip();
        root.Controls.Add(_headerSignalStrip, 0, 2);

        header.Controls.Add(root);
        return header;
    }

    private void ApplyTopShellMode()
    {
        if (_mainTabs is null || _headerShellCard is null || _headerSignalStrip is null)
        {
            return;
        }

        var expanded = _mainTabs.SelectedIndex == 0;
        _headerSignalStrip.Visible = true;
        _headerSignalRibbon.Height = 58;
        if (_headerSignalFooter is not null)
        {
            _headerSignalFooter.Visible = true;
            _headerSignalFooter.Margin = expanded ? new Padding(0, 8, 0, 0) : new Padding(0, 6, 0, 0);
        }

        if (_headerElapsedSubtitleLabel is not null)
        {
            _headerElapsedSubtitleLabel.Margin = expanded ? new Padding(0, 0, 0, 4) : new Padding(0, 0, 0, 2);
            _headerElapsedSubtitleLabel.Font = expanded
                ? SubtitleFont
                : new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        }

        _recordToggleButton.Size = expanded ? new Size(218, 44) : new Size(206, 40);
        _headerShellCard.Padding = expanded ? new Padding(18, 16, 18, 14) : new Padding(18, 10, 18, 8);
        _headerShellCard.Margin = expanded ? new Padding(0, 0, 0, 8) : new Padding(0, 0, 0, 4);
    }

    private Control BuildMainTabs()
    {
        var tabs = new ShellPageHost
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };

        _mainTabs = tabs;

        // Oude tabbladen (Opnemen, Bibliotheek, Overleggen & acties, Status & diagnose) naast de pagina's van de schil.
        var recorderTab = new BufferedTabPage("Opnemen")
        {
            BackColor = AppBackground,
            AutoScroll = true,
            Padding = new Padding(0, 2, 0, 0),
        };
        recorderTab.Controls.Add(BuildConfigurationPanel());

        var libraryTab = new BufferedTabPage("Bibliotheek")
        {
            BackColor = AppBackground,
            Padding = new Padding(0, 2, 0, 0),
        };
        _libraryTabPage = libraryTab;
        libraryTab.Controls.Add(BuildDeferredLibraryPlaceholder());


        var statusTab = new BufferedTabPage("Status & diagnose")
        {
            BackColor = AppBackground,
            AutoScroll = true,
            Padding = new Padding(0, 2, 0, 0),
        };
        _statusTabPage = statusTab;
        statusTab.Controls.Add(BuildStatusPanel());

        var todayTab = BuildTodayPage();
        var notificationsTab = BuildNotificationsPage();
        var settingsHubTab = BuildSettingsHubPage();
        var recordingTab = BuildRecordingPage();
        var meetingsTab = BuildMeetingsPage();
        var proOfferTab = BuildProOfferPage();
        tabs.TabPages.Add(todayTab);
        tabs.TabPages.Add(recorderTab);
        tabs.TabPages.Add(libraryTab);
        tabs.TabPages.Add(statusTab);
        tabs.TabPages.Add(notificationsTab);
        tabs.TabPages.Add(settingsHubTab);
        tabs.TabPages.Add(recordingTab);
        tabs.TabPages.Add(meetingsTab);
        tabs.TabPages.Add(proOfferTab);
        RegisterShellPage(ShellNavToday, todayTab);
        // Overleggen is de nieuwe pagina; de oude bibliotheek blijft bereikbaar (link onderaan) tot fase 4.
        RegisterShellPage(ShellNavMeetings, meetingsTab);
        RegisterShellPage(ShellNavLibrary, libraryTab);
        RegisterShellPage(ShellNavNotifications, notificationsTab);
        RegisterShellPage(ShellNavSettings, settingsHubTab);
        RegisterShellPage(ShellNavSystem, statusTab);
        RegisterShellPage(ShellNavProOffer, proOfferTab);
        tabs.SelectedTab = todayTab;
        tabs.SelectedIndexChanged += async (_, _) =>
        {
            ApplyTopShellMode();
            SyncShellNavigation();
            if (tabs.SelectedTab == libraryTab)
            {
                EnsureLibraryTabBuilt();
                await RunLibraryUiActionAsync("Bibliotheek openen", ct => RefreshLibraryAsync(ct));
            }
            else if (tabs.SelectedTab == statusTab)
            {
                RefreshStatusPanel();
            }
        };
        ApplyTopShellMode();
        return tabs;
    }

    private void RefreshStatusPanel()
    {
        if (_statusTabPage is null || _statusTabPage.IsDisposed) return;
        var scrollY = _statusTabPage.AutoScrollPosition.Y;
        _statusTabPage.SuspendLayout();
        try
        {
            // Eerst opbouwen: de opbouw verhuist de gedeelde statuslabels naar het nieuwe paneel. Daarna pas de oude
            // omhulsels weggooien (vooraf weggooien nam die gedeelde labels mee).
            var previous = _statusTabPage.Controls.Cast<Control>().ToList();
            var panel = BuildStatusPanel();
            _statusTabPage.Controls.Clear();
            _statusTabPage.Controls.Add(panel);
            foreach (var control in previous) control.Dispose();
            if (scrollY < 0)
            {
                _statusTabPage.AutoScrollPosition = new Point(0, -scrollY);
            }
        }
        finally
        {
            _statusTabPage.ResumeLayout(true);
        }
    }

    private Control BuildDeferredLibraryPlaceholder()
    {
        var card = CreateRecorderSurfaceCard(autoSize: true);
        card.Padding = new Padding(18);
        card.Controls.Add(new Label
        {
            Text = "Bibliotheek wordt geladen zodra je deze tab opent.",
            AutoSize = true,
            ForeColor = Color.FromArgb(97, 107, 121),
            Margin = new Padding(0)
        });
        return card;
    }

    private void EnsureLibraryTabBuilt()
    {
        if (_libraryPanelBuilt || _libraryTabPage is null || _libraryTabPage.IsDisposed)
        {
            return;
        }

        var watch = Stopwatch.StartNew();
        _libraryTabPage.SuspendLayout();
        try
        {
            _libraryTabPage.Controls.Clear();
            _libraryTabPage.Controls.Add(BuildLibraryPanel());
            _libraryPanelBuilt = true;
        }
        finally
        {
            _libraryTabPage.ResumeLayout(true);
        }

        AppRuntime.LogMessage($"Startup/lazy-load: bibliotheekpaneel opgebouwd in {watch.ElapsedMilliseconds} ms.");
    }

    private Control BuildConfigurationPanel()
    {
        var stack = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = AppBackground,
            Margin = new Padding(0, 4, 0, 10),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var quickStartCard = BuildWorkflowQuickStartCard();
        quickStartCard.Margin = new Padding(0, 0, 0, 10);
        var settingsCard = BuildPrimarySettingsCard();
        settingsCard.Margin = new Padding(0, 0, 10, 0);
        var overviewCard = BuildRecorderQuickOverviewCard();
        overviewCard.Margin = new Padding(10, 0, 0, 0);
        var readinessCard = BuildRecorderReadinessCard();
        readinessCard.Margin = new Padding(0, 10, 0, 0);

        stack.Controls.Add(quickStartCard, 0, 0);
        stack.SetColumnSpan(quickStartCard, 2);
        stack.Controls.Add(settingsCard, 0, 1);
        stack.Controls.Add(overviewCard, 1, 1);
        stack.Controls.Add(readinessCard, 0, 2);
        stack.SetColumnSpan(readinessCard, 2);
        return stack;
    }

    private Control BuildWorkflowQuickStartCard()
    {
        var card = CreateRecorderSurfaceCard(autoSize: true);
        card.Padding = new Padding(16, 14, 16, 10);

        var layout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = CardBackground,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AddSectionTitle(layout, "Snel starten", 0);

        var buttons = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = CardBackground,
            Margin = new Padding(0),
            Padding = new Padding(0),
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 183));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var statusButton = CreateQuickStartButton("Status controleren", 175);
        statusButton.Click += (_, _) => NavigateToMainWorkflow("Status & diagnose", null);
        var libraryButton = CreateQuickStartButton("Bibliotheek openen", 160);
        libraryButton.Click += (_, _) => NavigateToMainWorkflow("Bibliotheek", null);
        buttons.Controls.Add(statusButton, 0, 0);
        buttons.Controls.Add(libraryButton, 1, 0);
        layout.Controls.Add(buttons, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private Button CreateQuickStartButton(string text, int width)
    {
        var button = new Button { Text = text, Width = width, Height = 34, Margin = new Padding(0, 0, 8, 0), AccessibleName = text };
        StyleButton(button, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        return button;
    }

    private void NavigateToMainWorkflow(string mainTabText, string? nestedTabText)
    {
        if (_mainTabs is null) return;
        foreach (TabPage page in _mainTabs.TabPages)
        {
            if (!string.Equals(page.Text, mainTabText, StringComparison.OrdinalIgnoreCase) && !string.Equals(page.Text, UiText.T(mainTabText), StringComparison.OrdinalIgnoreCase)) continue;
            _mainTabs.SelectedTab = page;
            if (!string.IsNullOrWhiteSpace(nestedTabText)) SelectNestedTab(page, nestedTabText);
            return;
        }
    }

    private static bool SelectNestedTab(Control root, string tabText)
    {
        foreach (Control control in root.Controls)
        {
            if (control is TabControl tabs)
            {
                foreach (TabPage page in tabs.TabPages)
                {
                    if (string.Equals(page.Text, tabText, StringComparison.OrdinalIgnoreCase) || string.Equals(page.Text, UiText.T(tabText), StringComparison.OrdinalIgnoreCase))
                    {
                        tabs.SelectedTab = page;
                        return true;
                    }
                }
            }
            if (SelectNestedTab(control, tabText)) return true;
        }
        return false;
    }

    private Control BuildPrimarySettingsCard()
    {
        var card = CreateRecorderSurfaceCard(autoSize: true);
        card.Margin = new Padding(0, 0, 0, 10);
        card.Padding = new Padding(16, 16, 16, 20);

        var layout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 9,
            BackColor = CardBackground,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));

        for (var i = 0; i < 9; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        AddSectionTitle(layout, "Opname-instellingen", 0);
        AddFieldRow(layout, 1, "Outputmap", _outputRootTextBox, _browseButton);
        AddFieldRow(layout, 2, "Microfoon", _micComboBox, _refreshDevicesButton);
        AddFieldRow(layout, 3, "Systeemaudio", _systemComboBox, null);

        var gainPanel = new BufferedTableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 1,
            Dock = DockStyle.Left,
            Margin = new Padding(0)
        };
        gainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        gainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        gainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        gainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        _micGainNumeric.Width = 96;
        _systemGainNumeric.Width = 96;
        gainPanel.Controls.Add(CreateInlineLabel("Mic", 0), 0, 0);
        gainPanel.Controls.Add(_micGainNumeric, 1, 0);
        gainPanel.Controls.Add(CreateInlineLabel("Systeem", 16), 2, 0);
        gainPanel.Controls.Add(_systemGainNumeric, 3, 0);
        AddFieldRow(layout, 4, "Gain", gainPanel, null);

        var footerActions = new BufferedTableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 12, 0, 0),
            BackColor = CardBackground,
            Padding = new Padding(0)
        };
        footerActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172));
        footerActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _openFolderButton.AutoSize = false;
        _openFolderButton.Width = 172;
        ConfigureGridActionButton(_openFolderButton, 38, rightMargin: 0, bottomMargin: 0);

        footerActions.Controls.Add(_openFolderButton, 0, 0);
        layout.Controls.Add(footerActions, 0, 5);
        layout.SetColumnSpan(footerActions, 3);

        _qualityLabel.Visible = false;
        _qualityLabel.Text = string.Empty;

        _hotkeyLabel.MaximumSize = new Size(620, 0);
        _hotkeyLabel.Margin = new Padding(0, 12, 0, 2);
        layout.Controls.Add(_hotkeyLabel, 0, 6);
        layout.SetColumnSpan(_hotkeyLabel, 3);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildRecorderQuickOverviewCard()
    {
        var card = CreateRecorderSurfaceCard(autoSize: true);
        card.Padding = new Padding(16);
        card.Margin = new Padding(0);

        var layout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 8,
            BackColor = CardBackground,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 8; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        AddSectionTitle(layout, "Snel overzicht", 0);
        AddOverviewRow(layout, 1, "Laatste sessie", _overviewLastSessionValueLabel);
        AddOverviewRow(layout, 2, "Sessies in bibliotheek", _overviewLibraryCountValueLabel);
        AddOverviewRow(layout, 3, "Workerstatus", _overviewWorkerStatusValueLabel);
        AddOverviewRow(layout, 4, "Laatste verwerking", _overviewLastProcessingValueLabel);
        AddOverviewRow(layout, 5, "Automation export", _overviewAutomationValueLabel);
        AddOverviewRow(layout, 6, "Laatste organisatie", _overviewLastOrganizationValueLabel);
        AddOverviewRow(layout, 7, "Laatste project", _overviewLastProjectValueLabel);

        card.Controls.Add(layout);
        return card;
    }

    private static void AddOverviewRow(TableLayoutPanel layout, int row, string caption, Label valueLabel)
    {
        var label = new Label
        {
            Text = caption,
            AutoSize = true,
            ForeColor = Color.FromArgb(67, 78, 92),
            Margin = new Padding(0, row == 1 ? 4 : 8, 12, 0),
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        valueLabel.Margin = new Padding(0, row == 1 ? 4 : 8, 0, 0);
        valueLabel.ForeColor = Color.FromArgb(39, 46, 56);
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(valueLabel, 1, row);
    }

    private Control BuildRecorderReadinessCard()
    {
        var card = CreateRecorderSurfaceCard(autoSize: true);
        card.Padding = new Padding(16);
        card.Margin = new Padding(0);

        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = CardBackground,
            Margin = new Padding(0),
            Padding = new Padding(0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var left = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = CardBackground,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++)
        {
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var right = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = CardBackground,
            Margin = new Padding(12, 0, 0, 0),
            Padding = new Padding(0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++)
        {
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        AddSectionTitle(left, "Opnamegereedheid", 0);
        AddRecorderReadinessRow(left, 1, "Recorderstatus", _recorderReadinessStatusValueLabel);
        AddRecorderReadinessRow(left, 2, "Actieve sessie", _recorderReadinessSessionValueLabel);
        AddRecorderReadinessRow(left, 3, "Worker", _recorderReadinessWorkerValueLabel);

        AddSectionTitle(right, "Bronnen", 0);
        AddRecorderReadinessRow(right, 1, "Microfoon", _recorderReadinessMicValueLabel);
        AddRecorderReadinessRow(right, 2, "Systeemaudio", _recorderReadinessSystemValueLabel);
        AddRecorderReadinessRow(right, 3, "Profiel", _recorderReadinessProfileValueLabel);

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(right, 1, 0);
        card.Controls.Add(root);
        return card;
    }

    private static void AddRecorderReadinessRow(TableLayoutPanel layout, int row, string caption, Label valueLabel)
    {
        var label = new Label
        {
            Text = caption,
            AutoSize = true,
            ForeColor = Color.FromArgb(67, 78, 92),
            Margin = new Padding(0, row == 1 ? 4 : 8, 12, 0),
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        valueLabel.Margin = new Padding(0, row == 1 ? 4 : 8, 0, 0);
        valueLabel.ForeColor = Color.FromArgb(39, 46, 56);
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(valueLabel, 1, row);
    }

    private Control BuildRecorderBrandRow()
    {
        var row = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = CardBackground,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));

        var brandHost = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = CardBackground,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        brandHost.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        brandHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var logo = new PictureBox
        {
            Image = Branding.CreateHeaderLogoImage(96),
            SizeMode = PictureBoxSizeMode.Zoom,
            Width = 42,
            Height = 42,
            Margin = new Padding(0, 0, 12, 0),
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            TabStop = false
        };

        var titleFlow = new BufferedFlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 0),
            Padding = new Padding(0),
            BackColor = CardBackground,
            AutoScroll = true
        };
        titleFlow.Controls.Add(new Label
        {
            Text = "Captivox",
            AutoSize = true,
            Font = new Font("Segoe UI", 20F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(29, 35, 45),
            Margin = new Padding(0, 0, 8, 0)
        });
        titleFlow.Controls.Add(new Label
        {
            Text = "Recorder",
            AutoSize = true,
            Font = new Font("Segoe UI", 20F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(88, 97, 111),
            Margin = new Padding(0)
        });

        brandHost.Controls.Add(logo, 0, 0);
        brandHost.Controls.Add(titleFlow, 1, 0);

        var settingsHost = new BufferedFlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Dock = DockStyle.Right,
            Margin = new Padding(0),
            Padding = new Padding(0, 3, 0, 0),
            BackColor = CardBackground
        };
        _settingsButton.AutoSize = false;
        _settingsButton.Size = new Size(34, 34);
        _settingsButton.Dock = DockStyle.None;
        _settingsButton.Margin = new Padding(0);
        settingsHost.Controls.Add(_settingsButton);

        row.Controls.Add(brandHost, 0, 0);
        row.Controls.Add(settingsHost, 1, 0);
        return row;
    }

    private Control BuildCompactBrandRow()
    {
        var row = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = AppBackground,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var logo = new PictureBox
        {
            Image = Branding.CreateHeaderLogoImage(96),
            SizeMode = PictureBoxSizeMode.Zoom,
            Width = 34,
            Height = 34,
            Margin = new Padding(0, 0, 10, 0),
            Dock = DockStyle.Fill,
            BackColor = AppBackground,
            TabStop = false
        };

        var titleFlow = new BufferedFlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 0),
            Padding = new Padding(0),
            BackColor = AppBackground
        };
        titleFlow.Controls.Add(new Label
        {
            Text = "Captivox",
            AutoSize = true,
            Font = new Font("Segoe UI", 13.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(29, 35, 45),
            Margin = new Padding(0, 0, 6, 0)
        });
        titleFlow.Controls.Add(new Label
        {
            Text = "Recorder",
            AutoSize = true,
            Font = new Font("Segoe UI", 13.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(88, 97, 111),
            Margin = new Padding(0)
        });

        row.Controls.Add(logo, 0, 0);
        row.Controls.Add(titleFlow, 1, 0);
        return row;
    }

    private Control BuildRecorderSignalStrip()
    {
        _headerSignalRibbon.Dock = DockStyle.Top;
        _headerSignalRibbon.Margin = new Padding(0);
        _headerSignalRibbon.Height = 58;

        var strip = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = CardBackground,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        strip.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        strip.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        strip.Controls.Add(_headerSignalRibbon, 0, 0);
        _headerSignalFooter = BuildHeaderSignalCardsRow();
        strip.Controls.Add(_headerSignalFooter, 0, 1);

        return strip;
    }

    private static Control CreateSignalMetaValue(Label sourceLabel)
    {
        sourceLabel.AutoSize = true;
        sourceLabel.Margin = new Padding(8, 0, 0, 0);
        sourceLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        sourceLabel.ForeColor = Color.FromArgb(97, 107, 121);
        sourceLabel.TextAlign = ContentAlignment.MiddleLeft;
        return sourceLabel;
    }

    private static Label CreateSignalMetaLabel(string text, bool bold = false)
        => new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(97, 107, 121),
            Margin = new Padding(0)
        };

    private static Label CreateMeterLabel(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(63, 72, 86),
            Margin = new Padding(0, 5, 12, 0)
        };

    private static Label CreateShellInfoLabel(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = Color.FromArgb(97, 107, 121),
            Font = new Font("Segoe UI", 9.25F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0)
        };

    private Control CreateSignalHost(Control control, int hostHeight, int hostWidth, bool dockFill = false, int bottomMargin = 12)
    {
        var host = new BufferedPanel
        {
            Height = hostHeight,
            Margin = new Padding(0, 0, 0, bottomMargin),
            Padding = new Padding(0),
            BackColor = CardBackground,
            Dock = dockFill ? DockStyle.Fill : DockStyle.None,
            MinimumSize = new Size(Math.Max(dockFill ? 140 : hostWidth, hostWidth), hostHeight)
        };
        if (!dockFill)
        {
            host.Width = hostWidth;
        }

        control.Margin = new Padding(0);
        control.Dock = DockStyle.Fill;
        control.MinimumSize = new Size(0, hostHeight);
        host.Controls.Add(control);
        return host;
    }

    private static void ConfigureGridActionButton(Button button, int height, int rightMargin, int bottomMargin)
    {
        button.AutoSize = false;
        button.MinimumSize = new Size(0, height);
        button.Height = height;
        button.Dock = DockStyle.Fill;
        button.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        button.Margin = new Padding(0, 0, rightMargin, bottomMargin);
        button.Padding = new Padding(10, 0, 10, 0);
    }

    private Control CreateSignalCard(string title, Control bodyControl, Control? trailingControl)
    {
        var shell = new InsetPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 10, 10),
            Padding = new Padding(12, 10, 12, 10),
            BackColor = Color.FromArgb(249, 251, 253),
            BorderColor = Color.FromArgb(225, 231, 238)
        };

        var panel = new BufferedTableLayoutPanel
        {
            ColumnCount = trailingControl is null ? 1 : 2,
            RowCount = 2,
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = shell.BackColor,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (trailingControl is not null)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        }
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(31, 41, 55),
            Margin = new Padding(0, 0, 0, 6)
        };
        panel.Controls.Add(titleLabel, 0, 0);
        if (trailingControl is not null)
        {
            trailingControl.Margin = new Padding(8, 0, 0, 6);
            panel.Controls.Add(trailingControl, 1, 0);
        }

        bodyControl.Dock = DockStyle.None;
        bodyControl.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        bodyControl.Margin = new Padding(0);
        panel.Controls.Add(bodyControl, 0, 1);
        if (trailingControl is not null)
        {
            panel.SetColumnSpan(bodyControl, 2);
        }

        shell.Controls.Add(panel);
        return shell;
    }
    private Control BuildStatusPanel()
    {
        var stack = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = AppBackground,
            Margin = new Padding(0, 4, 0, 10),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var overviewCard = CreateRecorderSurfaceCard(autoSize: true);
        overviewCard.Margin = new Padding(0, 0, 0, 10);
        overviewCard.Padding = new Padding(18);

        var overviewLayout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = CardBackground,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
        };
        overviewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        overviewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        overviewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AddSectionTitle(overviewLayout, "Status & diagnostics", 0);
        overviewLayout.Controls.Add(CreateShellInfoLabel("Live status van recorder, worker, herstel, diagnostics en retentie. Technische details blijven zichtbaar, maar rustiger gegroepeerd."), 0, 1);

        var statusGrid = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 16,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
            BackColor = CardBackground,
        };
        statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172));
        statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 16; i++)
        {
            statusGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var workerProgressHost = new BufferedFlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = CardBackground,
        };
        workerProgressHost.Controls.Add(_workerProgressBar);

        AddStatusTableRow(statusGrid, 0, "Recorderstatus", _statusLabel);
        AddStatusTableRow(statusGrid, 1, "Actieve sessie", _sessionLabel);
        AddStatusTableRow(statusGrid, 2, "Microfoon", _selectedMicLabel);
        AddStatusTableRow(statusGrid, 3, "Systeemaudio", _selectedSystemLabel);
        AddStatusTableRow(statusGrid, 4, "Profiel", _profileLabel);
        AddStatusTableRow(statusGrid, 5, "Workerstatus", _workerStatusLabel);
        AddStatusTableRow(statusGrid, 6, "Queue status", _queueStatusLabel);
        AddStatusTableRow(statusGrid, 7, "Verwerkingsfase", _workerPhaseLabel);
        AddStatusTableRow(statusGrid, 8, "Worker-sessie", _workerSessionLabel);
        AddStatusTableRow(statusGrid, 9, "Voortgang", workerProgressHost);
        AddStatusTableRow(statusGrid, 10, "Workeractiviteit", _workerActivityLabel);
        AddStatusTableRow(statusGrid, 11, "Retentie", _retentionStatusLabel);
        AddStatusTableRow(statusGrid, 12, "Opname health", _recordingHealthLabel);
        AddStatusTableRow(statusGrid, 13, "Mic health", _micHealthLabel);
        AddStatusTableRow(statusGrid, 14, "System health", _systemHealthLabel);
        AddStatusTableRow(statusGrid, 15, "Recovery", _recoveryStatusLabel);
        overviewLayout.Controls.Add(statusGrid, 0, 2);
        overviewCard.Controls.Add(overviewLayout);
        stack.Controls.Add(overviewCard, 0, 0);

        var detailCard = CreateRecorderSurfaceCard(autoSize: true);
        detailCard.Padding = new Padding(18);
        var detailLayout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = CardBackground,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
        };
        detailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AddSectionTitle(detailLayout, "Sessiedetails", 0);
        _detailsTextBox.Margin = new Padding(0, 8, 0, 0);
        _detailsTextBox.Height = 220;
        detailLayout.Controls.Add(_detailsTextBox, 0, 1);
        detailCard.Controls.Add(detailLayout);
        stack.Controls.Add(detailCard, 0, 1);

        return stack;
    }

    private static void ApplySafeHorizontalSplitLayout(SplitContainer splitContainer, int preferredDistance, int preferredPanel1MinSize, int preferredPanel2MinSize)
    {
        if (splitContainer.IsDisposed)
        {
            return;
        }

        var available = splitContainer.ClientSize.Height - splitContainer.SplitterWidth;
        if (available <= 0)
        {
            return;
        }

        var panel1Min = Math.Max(0, Math.Min(preferredPanel1MinSize, Math.Max(0, available - 1)));
        var panel2Min = Math.Max(0, Math.Min(preferredPanel2MinSize, Math.Max(0, available - panel1Min)));
        if (panel1Min + panel2Min > available)
        {
            panel2Min = Math.Max(0, available - panel1Min);
        }

        try
        {
            if (splitContainer.Panel1MinSize != panel1Min)
            {
                splitContainer.Panel1MinSize = panel1Min;
            }

            if (splitContainer.Panel2MinSize != panel2Min)
            {
                splitContainer.Panel2MinSize = panel2Min;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var max = available - panel2Min;
        if (max < panel1Min)
        {
            return;
        }

        var clamped = Math.Max(panel1Min, Math.Min(preferredDistance, max));
        if (splitContainer.SplitterDistance == clamped)
        {
            return;
        }

        try
        {
            splitContainer.SplitterDistance = clamped;
        }
        catch (ArgumentOutOfRangeException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void ApplySafeVerticalSplitLayout(SplitContainer splitContainer, int preferredDistance, int preferredPanel1MinSize, int preferredPanel2MinSize)
    {
        if (splitContainer.IsDisposed)
        {
            return;
        }

        var available = splitContainer.ClientSize.Width - splitContainer.SplitterWidth;
        if (available <= 0)
        {
            return;
        }

        var panel1Min = Math.Max(0, Math.Min(preferredPanel1MinSize, Math.Max(0, available - 1)));
        var panel2Min = Math.Max(0, Math.Min(preferredPanel2MinSize, Math.Max(0, available - panel1Min)));
        if (panel1Min + panel2Min > available)
        {
            panel2Min = Math.Max(0, available - panel1Min);
        }

        try
        {
            if (splitContainer.Panel1MinSize != panel1Min)
            {
                splitContainer.Panel1MinSize = panel1Min;
            }

            if (splitContainer.Panel2MinSize != panel2Min)
            {
                splitContainer.Panel2MinSize = panel2Min;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var max = available - panel2Min;
        if (max < panel1Min)
        {
            return;
        }

        var clamped = Math.Max(panel1Min, Math.Min(preferredDistance, max));
        if (splitContainer.SplitterDistance == clamped)
        {
            return;
        }

        try
        {
            splitContainer.SplitterDistance = clamped;
        }
        catch (ArgumentOutOfRangeException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private Control BuildLibraryPanel()
    {
        var card = CreateRecorderSurfaceCard();
        card.Padding = new Padding(18);

        var layout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = CardBackground,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddSectionTitle(layout, "Sessiebibliotheek", 0);
        layout.Controls.Add(CreateShellInfoLabel("Zoeken en openen blijft sessiemap-gedreven. Tags en andere indeling blijven herleidbaar tot transcript en sessiemap."), 0, 1);
        layout.Controls.Add(BuildLibraryFilterRow(), 0, 2);

        var splitHost = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.None,
            IsSplitterFixed = false,
            Margin = new Padding(0),
            BackColor = CardBackground,
            BorderStyle = BorderStyle.None,
            TabStop = false,
            SplitterWidth = 8
        };
        var librarySplitInitialized = false;
        void ApplyLibrarySplitLayout(bool preferInitialRatio = false)
        {
            var preferredDistance = librarySplitInitialized && !preferInitialRatio
                ? splitHost.SplitterDistance
                : Math.Max(280, splitHost.ClientSize.Width * 32 / 100);

            ApplySafeVerticalSplitLayout(splitHost, preferredDistance, 240, 420);
            librarySplitInitialized = splitHost.ClientSize.Width > 0;
        }
        splitHost.HandleCreated += (_, _) => ApplyLibrarySplitLayout(preferInitialRatio: true);
        splitHost.SizeChanged += (_, _) => ApplyLibrarySplitLayout();

        var listHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            MinimumSize = new Size(240, 0)
        };
        _libraryListView.Dock = DockStyle.Fill;
        _libraryListView.Margin = new Padding(0);
        listHost.Controls.Add(_libraryListView);
        splitHost.Panel1.Padding = new Padding(0, 0, 8, 0);
        splitHost.Panel1.Controls.Add(listHost);

        var detailHost = BuildLibraryDetailPanel();
        detailHost.Dock = DockStyle.Fill;
        splitHost.Panel2.Padding = new Padding(8, 0, 0, 0);
        splitHost.Panel2.Controls.Add(detailHost);

        layout.Controls.Add(splitHost, 0, 3);

        card.Controls.Add(layout);
        return card;
    }

    private void AdjustLibraryListColumns()
    {
        if (_libraryListView.IsDisposed)
        {
            return;
        }

        var clientWidth = _libraryListView.ClientSize.Width;
        if (clientWidth <= 0 || _libraryListView.Columns.Count < 7)
        {
            return;
        }

        const int dateWidth = 132;
        const int sessionWidth = 230;
        const int sourceWidth = 92;
        const int contextWidth = 150;
        const int subcontextWidth = 160;
        const int statusWidth = 150;
        var fixedWidth = dateWidth + sessionWidth + sourceWidth + contextWidth + subcontextWidth + statusWidth;
        var fragmentWidth = Math.Max(220, clientWidth - fixedWidth - SystemInformation.VerticalScrollBarWidth - 8);

        _libraryListView.Columns[0].Width = dateWidth;
        _libraryListView.Columns[1].Width = sessionWidth;
        _libraryListView.Columns[2].Width = sourceWidth;
        _libraryListView.Columns[3].Width = contextWidth;
        _libraryListView.Columns[4].Width = subcontextWidth;
        _libraryListView.Columns[5].Width = statusWidth;
        _libraryListView.Columns[6].Width = fragmentWidth;
    }

    private void RefreshLibraryActionStates()
    {
        var selectedEntries = GetSelectedLibraryEntries();
        var entry = selectedEntries.FirstOrDefault();
        var selectionCount = selectedEntries.Count;
        var hasSelection = selectionCount > 0;
        var hasSingleSelection = selectionCount == 1;
        _libraryOpenFolderButton.Enabled = hasSingleSelection;
        _libraryOpenTranscriptButton.Enabled = hasSingleSelection && entry is not null && HasTranscriptFile(entry);
        _libraryCopyTranscriptButton.Enabled = hasSingleSelection && entry is not null && HasTranscriptFile(entry);
        _libraryExportSrtButton.Enabled = hasSingleSelection;
        _libraryExportVttButton.Enabled = hasSingleSelection;
        _libraryDeleteButton.Enabled = hasSingleSelection;
        _libraryEditTagsButton.Enabled = true;

        _libraryContextOpenTranscriptItem.Enabled = _libraryOpenTranscriptButton.Enabled;
        _libraryContextOpenFolderItem.Enabled = _libraryOpenFolderButton.Enabled;
        _libraryContextEditTitleItem.Enabled = hasSingleSelection;
        _libraryContextEditRecordedAtItem.Enabled = hasSingleSelection && entry is not null && string.Equals(entry.SourceType, "import", StringComparison.OrdinalIgnoreCase);
        _libraryContextCopyTranscriptItem.Enabled = _libraryCopyTranscriptButton.Enabled;
        _libraryContextExportSrtItem.Enabled = _libraryExportSrtButton.Enabled;
        _libraryContextExportVttItem.Enabled = _libraryExportVttButton.Enabled;
        _libraryContextDeleteItem.Enabled = _libraryDeleteButton.Enabled;
    }

    private static bool HasTranscriptFile(TranscriptLibraryEntry? entry)
    {
        if (entry is null)
        {
            return false;
        }

        return (!string.IsNullOrWhiteSpace(entry.TranscriptTextPath) && File.Exists(entry.TranscriptTextPath))
            || (!string.IsNullOrWhiteSpace(entry.TranscriptJsonPath) && File.Exists(entry.TranscriptJsonPath));
    }

    private void LibraryListView_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        var item = _libraryListView.GetItemAt(e.X, e.Y);
        if (item is not null)
        {
            item.Selected = true;
        }

        RefreshLibraryActionStates();
        _libraryContextMenu.Show(_libraryListView, e.Location);
    }

    private async Task LoadDevicesAsync(bool showMessageBoxOnFailure = true)
    {
        var deviceWatch = Stopwatch.StartNew();
        if (_session is null)
        {
            SetRecorderDeviceLoadingState(true);
        }

        try
        {
            var previousMicId = (_micComboBox.SelectedItem as DeviceInfo)?.Id ?? _preferredMicDeviceId;
            var previousSystemId = (_systemComboBox.SelectedItem as DeviceInfo)?.Id ?? _preferredSystemDeviceId;

            var micDevicesTask = Task.Run(DeviceCatalog.GetCaptureDevices);
            var systemDevicesTask = Task.Run(DeviceCatalog.GetRenderDevices);
            var deviceEnumerationTask = Task.WhenAll(micDevicesTask, systemDevicesTask);
            if (await Task.WhenAny(deviceEnumerationTask, Task.Delay(TimeSpan.FromSeconds(8))).ConfigureAwait(true) != deviceEnumerationTask)
            {
                throw new TimeoutException("Audioapparaten laden duurde langer dan 8 seconden. Probeer opnieuw via Instellingen, Opnemen, Opnieuw zoeken.");
            }

            var micDevices = await micDevicesTask.ConfigureAwait(true);
            var systemDevices = await systemDevicesTask.ConfigureAwait(true);

            _micComboBox.BeginUpdate();
            _systemComboBox.BeginUpdate();
            _micComboBox.DataSource = micDevices.ToList();
            _systemComboBox.DataSource = systemDevices.ToList();
            RestoreSelection(_micComboBox, previousMicId);
            RestoreSelection(_systemComboBox, previousSystemId);
            _micComboBox.EndUpdate();
            _systemComboBox.EndUpdate();

            _preferredMicDeviceId = (_micComboBox.SelectedItem as DeviceInfo)?.Id;
            _preferredSystemDeviceId = (_systemComboBox.SelectedItem as DeviceInfo)?.Id;
            UpdateSelectedDeviceLabels();
            SetStatus("Apparaten geladen.", $"Microfoons: {micDevices.Count}{Environment.NewLine}Render-devices: {systemDevices.Count}{Environment.NewLine}Geselecteerde microfoon: {_selectedMicLabel.Text}{Environment.NewLine}Geselecteerde systeemaudio: {_selectedSystemLabel.Text}");
        }
        catch (Exception ex)
        {
            SetStatus("Fout bij laden apparaten.", ex.ToString(), isError: true);
            if (showMessageBoxOnFailure)
            {
                ShowOwnedMessage(this, ex.Message, "Apparaten laden mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                _selectedMicLabel.Text = "Audioapparaten laden mislukt";
                _selectedSystemLabel.Text = "Gebruik Opnieuw zoeken onder Instellingen, Opnemen";
            }
        }
        finally
        {
            deviceWatch.Stop();
            SetRecorderDeviceLoadingState(false);

            AppRuntime.LogMessage($"Startup/device timing: audioapparaten laden afgerond in {deviceWatch.ElapsedMilliseconds} ms; bruikbaar={_audioDevicesLoaded}.");
        }
    }

    private async Task ToggleRecordingAsync()
    {
        if (_stopFinalizeInProgress)
        {
            SetStatus("Opname wordt nog afgerond.", "Wacht tot mix en READY afronden voltooid zijn voordat je opnieuw start of stopt.");
            return;
        }

        if (_session is null)
        {
            await StartRecordingAsync();
            return;
        }

        await StopRecordingAsync();
    }

    private async Task StartRecordingAsync()
    {
        if (_micComboBox.SelectedItem is not DeviceInfo mic || _systemComboBox.SelectedItem is not DeviceInfo system)
        {
            ShowOwnedMessage(this, "Selecteer eerst zowel een microfoon als een systeemaudio-device.", "Apparaten ontbreken", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var preflight = await Task.Run(() => PreflightCheckService.Run(_outputRootTextBox.Text, mic.Id, system.Id, _appSettings));
            SetStatus(preflight.Success ? (preflight.HasWarnings ? "Controle voor de opname: let op." : "Controle voor de opname: in orde.") : "Opnemen kan niet starten.", preflight.Details, isError: !preflight.Success || preflight.HasWarnings);
            // De technische details (paden, formaten, instellingen) alleen in het logboek; de gebruiker krijgt korte zinnen.
            if (!preflight.Success || preflight.HasWarnings)
            {
                AppRuntime.LogMessage("Controle voor de opname:" + Environment.NewLine + preflight.Details);
            }

            if (!preflight.Success)
            {
                var problems = preflight.UserErrors.Count > 0 ? preflight.UserErrors : new[] { "Er ging iets mis bij de controle voor de opname." };
                ShowOwnedMessage(this, string.Join(Environment.NewLine + Environment.NewLine, problems) + Environment.NewLine + Environment.NewLine + "De details staan in het logboek.", "Opnemen kan niet starten", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (preflight.HasWarnings && preflight.UserWarnings.Count > 0)
            {
                var result = ShowOwnedMessage(this, string.Join(Environment.NewLine + Environment.NewLine, preflight.UserWarnings) + Environment.NewLine + Environment.NewLine + "Toch opnemen?", "Opnemen", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result != DialogResult.Yes)
                {
                    return;
                }
            }

            var deviceProfile = AudioDeviceRiskAnalyzer.DetermineDeviceProfile(mic.FriendlyName, system.FriendlyName);
            var sameDeviceRisk = AudioDeviceRiskAnalyzer.IsSamePhysicalDeviceRisk(mic.FriendlyName, system.FriendlyName);
            var options = new AppOptions
            {
                OutputRoot = _outputRootTextBox.Text,
                MicDeviceId = mic.Id,
                SystemDeviceId = system.Id,
                WriteStems = false,
                CreateReadyMarker = true,
                DiagnosticsMode = _appSettings.Ui.DiagnosticsMode,
                MicGain = (double)_micGainNumeric.Value,
                SystemGain = (double)_systemGainNumeric.Value,
                DeviceProfile = deviceProfile,
                SameDeviceRisk = sameDeviceRisk,
            };

            SaveCurrentSettings();
            Directory.CreateDirectory(options.OutputRoot);

            var session = new AudioCaptureSession(options);
            session.LevelsUpdated += Session_LevelsUpdated;
            // Eerst het vorige overleg loslaten: anders geldt het tijdens het starten even als actief.
            _manifest = null;
            _session = session;
            _manifest = await session.StartAsync();
            _startedAt = DateTimeOffset.Now;
            _lastMicSignalAt = _startedAt.Value;
            _lastSystemSignalAt = _startedAt.Value;
            _lastSilenceNotificationAt = DateTimeOffset.MinValue;
            _lastCaptureFailureNotificationAt = DateTimeOffset.MinValue;
            _capacityWarningStage = 0;
            _systemSilenceAcknowledgedAt = null;
            _lastStopFailed = false;
            _recordingStopwatch.Restart();
            _timer.Start();
            _notificationPollTimer.Start();
            // Vanaf hier loopt de opname. Een fout in het bijwerken van het scherm loggen, niet de opname weggooien.
            try
            {
                UpdateElapsed();
                ResetMeters();
                SetRunningState(true);
                SetStatus("Opname loopt.", $"Sessie: {_manifest.MeetingId}{Environment.NewLine}Mic: {_manifest.MicDevice}{Environment.NewLine}System: {_manifest.SystemDevice}{Environment.NewLine}Map: {_manifest.OutputDirectory}");
                UpdateRecordingHealthLabels("Healthy", "Signaal wordt bewaakt", "Signaal wordt bewaakt", "Monitoring actief");
            }
            catch (Exception uiException)
            {
                AppRuntime.LogException("Scherm bijwerken na opnamestart mislukt; de opname loopt door", uiException);
            }

            _notifyIcon.ShowBalloonTip(2500, Branding.DisplayName, UiText.T("Opname gestart."), ToolTipIcon.Info);
            ScheduleLibraryRefreshAfterRecordingStateChange("opname gestart");
        }
        catch (Exception ex)
        {
            if (_session is not null)
            {
                _session.LevelsUpdated -= Session_LevelsUpdated;
            }

            _session?.Dispose();
            _session = null;
            _manifest = null;
            _startedAt = null;
            _recordingStopwatch.Reset();
            _timer.Stop();
            UpdateElapsed();
            ResetMeters();
            SetRunningState(false);
            SetStatus("Starten mislukt.", ex.ToString(), isError: true);
            ShowOwnedMessage(this, ex.ToString(), "Opname starten mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ScheduleLibraryRefreshAfterRecordingStateChange(string reason)
    {
        if (!_libraryPanelBuilt)
        {
            return;
        }

        _libraryRecordingRefreshCts?.Cancel();
        _libraryRecordingRefreshCts?.Dispose();
        var cts = new CancellationTokenSource();
        _libraryRecordingRefreshCts = cts;

        _ = Task.Run(async () =>
        {
            var delays = new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8) };
            foreach (var delay in delays)
            {
                try
                {
                    await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                    if (cts.IsCancellationRequested || IsDisposed || Disposing || !IsHandleCreated)
                    {
                        return;
                    }

                    BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (_libraryPanelBuilt)
                            {
                                _ = RunLibraryUiActionAsync($"Sessiebibliotheek verversen na {reason}", ct => RefreshLibraryAsync(ct));
                            }
                        }
                        catch (Exception ex)
                        {
                            AppRuntime.LogException("Scheduled library refresh failed", ex);
                        }
                    }));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppRuntime.LogException("Scheduled library refresh loop failed", ex);
                    return;
                }
            }
        });
    }

    private async Task RunPreflightCheckAsync()
    {
        var mic = _micComboBox.SelectedItem as DeviceInfo;
        var system = _systemComboBox.SelectedItem as DeviceInfo;
        var report = await Task.Run(() => PreflightCheckService.Run(_outputRootTextBox.Text, mic?.Id, system?.Id, _appSettings));
        var summary = report.Success
            ? (report.HasWarnings ? "Preflight geslaagd met waarschuwingen." : "Preflight geslaagd.")
            : "Preflight mislukt.";

        SetStatus(summary, report.Details, isError: !report.Success || report.HasWarnings);
        ShowOwnedMessage(this, report.Details, summary, MessageBoxButtons.OK, report.Success ? (report.HasWarnings ? MessageBoxIcon.Warning : MessageBoxIcon.Information) : MessageBoxIcon.Error);
    }

    private void OpenWindowsSoundSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:sound",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Open sound settings failed", ex);
            ShowOwnedMessage(this, ex.Message, "Geluidsinstellingen openen mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task StopRecordingAsync()
    {
        if (_session is null || _stopFinalizeInProgress)
        {
            return;
        }

        var session = _session;
        _session = null;
        _stopFinalizeInProgress = true;
        _lastStopFailed = false;
        _recordToggleButton.Enabled = false;
        session.LevelsUpdated -= Session_LevelsUpdated;

        _timer.Stop();
        _recordingStopwatch.Reset();
        _startedAt = null;
        UpdateElapsed();
        ResetMeters();

        SetRunningState(false);
        _recordToggleButton.Text = "Opname afronden...";
        _recordToggleButton.VisualState = PrimaryActionVisualState.Busy;
        _recordToggleButton.Enabled = false;
        _trayToggleRecordItem.Text = UiText.T("Opname afronden...");
        _trayToggleRecordItem.Enabled = false;

        SetStatus("Opname wordt gestopt...", "Capture wordt netjes afgerond en gemixt. De interface blijft beschikbaar tijdens afronden.");
        UpdateRecordingHealthLabels("Wordt afgerond", "Wordt afgerond", "Wordt afgerond", "Mixen en READY schrijven");

        _ = FinalizeStopRecordingAsync(session);
        await Task.Yield();
    }

    private async Task FinalizeStopRecordingAsync(AudioCaptureSession session)
    {
        SessionManifest? completed = null;
        Exception? failure = null;

        try
        {
            completed = await session.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex;
            AppRuntime.LogException("Stop recording failed", ex);
        }

        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(new Action(() => CompleteStopRecordingOnUi(completed, failure)));
    }

    private void CompleteStopRecordingOnUi(SessionManifest? completed, Exception? failure)
    {
        try
        {
            if (failure is null && completed is not null)
            {
                _manifest = completed;
                SetStatus("Opname afgerond.", BuildCompletionDetails(completed));
                UpdateRecordingHealthLabels("Idle", "Niet actief", "Niet actief", "Wacht op transcriptie");
                _notifyIcon.ShowBalloonTip(2500, Branding.DisplayName, UiText.T("Opname afgerond."), ToolTipIcon.Info);
                if (_appSettings.Worker.WhisperXEnabled)
                {
                    _ = _notificationDispatcher.NotifyAsync(NotificationEventType.RecordingStoppedWaitingForTranscription, "Opname bewaard", "Captivox schrijft het overleg nu uit.", ToolTipIcon.Info);
                }

                _ = PollBackgroundStateAsync();
                ScheduleLibraryRefreshAfterRecordingStateChange("opname afgerond");
            }
            else
            {
                _lastStopFailed = true;
                var detail = failure?.ToString() ?? "Onbekende fout tijdens afronden van de opname.";
                SetStatus("Stoppen of mixen mislukt.", detail, isError: true);
                ShowOwnedMessage(this, detail, "Opname stoppen mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            _stopFinalizeInProgress = false;
            SetRunningState(false);

            if (_closeAfterFinalize)
            {
                var shouldClose = !_lastStopFailed;
                _closeAfterFinalize = false;
                if (shouldClose)
                {
                    Close();
                }
            }
        }
    }

    private string BuildCompletionDetails(SessionManifest completed)
    {
        var lines = new List<string>
        {
            $"Sessie: {completed.MeetingId}",
            $"Bestand: {completed.OutputFile}",
            $"Status: {completed.Status}",
        };

        if (completed.MixMetrics is not null)
        {
            lines.Add($"Peak na limiter: {completed.MixMetrics.MixPeakAfterLimiting:F2}");
            lines.Add($"Gem. mic gain: {completed.MixMetrics.AverageMicGain:F2}");
            lines.Add($"Gem. system gain: {completed.MixMetrics.AverageSystemGain:F2}");
            lines.Add($"Ducked windows: {completed.MixMetrics.DuckedWindows}");
            lines.Add($"Noise-gated windows: {completed.MixMetrics.NoiseGatedWindows}");
        }

        if (!string.IsNullOrWhiteSpace(completed.TranscriptionStatus))
        {
            lines.Add($"Transcriptiestatus: {completed.TranscriptionStatus}");
        }

        if (completed.CaptureDiagnostics is not null)
        {
            lines.Add($"Mic first callback lag: {completed.CaptureDiagnostics.MicFirstCallbackLagMs:F0} ms");
            lines.Add($"System first callback lag: {completed.CaptureDiagnostics.SystemFirstCallbackLagMs:F0} ms");
            lines.Add($"Mic inserted silence: {completed.CaptureDiagnostics.MicInsertedSilenceMs:F0} ms");
            lines.Add($"System inserted silence: {completed.CaptureDiagnostics.SystemInsertedSilenceMs:F0} ms");
            if (completed.CaptureDiagnostics.StopTimeoutHit)
            {
                lines.Add($"Stop-timeout geraakt: {completed.CaptureDiagnostics.StopTimeoutSeconds}s");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void SetRunningState(bool isRunning)
    {
        _headerSignalRibbon.IsRecording = isRunning;

        if (isRunning)
        {
            _recordToggleButton.Text = "Opname stoppen";
            _recordToggleButton.VisualState = PrimaryActionVisualState.Stop;
            _recordToggleButton.Enabled = true;
        }
        else if (_recordingBusy)
        {
            _recordToggleButton.Text = "Opname voorbereiden...";
            _recordToggleButton.VisualState = PrimaryActionVisualState.Busy;
            _recordToggleButton.Enabled = false;
        }
        else if (_audioDeviceLoading)
        {
            _recordToggleButton.Text = "Opname starten";
            _recordToggleButton.VisualState = PrimaryActionVisualState.Start;
            _recordToggleButton.Enabled = false;
        }
        else
        {
            _recordToggleButton.Text = "Opname starten";
            _recordToggleButton.VisualState = PrimaryActionVisualState.Start;
            _recordToggleButton.Enabled = CanStartRecordingNow();
        }

        _recordToggleButton.ForeColor = Color.White;

        _browseButton.Enabled = !isRunning;
        _refreshDevicesButton.Enabled = !isRunning;
        _micComboBox.Enabled = !isRunning;
        _systemComboBox.Enabled = !isRunning;
        _micGainNumeric.Enabled = !isRunning;
        _systemGainNumeric.Enabled = !isRunning;

        // De schermlezer hoort wat de knop nu doet; de sneltoets staat erbij.
        _recordToggleButton.AccessibleName = UiText.T(_recordToggleButton.Text);
        SetUiToolTip(_recordToggleButton, UiText.F("{0} (Ctrl+Shift+F9)", UiText.T(_recordToggleButton.Text)));
        _trayToggleRecordItem.Text = UiText.T(isRunning ? "Opname stoppen" : "Opname starten");
        _trayToggleRecordItem.Enabled = _recordToggleButton.Enabled;
        _trayShowHideItem.Text = UiText.T(Visible ? "Venster verbergen" : "Venster tonen");
        UpdateShellRecordingState(isRunning);
    }

    private async Task RunBusyUiActionAsync(string actionName, Func<Task> action)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        UseWaitCursor = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AppRuntime.LogException($"UI action failed: {actionName}", ex);
            throw;
        }
        finally
        {
            ResetUiWaitCursorState();
            _isBusy = false;
        }
    }

    private async Task RunRecordingUiActionAsync(string actionName, Func<Task> action)
    {
        if (_recordingBusy)
        {
            return;
        }

        _recordingBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AppRuntime.LogException($"Recording UI action failed: {actionName}", ex);
            throw;
        }
        finally
        {
            _recordingBusy = false;
            ResetUiWaitCursorState();
            SetRunningState(_session is not null);
        }
    }

    private Task RunLibraryUiActionAsync(string actionName, Func<Task> action)
        => RunLibraryUiActionAsync(actionName, _ => action());

    private async Task RunLibraryUiActionAsync(string actionName, Func<CancellationToken, Task> action)
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _libraryRefreshCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        SetLibraryBusyState(true);
        try
        {
            await action(cts.Token);
        }
        catch (OperationCanceledException)
        {
            AppRuntime.LogMessage($"Library UI action cancelled: {actionName}");
        }
        catch (Exception ex)
        {
            AppRuntime.LogException($"Library UI action failed: {actionName}", ex);
            throw;
        }
        finally
        {
            if (ReferenceEquals(_libraryRefreshCts, cts))
            {
                _libraryRefreshCts = null;
                SetLibraryBusyState(false);
            }

            cts.Dispose();
        }
    }

    private void ScheduleLibrarySearchRefresh()
    {
        var previous = Interlocked.Exchange(ref _librarySearchDebounceCts, new CancellationTokenSource());
        previous?.Cancel();
        previous?.Dispose();

        var cts = _librarySearchDebounceCts;
        if (cts is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, cts.Token).ConfigureAwait(false);
                if (cts.IsCancellationRequested || IsDisposed)
                {
                    return;
                }

                BeginInvoke(new Action(() => _ = RunLibraryUiActionAsync("Sessiebibliotheek zoeken", ct => RefreshLibraryAsync(ct))));
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException ex)
            {
                AppRuntime.LogException("Library search debounce invoke failed", ex);
            }
            finally
            {
                if (ReferenceEquals(_librarySearchDebounceCts, cts))
                {
                    _librarySearchDebounceCts = null;
                }
            }
        });
    }

    private void SetLibraryBusyState(bool isBusy)
    {
        if (_libraryPanelBuilt)
        {
            _libraryRefreshButton.Enabled = !isBusy;
            _libraryResetFiltersButton.Enabled = !isBusy;
        }
    }

    private void ResetUiWaitCursorState()
    {
        UseWaitCursor = false;
        Cursor = Cursors.Default;
        Cursor.Current = Cursors.Default;
        Application.UseWaitCursor = false;
    }

    private void LoadPersistedSettings()
    {
        _appSettings = SettingsStore.Load();
        SettingsStore.RemoveLegacyToken();
        _appSettings.Worker.RuntimeMode = "python";
        _outputRootTextBox.Text = _appSettings.Ui.OutputRoot;
        _micGainNumeric.Value = (decimal)Math.Clamp(_appSettings.Ui.MicGain, 0.10, 4.0);
        _systemGainNumeric.Value = (decimal)Math.Clamp(_appSettings.Ui.SystemGain, 0.10, 4.0);
        _appSettings.Ui.CreateReadyMarker = true;
        _minimizeToTrayCheckbox.Checked = _appSettings.Ui.MinimizeToTray;
        _appSettings.Ui.Monitoring ??= new MonitoringSettings();
        _appSettings.Ui.Notifications ??= new NotificationSettings();
        _preferredMicDeviceId = _appSettings.Ui.PreferredMicDeviceId;
        _preferredSystemDeviceId = _appSettings.Ui.PreferredSystemDeviceId;
    }

    private void ApplyHardwareAssessmentToSettings(HardwareAssessmentResult assessment, bool persist)
    {
        _appSettings.Hardware ??= new HardwareProfileSettings();
        _appSettings.Hardware.StartupDetectionCompleted = true;
        _appSettings.Hardware.LastDetectedAtUtc = assessment.DetectedAtUtc;
        _appSettings.Hardware.LastGpuVendor = assessment.GpuVendor;
        _appSettings.Hardware.LastGpuName = assessment.GpuName;
        _appSettings.Hardware.LastVramGb = assessment.VramGb;
        _appSettings.Hardware.LastVramConfidence = assessment.VramConfidence;
        _appSettings.Hardware.LastDetectionMethod = assessment.DetectionMethod;
        _appSettings.Hardware.LastRecommendedTranscriptionProfile = assessment.RecommendedTranscriptionProfile;
        _appSettings.Hardware.LastRecommendedModelTag = assessment.RecommendedModelTag;
        _appSettings.Hardware.LastAdviceClass = assessment.AdviceClass;
        _appSettings.Hardware.LastGpuRuntimeStatus = "profile_selected_runtime_validated_by_worker";
        _appSettings.Hardware.LastFallbackReason = assessment.TranscriptionProfileReason;

        var detectedProfile = HardwareAssessmentService.NormalizeTranscriptionProfile(assessment.RecommendedTranscriptionProfile);
        // De zelf gekozen snelle AMD-route blijft staan.
        if (!_appSettings.Worker.AmdGpuFastRoute && !string.IsNullOrWhiteSpace(detectedProfile) && !string.Equals(detectedProfile, "auto", StringComparison.OrdinalIgnoreCase))
        {
            _appSettings.Worker.TranscriptionProfile = detectedProfile;
        }

        if (persist)
        {
            SettingsStore.Save(_appSettings);
            EnsureWorkerProcessState();
        }
    }

    private async Task RunStartupHardwareDetectionAsync()
    {
        var currentProfile = HardwareAssessmentService.NormalizeTranscriptionProfile(_appSettings.Worker.TranscriptionProfile);
        if (_appSettings.Hardware?.StartupDetectionCompleted == true && !string.Equals(currentProfile, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var assessment = await Task.Run(() => HardwareAssessmentService.Detect(_appSettings.Ui.LanguageProfile)).ConfigureAwait(true);
        ApplyHardwareAssessmentToSettings(assessment, persist: true);
        AppRuntime.LogMessage($"Hardwareprofiel bij startup toegepast: vendor={assessment.GpuVendor}; gpu={assessment.GpuName ?? "unknown"}; vram={assessment.VramGb:0.#}GB; profiel={assessment.RecommendedTranscriptionProfile}; methode={assessment.DetectionMethod}; betrouwbaarheid={assessment.VramConfidence}.");
    }

    private void SaveCurrentSettings()
    {
        _appSettings.Ui.OutputRoot = _outputRootTextBox.Text.Trim();
        _editionService.ApplyEditionPolicy(_appSettings, _editionState);
        _appSettings.Ui.PreferredMicDeviceId = (_micComboBox.SelectedItem as DeviceInfo)?.Id ?? _preferredMicDeviceId;
        _appSettings.Ui.PreferredSystemDeviceId = (_systemComboBox.SelectedItem as DeviceInfo)?.Id ?? _preferredSystemDeviceId;
        _appSettings.Ui.MicGain = (double)_micGainNumeric.Value;
        _appSettings.Ui.SystemGain = (double)_systemGainNumeric.Value;
        _appSettings.Ui.CreateReadyMarker = true;
        _appSettings.Ui.MinimizeToTray = _minimizeToTrayCheckbox.Checked;
        _appSettings.Worker.KnownSessionRoots ??= new List<string>();
        if (!string.IsNullOrWhiteSpace(_appSettings.Ui.OutputRoot))
        {
            _appSettings.Worker.KnownSessionRoots.Add(_appSettings.Ui.OutputRoot);
        }
        SettingsStore.NormalizeKnownSessionRoots(_appSettings);
        SettingsStore.Save(_appSettings);
        EnsureWindowsStartupRegistration();
        UpdateMeetingDetectionTimerState();
        EnsureWorkerProcessState();
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(new Action(() => _ = PollBackgroundStateAsync()));
        }
    }

    private string? GetWorkerScriptPath()
    {
        var expectedVersion = AppRuntime.GetWorkerCompatibilityVersionTag();
        var candidates = new List<string?>
        {
            Path.Combine(AppContext.BaseDirectory, "workers", Branding.WorkerScriptName),
            AppRuntime.FindProjectFile(Path.Combine("workers", Branding.WorkerScriptName))
        };

        foreach (var candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var workerVersion = TryReadWorkerVersionForScript(candidate);
            if (string.Equals(workerVersion, expectedVersion, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            AppRuntime.LogMessage($"Worker-script overgeslagen vanwege versiemismatch: {candidate} heeft '{workerVersion ?? "unknown"}', app verwacht '{expectedVersion}'.");
        }

        AppRuntime.LogMessage($"Geen worker-script gevonden met verwachte versie {expectedVersion}; worker wordt niet gestart om stale verwerking te voorkomen.");
        return null;
    }

    private static string? TryReadWorkerVersionForScript(string workerScriptPath)
    {
        try
        {
            var versionPath = Path.Combine(Path.GetDirectoryName(workerScriptPath) ?? string.Empty, "worker.version.txt");
            if (!File.Exists(versionPath))
            {
                return null;
            }

            return File.ReadLines(versionPath, Encoding.UTF8).FirstOrDefault()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static readonly HashSet<int> OrphanWorkerKillFailures = new();

    private static readonly string WorkerScriptPattern = Regex.Escape(Branding.WorkerScriptName);

    private static void StopStaleReadyWorkerProcesses(string currentWorkerScript)
    {
        try
        {
            var expectedVersion = AppRuntime.GetWorkerCompatibilityVersionTag();
            var currentScriptFullPath = Path.GetFullPath(currentWorkerScript);
            var isolatedDataRoot = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Branding.AppDataOverrideVariable))
                ? null
                : AppRuntime.GetAppDataDirectory().TrimEnd('\\');
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'powershell.exe' OR Name = 'pwsh.exe'");
            using var results = searcher.Get();
            foreach (ManagementObject processInfo in results)
            {
                var processId = Convert.ToInt32(processInfo["ProcessId"] ?? 0);
                if (processId <= 0 || processId == Environment.ProcessId)
                {
                    continue;
                }

                var commandLine = Convert.ToString(processInfo["CommandLine"] ?? string.Empty) ?? string.Empty;
                if (commandLine.IndexOf(Branding.WorkerScriptName, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                // Een testexemplaar met een eigen gegevensmap ruimt alleen workers van die map op, nooit die van de echte installatie.
                if (isolatedDataRoot is not null && commandLine.IndexOf(isolatedDataRoot, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var processWorkerScript = TryExtractReadyWorkerScriptPath(commandLine);
                var processWorkerVersion = !string.IsNullOrWhiteSpace(processWorkerScript)
                    ? TryReadWorkerVersionForScript(processWorkerScript)
                    : null;

                var sameScript = !string.IsNullOrWhiteSpace(processWorkerScript)
                    && string.Equals(Path.GetFullPath(processWorkerScript), currentScriptFullPath, StringComparison.OrdinalIgnoreCase);

                var sameVersion = string.Equals(processWorkerVersion, expectedVersion, StringComparison.OrdinalIgnoreCase);

                if (sameScript && sameVersion)
                {
                    // Een losgeraakte READY-monitor (de recorder werd hard gestopt, bijvoorbeeld bij een installatie) houdt
                    // de vergrendeling vast met oude code in het geheugen; de versie op schijf zegt daar niets over. Elke nieuwe worker
                    // stopte dan meteen en de recorder startte hem elke tien seconden opnieuw. Er draait maar een recorder en die
                    // start hier zijn eigen monitor, dus elke andere monitor is verouderd. Een sessierunner (-ProcessSessionDir) maakt
                    // zijn transcriptie af; daarom alleen de monitor zelf stoppen, niet zijn kinderen.
                    // Alleen een monitor die als script draait (-File); geen zelftest, losse run of ander proces dat het pad noemt.
                    if (commandLine.IndexOf("-ProcessSessionDir", StringComparison.OrdinalIgnoreCase) >= 0
                        || commandLine.IndexOf("-SelfTest", StringComparison.OrdinalIgnoreCase) >= 0
                        || commandLine.IndexOf("-RunOnce", StringComparison.OrdinalIgnoreCase) >= 0
                        || !Regex.IsMatch(commandLine, @"(?i)-File\s+(?:""[^""]*" + WorkerScriptPattern + @"""|'[^']*" + WorkerScriptPattern + @"'|[^\s""']*" + WorkerScriptPattern + @")(?=\s|$)"))
                    {
                        continue;
                    }

                    try
                    {
                        using var orphan = Process.GetProcessById(processId);
                        orphan.Kill(entireProcessTree: false);
                        // Wachten tot de vergrendeling vrij is, anders stopt de nieuwe worker nog een keer meteen.
                        AppRuntime.LogMessage(orphan.WaitForExit(5000)
                            ? $"Losgeraakte READY-worker beeindigd voor start actuele worker. PID={processId}."
                            : $"Losgeraakte READY-worker gestopt, maar na 5 seconden nog niet afgesloten. PID={processId}.");
                    }
                    catch (Exception ex)
                    {
                        // Een keer per proces melden (bijvoorbeeld geen toegang); anders elke tien seconden opnieuw.
                        if (OrphanWorkerKillFailures.Add(processId)) AppRuntime.LogException($"Losgeraakte READY-worker kon niet worden beeindigd. PID={processId}", ex);
                    }

                    continue;
                }

                try
                {
                    using var process = Process.GetProcessById(processId);
                    process.Kill(entireProcessTree: true);
                    AppRuntime.LogMessage($"Stale READY-worker beeindigd voor start actuele worker. PID={processId}; workerVersie={processWorkerVersion ?? "unknown"}; verwacht={expectedVersion}; workerpad={processWorkerScript ?? "unknown"}.");
                }
                catch (Exception ex)
                {
                    AppRuntime.LogException($"Stale READY-worker kon niet worden beeindigd. PID={processId}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Stale READY-worker cleanup overgeslagen", ex);
        }
    }

    private static string? TryExtractReadyWorkerScriptPath(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        try
        {
            var match = Regex.Match(commandLine, "(?i)\\-File\\s+\\\"(?<path>[^\\\"]*" + WorkerScriptPattern + ")\\\"|(?<path>\\S*" + WorkerScriptPattern + ")");
            if (!match.Success)
            {
                return null;
            }

            var path = match.Groups["path"].Value;
            return string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        }
        catch
        {
            return null;
        }
    }

    private void StopWorkerProcess()
    {
        Process? process;
        lock (_workerProcessSync)
        {
            process = _workerProcess;
            _workerProcess = null;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            var hasExited = false;
            try
            {
                hasExited = process.HasExited;
            }
            catch (InvalidOperationException)
            {
                hasExited = true;
            }

            if (!hasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    hasExited = true;
                }

                if (!hasExited)
                {
                    try
                    {
                        process.WaitForExit(5000);
                    }
                    catch (InvalidOperationException)
                    {
                        // Process object verloor zijn OS-koppeling terwijl de worker al aan het afsluiten was.
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Stopping background worker failed", ex);
        }
        finally
        {
            try
            {
                process.Dispose();
            }
            catch
            {
            }
        }
    }

    private void EnsureWorkerProcessState()
    {
        try
        {
            if (!_appSettings.Worker.WhisperXEnabled)
            {
                StopWorkerProcess();
                return;
            }

            Process? currentWorker;
            lock (_workerProcessSync)
            {
                currentWorker = _workerProcess;
            }

            if (currentWorker is not null)
            {
                var workerAlive = false;
                try
                {
                    workerAlive = !currentWorker.HasExited;
                }
                catch (InvalidOperationException)
                {
                    workerAlive = false;
                }

                if (workerAlive)
                {
                    return;
                }

                try
                {
                    currentWorker.Dispose();
                }
                catch
                {
                }

                lock (_workerProcessSync)
                {
                    if (ReferenceEquals(_workerProcess, currentWorker))
                    {
                        _workerProcess = null;
                    }
                }
            }

            var now = DateTimeOffset.UtcNow;
            if (now - _lastWorkerStartAttemptAt < WorkerRestartCooldown)
            {
                return;
            }

            var workerScript = GetWorkerScriptPath();
            if (string.IsNullOrWhiteSpace(workerScript) || !File.Exists(workerScript))
            {
                AppRuntime.LogMessage("Background worker niet gestart: worker-script ontbreekt of versie komt niet overeen.");
                return;
            }

            StopStaleReadyWorkerProcesses(workerScript);

            var settingsPath = SettingsStore.GetSettingsPath();
            // De worker krijgt de gegevensmap van deze smaak als parameter mee en leest geen gegevensmap uit de omgeving,
            // zodat hij nooit de map van een andere Captivox-installatie gebruikt.
            var appDataRoot = AppRuntime.GetAppDataDirectory().TrimEnd('\\');
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{workerScript}\" -SettingsPath \"{settingsPath}\" -AppDataRoot \"{appDataRoot}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(workerScript) ?? AppContext.BaseDirectory,
            };

            var helperExePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(helperExePath) || !File.Exists(helperExePath))
            {
                helperExePath = Path.Combine(AppContext.BaseDirectory, Branding.ExecutableBaseName + ".exe");
            }

            // De worker draait in Windows PowerShell 5.1. Een PSModulePath die is geerfd van PowerShell 7 (recorder gestart
            // vanuit pwsh) verbergt Get-FileHash, waardoor een correct transcript de uitvoercontrole niet haalt. Laat 5.1
            // zijn eigen pad opbouwen.
            startInfo.Environment.Remove("PSModulePath");
            startInfo.Environment["CAPTIVOX_DEFAULT_OUTPUT_ROOT"] = Branding.DefaultRecordingsRoot;
            startInfo.Environment["CAPTIVOX_EXPECTED_VERSION"] = AppRuntime.GetWorkerCompatibilityVersionTag();
            startInfo.Environment["CAPTIVOX_WORKER_SCRIPT"] = workerScript;
            // Houd de optionele ondertitel-export gelijk aan de actuele instellingen. De worker leest ook
            // application_settings.json, maar een worker die de app start mag een net opgeslagen instelling niet missen.
            startInfo.Environment["CAPTIVOX_EXPORT_SRT"] = _appSettings.Worker.ExportSrt ? "true" : "false";
            startInfo.Environment["CAPTIVOX_EXPORT_VTT"] = _appSettings.Worker.ExportVtt ? "true" : "false";

            if (!string.IsNullOrWhiteSpace(helperExePath) && File.Exists(helperExePath))
            {
                startInfo.Environment["CAPTIVOX_TOKEN_HELPER_EXE"] = helperExePath;
                startInfo.Environment["CAPTIVOX_IMPORT_HELPER_EXE"] = helperExePath;
                startInfo.Environment["CAPTIVOX_RECORDER_EXE"] = helperExePath;
            }

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            process.Exited += (_, _) =>
            {
                string exitMessage;
                try
                {
                    var exitCode = process.ExitCode;
                    exitMessage = exitCode == 0
                        ? "Background worker exited with code 0; READY-monitor is netjes gestopt." // geen vertaling
                        : $"Background worker exited with code {exitCode}; app-monitor start de READY-worker opnieuw indien verwerking nodig blijft. Controleer worker.log/diagnostics alleen als sessies niet verdergaan."; // geen vertaling
                }
                catch (InvalidOperationException)
                {
                    exitMessage = "Background worker exited; exitcode was niet meer opvraagbaar. App-monitor probeert de READY-worker beschikbaar te houden."; // geen vertaling
                }

                AppRuntime.LogMessage(exitMessage);
                lock (_workerProcessSync)
                {
                    if (ReferenceEquals(_workerProcess, process))
                    {
                        _workerProcess = null;
                    }
                }
            };

            _lastWorkerStartAttemptAt = now;
            if (!process.Start())
            {
                AppRuntime.LogMessage("Background worker kon niet worden gestart.");
                return;
            }

            lock (_workerProcessSync)
            {
                _workerProcess = process;
            }
            AppRuntime.LogMessage("Background worker gestart.");
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Starting background worker failed", ex);
        }
    }

    private string GetEffectiveWorkerProfileSummary()
        => WorkerProductionProfile.BuildSummary(_appSettings);

    private static string GetDisplayVersion()
    {
        return AppRuntime.GetProductVersionTag();
    }

    private static IWin32Window? ResolveDialogOwner(IWin32Window? owner)
    {
        if (owner is Control control)
        {
            if (control.IsDisposed || !control.IsHandleCreated)
            {
                return null;
            }
        }

        return owner;
    }

    // Bij een schrijfbevestiging staat de standaardknop op Nee (een losse Enter mag niets doorvoeren).
    // De tekst en de titel in de taal van de interface (een samengestelde tekst regel voor regel, zie UiText).
    private static DialogResult ShowOwnedMessage(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
    {
        text = UiText.T(text);
        caption = UiText.T(caption);
        var resolvedOwner = ResolveDialogOwner(owner);
        return resolvedOwner is null
            ? MessageBox.Show(text, caption, buttons, icon, defaultButton)
            : MessageBox.Show(resolvedOwner, text, caption, buttons, icon, defaultButton);
    }

    // Een tip bij een onderdeel, in de taal van de interface.
    private void SetUiToolTip(Control control, string? text)
        => _toolTip.SetToolTip(control, string.IsNullOrEmpty(text) ? string.Empty : UiText.T(text));

    private static DialogResult ShowOwnedMessage(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        text = UiText.T(text);
        caption = UiText.T(caption);
        var resolvedOwner = ResolveDialogOwner(owner);
        return resolvedOwner is null
            ? MessageBox.Show(text, caption, buttons, icon)
            : MessageBox.Show(resolvedOwner, text, caption, buttons, icon);
    }

    private static void ResetWorkerSelfTestArtifacts()
    {
        var artifactsDirectory = AppRuntime.GetSelfTestDirectory();
        if (!Directory.Exists(artifactsDirectory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(artifactsDirectory))
        {
            File.Delete(file);
        }
    }

    private async Task RunWorkerConnectionTestAsync(IWin32Window owner)
    {
        SaveCurrentSettings();

        var helperScript = AppRuntime.FindProjectFile(Path.Combine("workers", "transcribe_with_whisperx_recorder.py"));
        if (string.IsNullOrWhiteSpace(helperScript) || !File.Exists(helperScript))
        {
            ShowOwnedMessage(owner, TranscriptionRuntimeStatus.MissingMessage + Environment.NewLine + Environment.NewLine + "Het transcriptiescript ontbreekt in de programmamap.", "Verbindingstest mislukt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var pythonExe = _appSettings.Worker.PythonExe?.Trim();
        if (string.IsNullOrWhiteSpace(pythonExe) || !File.Exists(pythonExe))
        {
            ShowOwnedMessage(owner, $"{TranscriptionRuntimeStatus.MissingMessage}{Environment.NewLine}{Environment.NewLine}Python niet gevonden: {pythonExe}", "Verbindingstest mislukt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var connectionTestTranscriptionProfile = HardwareAssessmentService.NormalizeTranscriptionProfile(_appSettings.Worker.TranscriptionProfile);
        if (connectionTestTranscriptionProfile == "auto" && _appSettings.Hardware?.StartupDetectionCompleted == true)
        {
            connectionTestTranscriptionProfile = HardwareAssessmentService.NormalizeTranscriptionProfile(_appSettings.Hardware.LastRecommendedTranscriptionProfile);
        }
        var connectionTestUsesAmd = connectionTestTranscriptionProfile == "amd-gpu";

        string? currentToken = null;

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = pythonExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(helperScript) ?? AppContext.BaseDirectory
        };

        process.StartInfo.ArgumentList.Add(helperScript);
        process.StartInfo.ArgumentList.Add("--self-test");
        process.StartInfo.ArgumentList.Add("--self-test-mode");
        process.StartInfo.ArgumentList.Add("quick");
        process.StartInfo.ArgumentList.Add("--language");
        process.StartInfo.ArgumentList.Add(_appSettings.Worker.Language);
        var effectiveTranscriptionProfile = connectionTestTranscriptionProfile;
        var isAmdTranscriptionProfile = effectiveTranscriptionProfile == "amd-gpu";
        var effectiveComputeType = isAmdTranscriptionProfile ? "ffmpeg-whisper" : WorkerProductionProfile.GetEffectiveComputeType(_appSettings.Worker);
        var effectiveBatchSize = isAmdTranscriptionProfile ? 1 : _appSettings.Worker.BatchSize;
        var effectiveDiarizationEnabled = Branding.IsPro && _appSettings.Worker.DiarizationEnabled;
        var effectiveDiarizationModel = effectiveDiarizationEnabled ? WorkerProductionProfile.GetEffectiveDiarizationModel(_appSettings.Worker, effectiveTranscriptionProfile) : string.Empty;
        process.StartInfo.ArgumentList.Add("--model");
        process.StartInfo.ArgumentList.Add(WorkerProductionProfile.GetEffectiveWhisperXModel(_appSettings.Worker, effectiveTranscriptionProfile));
        process.StartInfo.ArgumentList.Add("--batch-size");
        process.StartInfo.ArgumentList.Add(effectiveBatchSize.ToString());
        process.StartInfo.ArgumentList.Add("--compute-type");
        process.StartInfo.ArgumentList.Add(effectiveComputeType);
        process.StartInfo.ArgumentList.Add("--transcription-profile");
        process.StartInfo.ArgumentList.Add(effectiveTranscriptionProfile);
        process.StartInfo.ArgumentList.Add("--whispercpp-exe");
        process.StartInfo.ArgumentList.Add(_appSettings.Worker.WhisperCppExe);
        process.StartInfo.ArgumentList.Add("--whispercpp-model");
        process.StartInfo.ArgumentList.Add(_appSettings.Worker.WhisperCppModelPath);
        process.StartInfo.ArgumentList.Add("--ffmpeg-path");
        process.StartInfo.ArgumentList.Add(_appSettings.Worker.FfmpegPath);
        process.StartInfo.ArgumentList.Add("--enable-diarization");
        process.StartInfo.ArgumentList.Add(effectiveDiarizationEnabled ? "true" : "false");
        process.StartInfo.ArgumentList.Add("--diarization-model");
        process.StartInfo.ArgumentList.Add(effectiveDiarizationModel);
        process.StartInfo.ArgumentList.Add("--diarization-acceleration");
        process.StartInfo.ArgumentList.Add(WorkerProductionProfile.GetEffectiveDiarizationAcceleration(_appSettings.Worker));
        if (effectiveDiarizationEnabled && _appSettings.Worker.MinSpeakers is > 0)
        {
            process.StartInfo.ArgumentList.Add("--min-speakers");
            process.StartInfo.ArgumentList.Add(_appSettings.Worker.MinSpeakers.Value.ToString());
        }
        if (effectiveDiarizationEnabled && _appSettings.Worker.MaxSpeakers is > 0)
        {
            process.StartInfo.ArgumentList.Add("--max-speakers");
            process.StartInfo.ArgumentList.Add(_appSettings.Worker.MaxSpeakers.Value.ToString());
        }
        process.StartInfo.Environment["PYANNOTE_METRICS_ENABLED"] = "0";
        // Bij een terugval op de processor kiest het hulpprogramma het lichtere CPU-model; het zet zelf een lage
        // prioriteit en een begrensd aantal draden.
        process.StartInfo.Environment["CAPTIVOX_WHISPERX_MODEL"] = _appSettings.Worker.WhisperXModel;
        process.StartInfo.Environment["CAPTIVOX_WHISPERX_CPU_MODEL"] = WorkerProductionProfile.GetEffectiveWhisperXModel(_appSettings.Worker, "cpu-fallback");
        // Het hulpprogramma zoekt zijn runtime in de gegevensmap van deze smaak, nooit in een geerfde map.
        process.StartInfo.Environment[Branding.WorkerDataRootVariable] = AppRuntime.GetAppDataDirectory();
        if (!string.IsNullOrWhiteSpace(_appSettings.Worker.FfmpegPath))
        {
            process.StartInfo.Environment["FFMPEG_PATH"] = _appSettings.Worker.FfmpegPath;
        }

        try
        {
            ResetWorkerSelfTestArtifacts();

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            var timedOut = false;
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                AppRuntime.LogMessage("Quick connection test timed out after 45 seconds.");
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception killEx)
                {
                    AppRuntime.LogException("Quick connection test kill failed", killEx);
                }

                await process.WaitForExitAsync();
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            var rawOutput = (stdout + Environment.NewLine + stderr).Trim();
            if (timedOut)
            {
                rawOutput = string.IsNullOrWhiteSpace(rawOutput)
                    ? "De verbindingstest is afgebroken na 45 seconden zonder afgeronde quick self-test."
                    : rawOutput + Environment.NewLine + Environment.NewLine + "De verbindingstest is afgebroken na 45 seconden zonder afgeronde quick self-test.";
            }
            else if (string.IsNullOrWhiteSpace(rawOutput))
            {
                rawOutput = "Geen uitvoer ontvangen.";
            }

            string? summaryLine = null;
            foreach (var line in rawOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("SELFTEST_JSON:", StringComparison.Ordinal))
                {
                    summaryLine = line.Substring("SELFTEST_JSON:".Length);
                    break;
                }
            }

            var effectiveExitCode = process.ExitCode;
            if (timedOut)
            {
                effectiveExitCode = -1;
            }

            if (string.IsNullOrWhiteSpace(summaryLine))
            {
                var fallbackSummary = new
                {
                    success = effectiveExitCode == 0 && !timedOut,
                    app_version = GetDisplayVersion(),
                    worker_version = GetDisplayVersion(),
                    runtime_mode = "python",
                    self_test_mode = "quick",
                    python_exe = pythonExe,
                    helper_script = helperScript,
                    checks = new
                    {
                        python_ok = File.Exists(pythonExe),
                        helper_ok = File.Exists(helperScript),
                        token_ok = !_appSettings.Worker.DiarizationEnabled || !string.IsNullOrWhiteSpace(currentToken),
                        timed_out = timedOut
                    },
                    messages = new[]
                    {
                        timedOut ? "Quick self-test time-out na 45 seconden." : effectiveExitCode == 0 ? "Quick self-test geslaagd." : $"Quick self-test faalde met exitcode {effectiveExitCode}."
                    }
                };
                summaryLine = JsonSerializer.Serialize(fallbackSummary);
            }

            var artifactsDirectory = PersistWorkerSelfTestArtifacts(process.StartInfo, effectiveExitCode, stdout, stderr, summaryLine);

            try
            {
                using var doc = JsonDocument.Parse(summaryLine);
                var root = doc.RootElement;
                var success = root.TryGetProperty("success", out var successProp) && successProp.GetBoolean();
                var title = success ? "Verbindingstest geslaagd" : "Verbindingstest met waarschuwingen/mislukking";
                var icon = success ? MessageBoxIcon.Information : MessageBoxIcon.Warning;

                var lines = new List<string>();
                lines.Add($"Programma: {GetDisplayVersion()}");
                if (root.TryGetProperty("runtime_mode", out var runtimeProp))
                {
                    lines.Add($"Runtime: {runtimeProp.GetString()}");
                }
                if (root.TryGetProperty("self_test_mode", out var selfModeProp))
                {
                    lines.Add($"Self-testmodus: {selfModeProp.GetString()}");
                }
                if (root.TryGetProperty("python_version", out var pyVersionProp))
                {
                    lines.Add($"Python-versie: {pyVersionProp.GetString()}");
                }
                if (root.TryGetProperty("python_exe", out var pyExeProp))
                {
                    lines.Add($"Python: {pyExeProp.GetString()}");
                }
                if (root.TryGetProperty("helper_script", out var helperProp))
                {
                    lines.Add($"Helper: {helperProp.GetString()}");
                }

                lines.Add(string.Empty);
                lines.Add("Checks:");
                if (root.TryGetProperty("checks", out var checksProp) && checksProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in checksProp.EnumerateObject())
                    {
                        var state = prop.Value.ValueKind == JsonValueKind.True ? "OK" : prop.Value.ValueKind == JsonValueKind.False ? "Niet OK" : prop.Value.ToString();
                        lines.Add($"- {prop.Name}: {state}");
                    }
                }

                if (root.TryGetProperty("messages", out var messagesProp) && messagesProp.ValueKind == JsonValueKind.Array)
                {
                    lines.Add(string.Empty);
                    lines.Add("Meldingen:");
                    foreach (var item in messagesProp.EnumerateArray())
                    {
                        lines.Add($"- {item.GetString()}");
                    }
                }

                if (!success)
                {
                    lines.Add(string.Empty);
                    lines.Add("Overzicht ruwe uitvoer:");
                    lines.Add(SummarizeSelfTestOutput(rawOutput));
                }

                lines.Add(string.Empty);
                lines.Add($"Details opgeslagen in: {artifactsDirectory}");

                ShowOwnedMessage(owner, string.Join(Environment.NewLine, lines), title, MessageBoxButtons.OK, icon);
                return;
            }
            catch (Exception ex)
            {
                AppRuntime.LogException("Quick connection test JSON parse failed", ex);
            }

            var fallbackSuccess = effectiveExitCode == 0 && !timedOut;
            var fallbackIcon = fallbackSuccess ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
            var fallbackTitle = fallbackSuccess ? "Verbindingstest geslaagd" : "Verbindingstest met waarschuwingen/mislukking";
            var fallbackMessage = fallbackSuccess
                ? rawOutput + Environment.NewLine + Environment.NewLine + $"Details opgeslagen in: {artifactsDirectory}"
                : SummarizeSelfTestOutput(rawOutput) + Environment.NewLine + Environment.NewLine + $"Details opgeslagen in: {artifactsDirectory}";
            ShowOwnedMessage(owner, fallbackMessage, fallbackTitle, MessageBoxButtons.OK, fallbackIcon);
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Quick connection test failed", ex);
            ShowOwnedMessage(owner, ex.ToString(), "Verbindingstest mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string PersistWorkerSelfTestArtifacts(ProcessStartInfo startInfo, int exitCode, string stdout, string stderr, string? summaryJson)
    {
        var artifactsDirectory = AppRuntime.GetSelfTestDirectory();

        try
        {
            Directory.CreateDirectory(artifactsDirectory);
            File.WriteAllText(Path.Combine(artifactsDirectory, "stdout.txt"), stdout ?? string.Empty, Encoding.UTF8);
            File.WriteAllText(Path.Combine(artifactsDirectory, "stderr.txt"), stderr ?? string.Empty, Encoding.UTF8);
            File.WriteAllText(Path.Combine(artifactsDirectory, "combined.txt"), ((stdout ?? string.Empty) + Environment.NewLine + (stderr ?? string.Empty)).Trim(), Encoding.UTF8);
            if (!string.IsNullOrWhiteSpace(summaryJson))
            {
                File.WriteAllText(Path.Combine(artifactsDirectory, "summary.json"), summaryJson, Encoding.UTF8);
            }

            var environmentLines = new List<string>
            {
                $"timestamp={DateTimeOffset.Now:O}",
                $"app_version={GetDisplayVersion()}",
                $"command={Path.GetFileName(startInfo.FileName)}",
                $"arguments={(string.IsNullOrWhiteSpace(startInfo.Arguments) ? string.Empty : "[REDACTED_ARGUMENTS]")}",
                $"working_directory=[REDACTED_PATH]",
                $"exit_code={exitCode}",
                $"os_version={Environment.OSVersion}",
                $"dotnet_runtime={Environment.Version}",
                $"machine_name=[REDACTED_IDENTIFIER]",
                $"user_name=[REDACTED_IDENTIFIER]",
                $"summary_json_present={!string.IsNullOrWhiteSpace(summaryJson)}"
            };
            File.WriteAllText(Path.Combine(artifactsDirectory, "environment.txt"), string.Join(Environment.NewLine, environmentLines), Encoding.UTF8);
            AppRuntime.LogMessage($"Worker self-test artefacts bijgewerkt: {artifactsDirectory} (exitcode {exitCode})");
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Persist worker self-test artefacts failed", ex);
        }

        return artifactsDirectory;
    }

    private static string SummarizeSelfTestOutput(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return "Geen uitvoer ontvangen.";
        }

        if (TranscriptionRuntimeStatus.LooksLikeRuntimeFailure(rawOutput))
        {
            return TranscriptionRuntimeStatus.MissingMessage;
        }

        if (rawOutput.Contains("Unexpected token '??'", StringComparison.Ordinal) || rawOutput.Contains("Missing closing ')' in expression", StringComparison.Ordinal))
        {
            return "De worker-self-test liep vast in de PowerShell-parser. Dit wijst meestal op scriptsyntax die niet wordt ondersteund door de actieve PowerShell-versie.";
        }

        if (rawOutput.Contains("parameter name 'Depth'", StringComparison.OrdinalIgnoreCase))
        {
            return "De worker-self-test gebruikt een parameter die niet beschikbaar is in Windows PowerShell 5.1 (waarschijnlijk ConvertFrom-Json -Depth).";
        }


        var lines = rawOutput
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Take(12)
            .ToArray();

        return string.Join(Environment.NewLine, lines);
    }

    private void ExportDiagnosticsPackage(IWin32Window owner)
    {
        SaveCurrentSettings();

        var warning = "Het diagnosepakket bevat logbestanden, manifestgegevens, lokale paden en mogelijk meetingmetadata. Audio wordt niet standaard meegepakt. Deel dit pakket alleen met beheerders of ontwikkelaars die deze informatie mogen inzien.";
        var confirm = ShowOwnedMessage(owner, warning + Environment.NewLine + Environment.NewLine + "Wil je doorgaan met exporteren?", "Waarschuwing diagnosepakket", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = UiText.T("Exporteer diagnosepakket"),
            Filter = UiText.T("ZIP-bestand (*.zip)|*.zip"),
            FileName = $"{Branding.DiagnosticsPrefix}_diagnostics_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.zip"
        };

        var resolvedOwner = ResolveDialogOwner(owner);
        var dialogResult = resolvedOwner is null ? dialog.ShowDialog() : dialog.ShowDialog(resolvedOwner);
        if (dialogResult != DialogResult.OK)
        {
            return;
        }

        try
        {
            var result = DiagnosticsPackageExporter.Export(dialog.FileName, _manifest, _appSettings);
            var message = result.HasWarnings
                ? $"Diagnosepakket opgeslagen op:{Environment.NewLine}{dialog.FileName}{Environment.NewLine}{Environment.NewLine}Niet alle bestanden konden live worden meegenomen. Bekijk in de ZIP het bestand export_warnings.txt voor details."
                : $"Diagnosepakket opgeslagen op:{Environment.NewLine}{dialog.FileName}";
            ShowOwnedMessage(owner, message, "Diagnosepakket geexporteerd", MessageBoxButtons.OK, result.HasWarnings ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Diagnostics export failed", ex);
            ShowOwnedMessage(owner, ex.ToString(), "Export diagnosepakket mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowSettingsDialog()
    {
        using var dialog = new BufferedDialogForm
        {
            Text = $"{Branding.DisplayName} | Instellingen en admin",
            Icon = Branding.GetAppIconOrDefault(),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            MaximizeBox = true,
            MinimizeBox = false,
            ShowInTaskbar = false,
            MinimumSize = new Size(1020, 860),
            ClientSize = new Size(1120, 980),
            AutoScaleMode = AutoScaleMode.Dpi,
            BackColor = AppBackground,
            Font = Font,
        };

        var minimizeToTrayCheckbox = new CheckBox { Text = _minimizeToTrayCheckbox.Text, Checked = _minimizeToTrayCheckbox.Checked, AutoSize = true, Margin = new Padding(0, 0, 0, 10), ForeColor = Color.FromArgb(43, 51, 63) };
        var startWithWindowsCheckbox = new CheckBox { Text = "Start applicatie bij aanmelden op Windows", Checked = _appSettings.Ui.StartWithWindows, AutoSize = true, Margin = new Padding(0, 0, 0, 8), ForeColor = Color.FromArgb(43, 51, 63) };
        var startMinimizedCheckbox = new CheckBox { Text = "Geminimaliseerd starten", Checked = _appSettings.Ui.StartMinimized, AutoSize = true, Margin = new Padding(0, 0, 0, 10), ForeColor = Color.FromArgb(43, 51, 63) };
        var meetingDetectionEnabledCheckbox = new CheckBox { Text = "Meeting detectie inschakelen", Checked = _appSettings.Ui.MeetingDetection.Enabled, AutoSize = true, Margin = new Padding(0, 0, 0, 8), ForeColor = Color.FromArgb(43, 51, 63) };
        var detectTeamsCheckbox = new CheckBox { Text = "Microsoft Teams", Checked = _appSettings.Ui.MeetingDetection.DetectTeams, AutoSize = true, Margin = new Padding(0, 0, 12, 0), ForeColor = Color.FromArgb(43, 51, 63) };
        var detectZoomCheckbox = new CheckBox { Text = "Zoom", Checked = _appSettings.Ui.MeetingDetection.DetectZoom, AutoSize = true, Margin = new Padding(0, 0, 12, 0), ForeColor = Color.FromArgb(43, 51, 63) };
        var detectBrowserMeetingsCheckbox = new CheckBox { Text = "Browser meetings (Meet/Teams/Zoom)", Checked = _appSettings.Ui.MeetingDetection.DetectBrowserMeetings, AutoSize = true, Margin = new Padding(0), ForeColor = Color.FromArgb(43, 51, 63) };
        var meetingDetectionDelayNumeric = new NumericUpDown { Minimum = 5, Maximum = 120, Value = Math.Clamp(_appSettings.Ui.MeetingDetection.StabilitySeconds, 5, 120), Width = 90 };
        var meetingDetectionCooldownNumeric = new NumericUpDown { Minimum = 1, Maximum = 240, Value = Math.Clamp(_appSettings.Ui.MeetingDetection.PromptCooldownMinutes, 1, 240), Width = 90 };
        var diagnosticsModeCheckbox = new CheckBox { Text = "Uitgebreide logging / diagnosemodus", Checked = _appSettings.Ui.DiagnosticsMode, AutoSize = true, Margin = new Padding(0, 0, 0, 10), ForeColor = Color.FromArgb(43, 51, 63) };
        var preserveWorkerOutputCheckbox = new CheckBox { Text = "Bewaar worker-uitvoer (stdout/stderr) bij diagnosemodus", Checked = _appSettings.Worker.PreserveWorkerOutput, AutoSize = true, Margin = new Padding(0, 0, 0, 10), ForeColor = Color.FromArgb(43, 51, 63) };
        var workerEnabledCheckbox = new CheckBox { Text = "WhisperX ingeschakeld", Checked = _appSettings.Worker.WhisperXEnabled, AutoSize = true, Margin = new Padding(0, 0, 0, 10), ForeColor = Color.FromArgb(43, 51, 63) };
        var useProductionProfileCheckbox = new CheckBox { Text = Branding.IsPro ? "Gebruik productieprofiel (aanbevolen)" : "Gebruik de standaardinstellingen (aanbevolen)", Checked = _appSettings.Worker.UseProductionProfile, AutoSize = true, Margin = new Padding(0, 0, 0, 10), ForeColor = Color.FromArgb(43, 51, 63) };
        var transcriptionProfileCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        transcriptionProfileCombo.Items.AddRange(new object[] { "Automatisch", "NVIDIA CUDA", "AMD GPU", "CPU fallback" });
        transcriptionProfileCombo.SelectedItem = HardwareAssessmentService.GetTranscriptionProfileDisplayName(_appSettings.Worker.TranscriptionProfile);

        var pythonPathTextBox = new TextBox { Text = _appSettings.Worker.PythonExe, Width = 420 };
        var runtimeHintLabel = new Label { AutoSize = true, MaximumSize = new Size(650, 0), ForeColor = Color.FromArgb(73, 82, 96), Text = GetEffectiveWorkerProfileSummary() };
        var activeTranscriptionProfileLabel = new Label { AutoSize = true, MaximumSize = new Size(650, 0), ForeColor = Color.FromArgb(73, 82, 96), Text = BuildActiveTranscriptionProfileText(_appSettings.Worker.TranscriptionProfile, null) };
        var modelTextBox = new TextBox { Text = _appSettings.Worker.WhisperXModel, Width = 220 };
        var cpuModelTextBox = new TextBox { Text = _appSettings.Worker.CpuWhisperXModel, Width = 220 };
        var computeTypeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        computeTypeCombo.Items.AddRange(new object[] { "float16", "int8", "float32" });
        computeTypeCombo.SelectedItem = WorkerProductionProfile.GetEffectiveComputeType(_appSettings.Worker);
        var languageProfileCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        languageProfileCombo.Items.AddRange(LanguageProfileCatalog.GetOptions().Select(x => x.DisplayName).Cast<object>().ToArray());
        languageProfileCombo.SelectedItem = LanguageProfileCatalog.GetDisplayName(_appSettings.Ui.LanguageProfile);
        var languageTextBox = new TextBox { Text = LanguageProfileCatalog.GetTranscriptionLanguageCode(_appSettings.Ui.LanguageProfile), Width = 120, ReadOnly = true, TabStop = false, BackColor = Color.FromArgb(245, 247, 250) };
        var outputFormatCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        outputFormatCombo.Items.AddRange(new object[] { "txt+json", "txt", "json" });
        outputFormatCombo.SelectedItem = _appSettings.Worker.TranscriptOutputFormat;
        var batchSizeNumeric = new NumericUpDown { Minimum = 1, Maximum = 32, Value = _appSettings.Worker.BatchSize, Width = 80 };
        var pollIntervalNumeric = new NumericUpDown { Minimum = 5, Maximum = 300, Value = Math.Clamp(_appSettings.Worker.PollIntervalSec, 5, 300), Width = 80 };
        var minReadyAgeNumeric = new NumericUpDown { Minimum = 0, Maximum = 120, Value = Math.Clamp(_appSettings.Worker.MinReadyAgeSec, 0, 120), Width = 80 };
        var staleStartedNumeric = new NumericUpDown { Minimum = 5, Maximum = 1440, Value = Math.Clamp(_appSettings.Worker.RetryStaleStartedAfterMinutes, 5, 1440), Width = 80 };
        var maxRetryAttemptsNumeric = new NumericUpDown { Minimum = 0, Maximum = 25, Value = Math.Clamp(_appSettings.Worker.MaxRetryAttempts, 0, 25), Width = 80 };
        var retryBackoffNumeric = new NumericUpDown { Minimum = 0, Maximum = 1440, Value = Math.Clamp(_appSettings.Worker.RetryBackoffMinutes, 0, 1440), Width = 80 };
        var ffmpegPathTextBox = new TextBox { Text = _appSettings.Worker.FfmpegPath, Width = 220 };
        var healthMonitorCheckbox = new CheckBox { Text = "Opname health monitor inschakelen", Checked = _appSettings.Ui.Monitoring.HealthMonitorEnabled, AutoSize = true, ForeColor = Color.FromArgb(43, 51, 63) };
        var silenceDetectionCheckbox = new CheckBox { Text = "Silence detection inschakelen", Checked = _appSettings.Ui.Monitoring.SilenceDetectionEnabled, AutoSize = true, ForeColor = Color.FromArgb(43, 51, 63) };
        var silenceThresholdNumeric = new NumericUpDown { Minimum = 1, Maximum = 25, DecimalPlaces = 1, Increment = 0.5M, Value = (decimal)Math.Clamp(_appSettings.Ui.Monitoring.SilenceThresholdPercent, 1, 25), Width = 90 };
        var micSilenceSecondsNumeric = new NumericUpDown { Minimum = 3, Maximum = 120, Value = Math.Clamp(_appSettings.Ui.Monitoring.MicSilenceWarningSeconds, 3, 120), Width = 90 };
        var systemSilenceSecondsNumeric = new NumericUpDown { Minimum = 3, Maximum = 120, Value = Math.Clamp(_appSettings.Ui.Monitoring.SystemSilenceWarningSeconds, 3, 120), Width = 90 };
        var bothSilenceSecondsNumeric = new NumericUpDown { Minimum = 3, Maximum = 180, Value = Math.Clamp(_appSettings.Ui.Monitoring.BothSilenceWarningSeconds, 3, 180), Width = 90 };
        var warningCooldownSecondsNumeric = new NumericUpDown { Minimum = 10, Maximum = 600, Value = Math.Clamp(_appSettings.Ui.Monitoring.WarningCooldownSeconds, 10, 600), Width = 90 };
        var retentionEnabledCheckbox = new CheckBox { Text = "Retentie inschakelen voor completed sessies", Checked = _appSettings.Worker.Retention.Enabled, AutoSize = true, ForeColor = Color.FromArgb(43, 51, 63) };
        var audioRetentionDaysNumeric = new NumericUpDown { Minimum = 1, Maximum = 3650, Value = Math.Clamp(_appSettings.Worker.Retention.AudioRetentionDays, 1, 3650), Width = 90 };
        var transcriptRetentionDaysNumeric = new NumericUpDown { Minimum = 1, Maximum = 3650, Value = Math.Clamp(_appSettings.Worker.Retention.TranscriptRetentionDays, 1, 3650), Width = 90 };
        var sessionRecordRetentionDaysNumeric = new NumericUpDown { Minimum = 1, Maximum = 3650, Value = Math.Clamp(_appSettings.Worker.Retention.SessionRecordRetentionDays, Math.Max(1, _appSettings.Worker.Retention.TranscriptRetentionDays), 3650), Width = 90 };
        var enhancementEnabledCheckbox = new CheckBox { Text = "Audio enhancement inschakelen", Checked = _appSettings.Worker.Enhancement.Enabled, AutoSize = true, ForeColor = Color.FromArgb(43, 51, 63) };
        var enhancementImportedOnlyCheckbox = new CheckBox { Text = "Alleen voor import/watch folder", Checked = _appSettings.Worker.Enhancement.ImportedAudioOnly, AutoSize = true, ForeColor = Color.FromArgb(43, 51, 63) };
        var enhancementProfileCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        enhancementProfileCombo.Items.AddRange(new object[] { "light-clean", "speech-focus" });
        enhancementProfileCombo.SelectedItem = string.IsNullOrWhiteSpace(_appSettings.Worker.Enhancement.Profile) ? "light-clean" : _appSettings.Worker.Enhancement.Profile;
        var exportSrtCheckbox = new CheckBox { Text = "Maak ook transcript.srt", Checked = _appSettings.Worker.ExportSrt, AutoSize = true, ForeColor = Color.FromArgb(43, 51, 63) };
        var exportVttCheckbox = new CheckBox { Text = "Maak ook transcript.vtt", Checked = _appSettings.Worker.ExportVtt, AutoSize = true, ForeColor = Color.FromArgb(43, 51, 63) };
        var detectHardwareForAiButton = new Button { Text = "Hardware opnieuw detecteren", Width = 210, Height = 34 };
        StyleButton(detectHardwareForAiButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        var hardwareAssessmentPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = CardBackground, Margin = new Padding(0) };
        hardwareAssessmentPanel.Controls.Add(detectHardwareForAiButton);
        var hardwareAssessmentLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(650, 0),
            ForeColor = Color.FromArgb(73, 82, 96),
            Text = "Hardware-inschatting nog niet uitgevoerd. Gebruik 'Hardware opnieuw detecteren' om GPU, dedicated VRAM, detectiemethode en betrouwbaarheid op te vragen."
        };
        var recordingStoppedWindowsCheckbox = new CheckBox { Text = "Windows", Checked = _appSettings.Ui.Notifications.RecordingStoppedWaitingForTranscription.WindowsEnabled, AutoSize = true, ForeColor = Color.FromArgb(43, 51, 63) };
        var transcriptReadyWindowsCheckbox = new CheckBox { Text = "Windows", Checked = _appSettings.Ui.Notifications.TranscriptReady.WindowsEnabled, AutoSize = true, ForeColor = Color.FromArgb(43, 51, 63) };
        var transcriptionFailedWindowsCheckbox = new CheckBox { Text = "Windows", Checked = _appSettings.Ui.Notifications.TranscriptionFailed.WindowsEnabled, AutoSize = true, ForeColor = Color.FromArgb(43, 51, 63) };
        var silenceWarningWindowsCheckbox = new CheckBox { Text = "Windows", Checked = _appSettings.Ui.Notifications.SilenceWarning.WindowsEnabled, AutoSize = true, ForeColor = Color.FromArgb(43, 51, 63) };
        var healthWarningWindowsCheckbox = new CheckBox { Text = "Windows", Checked = _appSettings.Ui.Notifications.RecordingHealthWarning.WindowsEnabled, AutoSize = true, ForeColor = Color.FromArgb(43, 51, 63) };
        var recordingFailureWindowsCheckbox = new CheckBox { Text = "Windows", Checked = _appSettings.Ui.Notifications.RecordingFailure.WindowsEnabled, AutoSize = true, ForeColor = Color.FromArgb(43, 51, 63) };
        string BuildActiveTranscriptionProfileText(string? configuredProfile, HardwareAssessmentResult? assessment)
        {
            var normalized = HardwareAssessmentService.NormalizeTranscriptionProfile(configuredProfile);
            if (normalized == "auto")
            {
                if (assessment is not null)
                {
                    return $"Automatisch actief op basis van laatste detectie: {assessment.TranscriptionProfileDisplayName}. {assessment.TranscriptionProfileReason}";
                }

                return "Automatisch actief: met een NVIDIA-kaart via CUDA, anders op de processor (ook met een AMD-kaart; de snelle AMD-route zet je aan onder Instellingen, Opnemen). Gebruik Hardware opnieuw detecteren om de verwachte keuze hier te tonen.";
            }

            var hardwareProfile = _appSettings.Hardware;
            if (hardwareProfile?.StartupDetectionCompleted == true
                && string.Equals(HardwareAssessmentService.NormalizeTranscriptionProfile(hardwareProfile.LastRecommendedTranscriptionProfile), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return normalized == "amd-gpu"
                    ? $"Standaard op basis van hardwaredetectie: {HardwareAssessmentService.GetTranscriptionProfileDisplayName(normalized)}. Engine: FFmpeg Whisper GPU met Vulkan-validatie wanneer aanwezig; anders CPU fallback."
                    : $"Standaard op basis van hardwaredetectie: {HardwareAssessmentService.GetTranscriptionProfileDisplayName(normalized)}. Als dit profiel runtime niet bruikbaar is, valt de helper terug op CPU.";
            }

            return normalized == "amd-gpu"
                ? $"Handmatig ingesteld: {HardwareAssessmentService.GetTranscriptionProfileDisplayName(normalized)}. Engine: FFmpeg Whisper GPU met Vulkan-validatie wanneer aanwezig; anders CPU fallback."
                : $"Handmatig ingesteld: {HardwareAssessmentService.GetTranscriptionProfileDisplayName(normalized)}. Als dit profiel runtime niet bruikbaar is, valt de helper terug op CPU.";
        }

        void RefreshWorkerProfileUi()
        {
            var preview = new WorkerSettings
            {
                WhisperXEnabled = workerEnabledCheckbox.Checked,
                DiarizationEnabled = false,
                UseProductionProfile = useProductionProfileCheckbox.Checked,
                TranscriptionProfile = ConvertTranscriptionProfileSelection(transcriptionProfileCombo.SelectedItem?.ToString()),
                AmdGpuFastRoute = ConvertTranscriptionProfileSelection(transcriptionProfileCombo.SelectedItem?.ToString()) == "amd-gpu",
                PythonExe = pythonPathTextBox.Text.Trim(),
                WhisperXModel = modelTextBox.Text.Trim(),
                CpuWhisperXModel = cpuModelTextBox.Text.Trim(),
                ComputeType = (computeTypeCombo.SelectedItem?.ToString() ?? _appSettings.Worker.ComputeType).Trim(),
                Language = languageTextBox.Text.Trim(),
                BatchSize = (int)batchSizeNumeric.Value,
                TranscriptOutputFormat = outputFormatCombo.SelectedItem?.ToString() ?? _appSettings.Worker.TranscriptOutputFormat,
                PollIntervalSec = (int)pollIntervalNumeric.Value,
                MinReadyAgeSec = (int)minReadyAgeNumeric.Value,
                RetryStaleStartedAfterMinutes = (int)staleStartedNumeric.Value,
                MaxRetryAttempts = (int)maxRetryAttemptsNumeric.Value,
                RetryBackoffMinutes = (int)retryBackoffNumeric.Value,
                FfmpegPath = ffmpegPathTextBox.Text.Trim(),
                PreserveWorkerOutput = preserveWorkerOutputCheckbox.Checked,
                Retention = _appSettings.Worker.Retention,
                Enhancement = _appSettings.Worker.Enhancement,
                ExportSrt = exportSrtCheckbox.Checked,
                ExportVtt = exportVttCheckbox.Checked,
                RuntimeMode = "python"
            };

            runtimeHintLabel.Text = WorkerProductionProfile.BuildSummary(preview);
            activeTranscriptionProfileLabel.Text = BuildActiveTranscriptionProfileText(preview.TranscriptionProfile, null);

            var manualEditable = !useProductionProfileCheckbox.Checked;
            modelTextBox.Enabled = manualEditable;
            cpuModelTextBox.Enabled = manualEditable;
            computeTypeCombo.Enabled = manualEditable;

            if (!manualEditable)
            {
                modelTextBox.Text = _appSettings.Worker.WhisperXModel;
                cpuModelTextBox.Text = _appSettings.Worker.CpuWhisperXModel;
                computeTypeCombo.SelectedItem = WorkerProductionProfile.DefaultComputeType;
                if ((int)pollIntervalNumeric.Value != WorkerProductionProfile.DefaultPollIntervalSec)
                {
                    pollIntervalNumeric.Value = WorkerProductionProfile.DefaultPollIntervalSec;
                }
                if ((int)minReadyAgeNumeric.Value != WorkerProductionProfile.DefaultMinReadyAgeSec)
                {
                    minReadyAgeNumeric.Value = WorkerProductionProfile.DefaultMinReadyAgeSec;
                }
            }
        }

        useProductionProfileCheckbox.CheckedChanged += (_, _) => RefreshWorkerProfileUi();
        transcriptionProfileCombo.SelectedIndexChanged += (_, _) => RefreshWorkerProfileUi();
        workerEnabledCheckbox.CheckedChanged += (_, _) => RefreshWorkerProfileUi();
        computeTypeCombo.SelectedIndexChanged += (_, _) => RefreshWorkerProfileUi();
        modelTextBox.TextChanged += (_, _) => RefreshWorkerProfileUi();
        cpuModelTextBox.TextChanged += (_, _) => RefreshWorkerProfileUi();
        RefreshWorkerProfileUi();

        var root = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14), BackColor = AppBackground };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = CreateRecorderSurfaceCard(autoSize: true);
        header.Padding = new Padding(18, 16, 18, 16);
        header.Margin = new Padding(0, 0, 0, 12);
        var headerLayout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, AutoSize = true, BackColor = CardBackground };
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerLayout.Controls.Add(BuildCompactBrandRow(), 0, 0);
        headerLayout.Controls.Add(new Label { Text = $"{Branding.DisplayName} | Instellingen en admin", Font = new Font("Segoe UI", 12.5F, FontStyle.Bold, GraphicsUnit.Point), AutoSize = true, ForeColor = Color.FromArgb(22, 28, 36), Margin = new Padding(0, 12, 0, 6) }, 0, 1);
        headerLayout.Controls.Add(new Label { Text = "Wijzigingen gelden voor nieuwe sessies. Workerinstellingen worden bij de volgende poll opnieuw ingelezen.", AutoSize = true, MaximumSize = new Size(760, 0), ForeColor = Color.FromArgb(73, 82, 96), Margin = new Padding(0) }, 0, 2);
        header.Controls.Add(headerLayout);
        root.Controls.Add(header, 0, 0);

        var scroller = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true, AutoScrollMargin = new Size(0, 12), Padding = new Padding(0, 4, 4, 0), BackColor = AppBackground };
        var stack = new BufferedTableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 11, Margin = new Padding(0, 4, 0, 0), Padding = new Padding(0), BackColor = AppBackground };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var stackRow = 0; stackRow < 11; stackRow++)
        {
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        Button? manageWatchFoldersButton = null;
        Label? processingFootnoteLabel = null;
        var editionCard = CreateRecorderSurfaceCard(autoSize: true);
        editionCard.Margin = new Padding(0, 0, 0, 10);
        editionCard.Padding = new Padding(18);
        var editionSummaryLabel = new Label { AutoSize = true, MaximumSize = new Size(720, 0), ForeColor = Color.FromArgb(22, 28, 36) };
        var editionDetailLabel = new Label { AutoSize = true, MaximumSize = new Size(720, 0), ForeColor = Color.FromArgb(73, 82, 96) };
        var manageLicenseButton = new Button { Text = "Meer informatie", Width = 170, Height = 34 };
        StyleButton(manageLicenseButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        var editionActions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0), BackColor = CardBackground };
        editionActions.Controls.Add(manageLicenseButton);
        var editionLayout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Margin = new Padding(0), BackColor = CardBackground };
        editionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editionLayout.Controls.Add(new Label { Text = "Publieke productlijn en ondersteuning", Font = SectionFont, AutoSize = true, Margin = new Padding(0, 0, 0, 10), ForeColor = Color.FromArgb(22, 28, 36) }, 0, 0);
        editionLayout.Controls.Add(editionSummaryLabel, 0, 1);
        editionLayout.Controls.Add(editionDetailLabel, 0, 2);
        editionLayout.Controls.Add(editionActions, 0, 3);
        editionCard.Controls.Add(editionLayout);

        void RefreshEditionSettingsUi()
        {
            var snapshot = _editionState;
            editionSummaryLabel.Text = snapshot.Summary;
            editionDetailLabel.Text = snapshot.Detail;
            if (manageWatchFoldersButton is not null)
            {
                var watchFoldersEnabled = snapshot.IsFeatureEnabled(EditionFeatureCatalog.WatchFolders);
                manageWatchFoldersButton.Text = "Watch folders beheren";
                manageWatchFoldersButton.Enabled = watchFoldersEnabled;
                SetUiToolTip(manageWatchFoldersButton, watchFoldersEnabled ? "Watch folders configureren" : "Watch folders zijn momenteel niet beschikbaar door lokale configuratie of sessiestatus." );
            }
            if (processingFootnoteLabel is not null)
            {
                processingFootnoteLabel.Text = snapshot.IsFeatureEnabled(EditionFeatureCatalog.WatchFolders)
                    ? "Watch folders zijn actief binnen de huidige publieke productlijn. Custom vocabulary blijft onderdeel van de basisworkflow. Exports worden automatisch naast transcript.json aangemaakt wanneer deze opties zijn ingeschakeld."
                    : "Watch folders zijn momenteel niet beschikbaar door lokale configuratie of prerequisites. Controleer de workerinstellingen en bootstrapstatus.";
            }
        }

        manageLicenseButton.Click += (_, _) =>
        {
            _ = ShowLicenseManagementDialog(dialog, RefreshEditionSettingsUi);
            RefreshEditionSettingsUi();
                    };
        RefreshEditionSettingsUi();

        var preflightSettingsButton = new Button { Text = "Controleer devices", Width = 180, Height = 34 };
        StyleButton(preflightSettingsButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        preflightSettingsButton.Click += async (_, _) => await RunBusyUiActionAsync("Preflight device-check", RunPreflightCheckAsync);

        var recorderCard = CreateRecorderSurfaceCard(autoSize: true);
        recorderCard.Margin = new Padding(0, 0, 0, 10);
        recorderCard.Padding = new Padding(16);
        var recorderLayout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Margin = new Padding(0), BackColor = CardBackground };
        recorderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        recorderLayout.Controls.Add(new Label { Text = "Recorderopties", Font = SectionFont, AutoSize = true, Margin = new Padding(0, 0, 0, 10), ForeColor = Color.FromArgb(22, 28, 36) }, 0, 0);
        var languageProfilePanel = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = CardBackground, Margin = new Padding(0, 0, 0, 8) };
        languageProfilePanel.Controls.Add(new Label { Text = "Taal van de opnames", AutoSize = true, Margin = new Padding(0, 7, 8, 0), ForeColor = Color.FromArgb(43, 51, 63) });
        languageProfilePanel.Controls.Add(languageProfileCombo);
        languageProfilePanel.Controls.Add(new Label { Text = "Transcriptie", AutoSize = true, Margin = new Padding(16, 7, 8, 0), ForeColor = Color.FromArgb(43, 51, 63) });
        languageProfilePanel.Controls.Add(languageTextBox);
        recorderLayout.Controls.Add(languageProfilePanel, 0, 1);
        recorderLayout.Controls.Add(new Label { Text = "Dit taalprofiel bepaalt de transcriptietaal. De taal van Captivox zelf kies je onder Instellingen, Algemeen.", AutoSize = true, MaximumSize = new Size(720, 0), ForeColor = Color.FromArgb(73, 82, 96), Margin = new Padding(0, 0, 0, 8) }, 0, 2);
        recorderLayout.Controls.Add(minimizeToTrayCheckbox, 0, 3);
        recorderLayout.Controls.Add(startWithWindowsCheckbox, 0, 4);
        recorderLayout.Controls.Add(startMinimizedCheckbox, 0, 5);
        var recorderToolsPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = CardBackground, Margin = new Padding(0, 2, 0, 0) };
        recorderToolsPanel.Controls.Add(preflightSettingsButton);
        recorderLayout.Controls.Add(new Label { Text = "Gebruik deze instellingen voor opstartgedrag, de taal van de opnames en devicecontrole. Wijzigingen gelden voor nieuwe sessies.", AutoSize = true, MaximumSize = new Size(720, 0), ForeColor = Color.FromArgb(73, 82, 96), Margin = new Padding(0, 0, 0, 8) }, 0, 6);
        recorderLayout.Controls.Add(recorderToolsPanel, 0, 7);
        recorderCard.Controls.Add(recorderLayout);
        stack.Controls.Add(recorderCard, 0, 0);

        var meetingDetectionCard = CreateRecorderSurfaceCard(autoSize: true);
        meetingDetectionCard.Margin = new Padding(0, 0, 0, 10);
        meetingDetectionCard.Padding = new Padding(16);
        var meetingDetectionLayout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Margin = new Padding(0), BackColor = CardBackground };
        meetingDetectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        meetingDetectionLayout.Controls.Add(new Label { Text = "Meeting detectie", Font = SectionFont, AutoSize = true, Margin = new Padding(0, 0, 0, 10), ForeColor = Color.FromArgb(22, 28, 36) }, 0, 0);
        meetingDetectionLayout.Controls.Add(meetingDetectionEnabledCheckbox, 0, 1);
        var meetingAppPanel = CreateInlineCheckboxPanel(detectTeamsCheckbox, detectZoomCheckbox);
        meetingAppPanel.Controls.Add(detectBrowserMeetingsCheckbox);
        meetingDetectionLayout.Controls.Add(meetingAppPanel, 0, 2);
        meetingDetectionLayout.Controls.Add(new Label { Text = "Meeting detectie werkt als assistentlaag: Captivox detecteert waarschijnlijk Teams/Zoom/Meet-gebruik en stelt dan voor om de opname te starten. Er wordt niet automatisch opgenomen.", AutoSize = true, MaximumSize = new Size(720, 0), ForeColor = Color.FromArgb(73, 82, 96), Margin = new Padding(0, 0, 0, 8) }, 0, 3);
        var meetingTimingPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = CardBackground, Margin = new Padding(0, 0, 0, 0) };
        meetingTimingPanel.Controls.Add(new Label { Text = "Detectievertraging (sec)", AutoSize = true, Margin = new Padding(0, 7, 8, 0) });
        meetingTimingPanel.Controls.Add(meetingDetectionDelayNumeric);
        meetingTimingPanel.Controls.Add(new Label { Text = "Cooldown na negeren (min)", AutoSize = true, Margin = new Padding(16, 7, 8, 0) });
        meetingTimingPanel.Controls.Add(meetingDetectionCooldownNumeric);
        meetingDetectionLayout.Controls.Add(meetingTimingPanel, 0, 4);
        meetingDetectionCard.Controls.Add(meetingDetectionLayout);
        stack.Controls.Add(meetingDetectionCard, 0, 1);


        var workerCard = CreateRecorderSurfaceCard(autoSize: true);
        workerCard.Margin = new Padding(0, 0, 0, 10);
        workerCard.Padding = new Padding(18);
        var workerLayout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0), BackColor = CardBackground };
        workerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        workerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var row = 0;
        AddSettingsRow(workerLayout, ref row, new Label { Text = "Transcriptiewerker", Font = SectionFont, AutoSize = true, ForeColor = Color.FromArgb(22, 28, 36) }, new Panel { Height = 1, Width = 1, BackColor = CardBackground });
        AddSettingsRow(workerLayout, ref row, new Label { Text = string.Empty, AutoSize = true }, workerEnabledCheckbox);
        AddSettingsRow(workerLayout, ref row, new Label { Text = string.Empty, AutoSize = true }, useProductionProfileCheckbox);
        AddSettingsRow(workerLayout, ref row, "Transcriptieprofiel", transcriptionProfileCombo);
        AddSettingsRow(workerLayout, ref row, new Label { Text = string.Empty, AutoSize = true }, new Label { Text = "Automatisch kiest NVIDIA CUDA bij NVIDIA-hardware en anders de processor (CPU fallback), ook bij een AMD-kaart: dat is nauwkeuriger. AMD GPU is de snelle, iets minder nauwkeurige route.", AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Color.FromArgb(73, 82, 96) });
        AddSettingsRow(workerLayout, ref row, "Actief profiel", activeTranscriptionProfileLabel);
        // De hardware-inschatting bepaalt het aanbevolen transcriptieprofiel.
        AddSettingsRow(workerLayout, ref row, "Hardware", hardwareAssessmentPanel);
        AddSettingsRow(workerLayout, ref row, new Label { Text = string.Empty, AutoSize = true }, hardwareAssessmentLabel);
        AddSettingsRow(workerLayout, ref row, "Runtime", runtimeHintLabel);
        AddSettingsRow(workerLayout, ref row, "Python/venv-pad", pythonPathTextBox);
        AddSettingsRow(workerLayout, ref row, "WhisperX model (videokaart)", modelTextBox);
        AddSettingsRow(workerLayout, ref row, "WhisperX model (processor)", cpuModelTextBox);
        AddSettingsRow(workerLayout, ref row, new Label { Text = string.Empty, AutoSize = true }, new Label { Text = "Zonder bruikbare videokaart transcribeert Captivox op de processor met het processormodel (standaard large-v3-turbo, int8), op lage prioriteit en met een begrensd aantal rekenkernen, zodat de pc bruikbaar blijft.", AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Color.FromArgb(73, 82, 96) });
        AddSettingsRow(workerLayout, ref row, "Compute type", computeTypeCombo);
        AddSettingsRow(workerLayout, ref row, "Transcriptietaal", languageTextBox);
        AddSettingsRow(workerLayout, ref row, "Batch size", batchSizeNumeric);
        AddSettingsRow(workerLayout, ref row, "Transcript-output", outputFormatCombo);
        AddSettingsRow(workerLayout, ref row, "Poll-interval (sec)", pollIntervalNumeric);
        AddSettingsRow(workerLayout, ref row, "Min READY-age (sec)", minReadyAgeNumeric);
        AddSettingsRow(workerLayout, ref row, "Stale started na (min)", staleStartedNumeric);
        AddSettingsRow(workerLayout, ref row, "Max retries (0=onbeperkt)", maxRetryAttemptsNumeric);
        AddSettingsRow(workerLayout, ref row, "Retry backoff (min)", retryBackoffNumeric);
        AddSettingsRow(workerLayout, ref row, "ffmpeg-pad", ffmpegPathTextBox);
        workerCard.Controls.Add(workerLayout);
        stack.Controls.Add(workerCard, 0, 4);

        var notificationsCard = CreateRecorderSurfaceCard(autoSize: true);
        notificationsCard.Margin = new Padding(0, 0, 0, 10);
        notificationsCard.Padding = new Padding(18);
        var notificationsLayout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0), BackColor = CardBackground };
        notificationsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        notificationsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var notificationsRow = 0;
        AddSettingsRow(notificationsLayout, ref notificationsRow, new Label { Text = "Monitoring en notificaties", Font = SectionFont, AutoSize = true, ForeColor = Color.FromArgb(22, 28, 36) }, new Panel { Height = 1, Width = 1, BackColor = CardBackground });
        AddSettingsRow(notificationsLayout, ref notificationsRow, new Label { Text = string.Empty, AutoSize = true }, healthMonitorCheckbox);
        AddSettingsRow(notificationsLayout, ref notificationsRow, new Label { Text = string.Empty, AutoSize = true }, silenceDetectionCheckbox);
        AddSettingsRow(notificationsLayout, ref notificationsRow, "Silence threshold (%)", silenceThresholdNumeric);
        AddSettingsRow(notificationsLayout, ref notificationsRow, "Mic stilte (sec)", micSilenceSecondsNumeric);
        AddSettingsRow(notificationsLayout, ref notificationsRow, "System stilte (sec)", systemSilenceSecondsNumeric);
        AddSettingsRow(notificationsLayout, ref notificationsRow, "Beide stil (sec)", bothSilenceSecondsNumeric);
        AddSettingsRow(notificationsLayout, ref notificationsRow, "Cooldown waarschuwingen (sec)", warningCooldownSecondsNumeric);
        AddSettingsRow(notificationsLayout, ref notificationsRow, "Opname afgerond", CreateInlineCheckboxPanel(recordingStoppedWindowsCheckbox));
        AddSettingsRow(notificationsLayout, ref notificationsRow, "Transcript gereed", CreateInlineCheckboxPanel(transcriptReadyWindowsCheckbox));
        AddSettingsRow(notificationsLayout, ref notificationsRow, "Transcriptie mislukt", CreateInlineCheckboxPanel(transcriptionFailedWindowsCheckbox));
        AddSettingsRow(notificationsLayout, ref notificationsRow, "Opname mislukt of vol", CreateInlineCheckboxPanel(recordingFailureWindowsCheckbox));
        AddSettingsRow(notificationsLayout, ref notificationsRow, "Stilte (beide kanalen)", CreateInlineCheckboxPanel(silenceWarningWindowsCheckbox));
        AddSettingsRow(notificationsLayout, ref notificationsRow, "Een kanaal stil", CreateInlineCheckboxPanel(healthWarningWindowsCheckbox));
        notificationsCard.Controls.Add(notificationsLayout);
        stack.Controls.Add(notificationsCard, 0, 5);

        var retentionCard = CreateRecorderSurfaceCard(autoSize: true);
        retentionCard.Margin = new Padding(0, 0, 0, 10);
        retentionCard.Padding = new Padding(18);
        var retentionLayout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0), BackColor = CardBackground };
        retentionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        retentionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var retentionRow = 0;
        AddSettingsRow(retentionLayout, ref retentionRow, new Label { Text = "Retentie", Font = SectionFont, AutoSize = true, ForeColor = Color.FromArgb(22, 28, 36) }, new Panel { Height = 1, Width = 1, BackColor = CardBackground });
        AddSettingsRow(retentionLayout, ref retentionRow, new Label { Text = string.Empty, AutoSize = true }, retentionEnabledCheckbox);
        AddSettingsRow(retentionLayout, ref retentionRow, "Audio (dagen)", audioRetentionDaysNumeric);
        AddSettingsRow(retentionLayout, ref retentionRow, "Transcripten (dagen)", transcriptRetentionDaysNumeric);
        AddSettingsRow(retentionLayout, ref retentionRow, "Sessiegegevens/logs (dagen)", sessionRecordRetentionDaysNumeric);
        AddSettingsRow(retentionLayout, ref retentionRow, new Label { Text = string.Empty, AutoSize = true }, new Label { Text = "Retentie verwijdert alleen completed sessies. Audio kan eerder worden verwijderd dan transcripten. Sessiegegevens/logs worden niet eerder verwijderd dan transcripten. De uitvoering gebeurt via de workerroute, niet via de GUI zelf.", AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Color.FromArgb(73, 82, 96) });
        retentionCard.Controls.Add(retentionLayout);
        stack.Controls.Add(retentionCard, 0, 6);

        var processingCard = CreateRecorderSurfaceCard(autoSize: true);
        processingCard.Margin = new Padding(0, 0, 0, 10);
        processingCard.Padding = new Padding(18);
        var processingLayout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0), BackColor = CardBackground };
        processingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        processingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var processingRow = 0;
        AddSettingsRow(processingLayout, ref processingRow, new Label { Text = "Import, enhancement en export", Font = SectionFont, AutoSize = true, ForeColor = Color.FromArgb(22, 28, 36) }, new Panel { Height = 1, Width = 1, BackColor = CardBackground });
        AddSettingsRow(processingLayout, ref processingRow, new Label { Text = string.Empty, AutoSize = true }, enhancementEnabledCheckbox);
        AddSettingsRow(processingLayout, ref processingRow, new Label { Text = string.Empty, AutoSize = true }, enhancementImportedOnlyCheckbox);
        AddSettingsRow(processingLayout, ref processingRow, "Enhancementprofiel", enhancementProfileCombo);
        AddSettingsRow(processingLayout, ref processingRow, new Label { Text = string.Empty, AutoSize = true }, exportSrtCheckbox);
        AddSettingsRow(processingLayout, ref processingRow, new Label { Text = string.Empty, AutoSize = true }, exportVttCheckbox);
        var workerToolsPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = CardBackground, Margin = new Padding(0) };
        manageWatchFoldersButton = new Button { Text = "Watch folders beheren", Width = 180, Height = 34 };
        var manageVocabularyButton = new Button { Text = "Custom vocabulary", Width = 180, Height = 34 };
        var importAudioSettingsButton = new Button { Text = "Audio importeren", Width = 180, Height = 34 };
        StyleButton(manageWatchFoldersButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(manageVocabularyButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(importAudioSettingsButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        manageWatchFoldersButton.Click += (_, _) => ShowWatchFoldersDialog();
        manageVocabularyButton.Click += (_, _) => ShowVocabularyDialog();
        importAudioSettingsButton.Click += async (_, _) => await RunBusyUiActionAsync("Audio importeren", ImportAudioFromDialogAsync);
        workerToolsPanel.Controls.Add(manageWatchFoldersButton);
        workerToolsPanel.Controls.Add(manageVocabularyButton);
        workerToolsPanel.Controls.Add(importAudioSettingsButton);
        AddSettingsRow(processingLayout, ref processingRow, "Beheer", workerToolsPanel);
        processingFootnoteLabel = new Label { Text = "Watch folders, custom vocabulary en handmatige audio-import beheer je centraal in Instellingen. Exports worden automatisch naast transcript.json aangemaakt wanneer deze opties zijn ingeschakeld.", AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Color.FromArgb(73, 82, 96) };
        AddSettingsRow(processingLayout, ref processingRow, new Label { Text = string.Empty, AutoSize = true }, processingFootnoteLabel);
        RefreshEditionSettingsUi();
        processingCard.Controls.Add(processingLayout);
        stack.Controls.Add(processingCard, 0, 7);


        detectHardwareForAiButton.Click += async (_, _) =>
        {
            detectHardwareForAiButton.Enabled = false;
            hardwareAssessmentLabel.Text = "Hardware-inschatting loopt...";
            try
            {
                var languageProfile = ConvertLanguageProfileSelection(languageProfileCombo.SelectedItem?.ToString());
                var assessment = await Task.Run(() => HardwareAssessmentService.Detect(languageProfile)).ConfigureAwait(true);
                ApplyHardwareAssessmentToSettings(assessment, persist: true);
                transcriptionProfileCombo.SelectedItem = HardwareAssessmentService.GetTranscriptionProfileDisplayName(assessment.RecommendedTranscriptionProfile);
                hardwareAssessmentLabel.Text = $"{assessment.Summary}{Environment.NewLine}{assessment.Detail}{Environment.NewLine}Toegepast transcriptieprofiel: {assessment.TranscriptionProfileDisplayName}.";
                runtimeHintLabel.Text = WorkerProductionProfile.BuildSummary(new WorkerSettings
                {
                    UseProductionProfile = useProductionProfileCheckbox.Checked,
                    TranscriptionProfile = assessment.RecommendedTranscriptionProfile,
                    ComputeType = computeTypeCombo.SelectedItem?.ToString() ?? WorkerProductionProfile.DefaultComputeType,
                    DiarizationEnabled = false
                });
                activeTranscriptionProfileLabel.Text = BuildActiveTranscriptionProfileText(assessment.RecommendedTranscriptionProfile, assessment);
            }
            catch (Exception ex)
            {
                AppRuntime.LogException("Hardware detectie mislukt", ex);
                hardwareAssessmentLabel.Text = $"Hardware-inschatting niet beschikbaar.{Environment.NewLine}{ex.Message}";
            }
            finally
            {
                detectHardwareForAiButton.Enabled = true;
            }
        };

        languageProfileCombo.SelectedIndexChanged += (_, _) => languageTextBox.Text = LanguageProfileCatalog.GetTranscriptionLanguageCode(ConvertLanguageProfileSelection(languageProfileCombo.SelectedItem?.ToString()));


        var diagnosticsCard = CreateRecorderSurfaceCard(autoSize: true);
        diagnosticsCard.Margin = new Padding(0, 0, 0, 10);
        diagnosticsCard.Padding = new Padding(18);
        var diagnosticsLayout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Margin = new Padding(0), BackColor = CardBackground };
        diagnosticsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        diagnosticsLayout.Controls.Add(new Label { Text = "Diagnostiek en beheer", Font = SectionFont, AutoSize = true, Margin = new Padding(0, 0, 0, 10), ForeColor = Color.FromArgb(22, 28, 36) }, 0, 0);
        diagnosticsLayout.Controls.Add(diagnosticsModeCheckbox, 0, 1);
        diagnosticsLayout.Controls.Add(preserveWorkerOutputCheckbox, 0, 2);
        diagnosticsLayout.Controls.Add(new Label { Text = "session.log blijft altijd aan. diagnostics.log en worker stdout/stderr worden alleen gebruikt wanneer diagnosemodus actief is. Diagnose-export neemt standaard alleen de huidige sessie plus de bijbehorende daglogs mee.", AutoSize = true, MaximumSize = new Size(650, 0), ForeColor = Color.FromArgb(73, 82, 96), Margin = new Padding(0, 4, 0, 10) }, 0, 3);
        var adminButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = CardBackground };
        var connectionTestButton = new Button { Text = "Verbinding testen", Width = 160, Height = 34 };
        var exportDiagnosticsButton = new Button { Text = "Exporteer diagnosepakket", Width = 200, Height = 34 };
        StyleButton(connectionTestButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(exportDiagnosticsButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        adminButtons.Controls.Add(connectionTestButton);
        adminButtons.Controls.Add(exportDiagnosticsButton);
        diagnosticsLayout.Controls.Add(adminButtons, 0, 4);
        diagnosticsCard.Controls.Add(diagnosticsLayout);
        stack.Controls.Add(diagnosticsCard, 0, 9);
        stack.Controls.Add(editionCard, 0, 10);

        scroller.Controls.Add(stack);
        root.Controls.Add(scroller, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0), BackColor = AppBackground };
        var cancelButton = new Button { Text = "Annuleren", DialogResult = DialogResult.Cancel, Width = 110, Height = 34 };
        var saveButton = new Button { Text = "Opslaan", DialogResult = DialogResult.OK, Width = 110, Height = 34 };
        StyleButton(cancelButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(saveButton, AccentColor, AccentHoverColor, Color.White);
        saveButton.FlatAppearance.BorderColor = Color.FromArgb(11, 86, 136);
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(saveButton);
        root.Controls.Add(buttons, 0, 2);

        void UpdateWorkerSettingsEnabledState()
        {
            var workerEnabled = workerEnabledCheckbox.Checked;
            transcriptionProfileCombo.Enabled = workerEnabled;
            pythonPathTextBox.Enabled = workerEnabled;
            modelTextBox.Enabled = workerEnabled;
            cpuModelTextBox.Enabled = workerEnabled;
            computeTypeCombo.Enabled = workerEnabled;
            languageTextBox.Enabled = workerEnabled;
            batchSizeNumeric.Enabled = workerEnabled;
            outputFormatCombo.Enabled = workerEnabled;
            pollIntervalNumeric.Enabled = workerEnabled;
            minReadyAgeNumeric.Enabled = workerEnabled;
            staleStartedNumeric.Enabled = workerEnabled;
            maxRetryAttemptsNumeric.Enabled = workerEnabled;
            retryBackoffNumeric.Enabled = workerEnabled;
            ffmpegPathTextBox.Enabled = workerEnabled;
            preserveWorkerOutputCheckbox.Enabled = workerEnabled;
            enhancementEnabledCheckbox.Enabled = workerEnabled;
            enhancementImportedOnlyCheckbox.Enabled = workerEnabled && enhancementEnabledCheckbox.Checked;
            enhancementProfileCombo.Enabled = workerEnabled && enhancementEnabledCheckbox.Checked;
            exportSrtCheckbox.Enabled = workerEnabled;
            exportVttCheckbox.Enabled = workerEnabled;

            audioRetentionDaysNumeric.Enabled = retentionEnabledCheckbox.Checked;
            transcriptRetentionDaysNumeric.Enabled = retentionEnabledCheckbox.Checked;
            sessionRecordRetentionDaysNumeric.Enabled = retentionEnabledCheckbox.Checked;
            if (sessionRecordRetentionDaysNumeric.Value < transcriptRetentionDaysNumeric.Value)
            {
                sessionRecordRetentionDaysNumeric.Value = transcriptRetentionDaysNumeric.Value;
            }

        }

        void ApplyDialogToSettings(bool persistToken)
        {
            _minimizeToTrayCheckbox.Checked = minimizeToTrayCheckbox.Checked;
            _appSettings.Ui.LanguageProfile = ConvertLanguageProfileSelection(languageProfileCombo.SelectedItem?.ToString());
            _appSettings.Ui.StartWithWindows = startWithWindowsCheckbox.Checked;
            _appSettings.Ui.StartMinimized = startMinimizedCheckbox.Checked;
            _appSettings.Ui.MeetingDetection.Enabled = meetingDetectionEnabledCheckbox.Checked;
            _appSettings.Ui.MeetingDetection.DetectTeams = detectTeamsCheckbox.Checked;
            _appSettings.Ui.MeetingDetection.DetectZoom = detectZoomCheckbox.Checked;
            _appSettings.Ui.MeetingDetection.DetectBrowserMeetings = detectBrowserMeetingsCheckbox.Checked;
            _appSettings.Ui.MeetingDetection.StabilitySeconds = (int)meetingDetectionDelayNumeric.Value;
            _appSettings.Ui.MeetingDetection.PromptCooldownMinutes = (int)meetingDetectionCooldownNumeric.Value;
            _appSettings.Ui.DiagnosticsMode = diagnosticsModeCheckbox.Checked;
            _appSettings.Ui.Monitoring.HealthMonitorEnabled = healthMonitorCheckbox.Checked;
            _appSettings.Ui.Monitoring.SilenceDetectionEnabled = silenceDetectionCheckbox.Checked;
            _appSettings.Ui.Monitoring.SilenceThresholdPercent = (double)silenceThresholdNumeric.Value;
            _appSettings.Ui.Monitoring.MicSilenceWarningSeconds = (int)micSilenceSecondsNumeric.Value;
            _appSettings.Ui.Monitoring.SystemSilenceWarningSeconds = (int)systemSilenceSecondsNumeric.Value;
            _appSettings.Ui.Monitoring.BothSilenceWarningSeconds = (int)bothSilenceSecondsNumeric.Value;
            _appSettings.Ui.Monitoring.WarningCooldownSeconds = (int)warningCooldownSecondsNumeric.Value;
            _appSettings.Ui.Notifications.RecordingStoppedWaitingForTranscription.WindowsEnabled = recordingStoppedWindowsCheckbox.Checked;
            _appSettings.Ui.Notifications.TranscriptReady.WindowsEnabled = transcriptReadyWindowsCheckbox.Checked;
            _appSettings.Ui.Notifications.TranscriptionFailed.WindowsEnabled = transcriptionFailedWindowsCheckbox.Checked;
            _appSettings.Ui.Notifications.SilenceWarning.WindowsEnabled = silenceWarningWindowsCheckbox.Checked;
            _appSettings.Ui.Notifications.RecordingHealthWarning.WindowsEnabled = healthWarningWindowsCheckbox.Checked;
            _appSettings.Ui.Notifications.RecordingFailure.WindowsEnabled = recordingFailureWindowsCheckbox.Checked;
            _appSettings.Worker.PreserveWorkerOutput = preserveWorkerOutputCheckbox.Checked;
            _appSettings.Worker.Retention.Enabled = retentionEnabledCheckbox.Checked;
            _appSettings.Worker.Retention.AudioRetentionDays = (int)audioRetentionDaysNumeric.Value;
            _appSettings.Worker.Retention.TranscriptRetentionDays = (int)transcriptRetentionDaysNumeric.Value;
            _appSettings.Worker.Retention.SessionRecordRetentionDays = Math.Max((int)sessionRecordRetentionDaysNumeric.Value, (int)transcriptRetentionDaysNumeric.Value);
            _appSettings.Worker.Enhancement.Enabled = enhancementEnabledCheckbox.Checked;
            _appSettings.Worker.Enhancement.ImportedAudioOnly = enhancementImportedOnlyCheckbox.Checked;
            _appSettings.Worker.Enhancement.Profile = enhancementProfileCombo.SelectedItem?.ToString() ?? "light-clean";
            _appSettings.Worker.ExportSrt = exportSrtCheckbox.Checked;
            _appSettings.Worker.ExportVtt = exportVttCheckbox.Checked;
            _appSettings.Worker.WhisperXEnabled = workerEnabledCheckbox.Checked;
            _appSettings.Worker.UseProductionProfile = useProductionProfileCheckbox.Checked;
            _appSettings.Worker.TranscriptionProfile = ConvertTranscriptionProfileSelection(transcriptionProfileCombo.SelectedItem?.ToString());
            // AMD GPU hier kiezen is hetzelfde als "Snel via de videokaart (AMD)" onder Instellingen, Opnemen.
            _appSettings.Worker.AmdGpuFastRoute = _appSettings.Worker.TranscriptionProfile == "amd-gpu";
            _appSettings.Worker.RuntimeMode = "python";
            _appSettings.Worker.PythonExe = pythonPathTextBox.Text.Trim();
            _appSettings.Worker.WhisperXModel = modelTextBox.Text.Trim();
            _appSettings.Worker.CpuWhisperXModel = string.IsNullOrWhiteSpace(cpuModelTextBox.Text) ? WorkerSettings.DefaultCpuWhisperXModel : cpuModelTextBox.Text.Trim();
            _appSettings.Worker.ComputeType = useProductionProfileCheckbox.Checked ? WorkerProductionProfile.DefaultComputeType : (Convert.ToString(computeTypeCombo.SelectedItem) ?? WorkerProductionProfile.DefaultComputeType);
            _appSettings.Worker.Language = LanguageProfileCatalog.GetTranscriptionLanguageCode(_appSettings.Ui.LanguageProfile);
            _appSettings.Worker.BatchSize = (int)batchSizeNumeric.Value;
            _appSettings.Worker.TranscriptOutputFormat = Convert.ToString(outputFormatCombo.SelectedItem) ?? "txt+json";
            _appSettings.Worker.PollIntervalSec = useProductionProfileCheckbox.Checked ? WorkerProductionProfile.DefaultPollIntervalSec : (int)pollIntervalNumeric.Value;
            _appSettings.Worker.MinReadyAgeSec = useProductionProfileCheckbox.Checked ? WorkerProductionProfile.DefaultMinReadyAgeSec : (int)minReadyAgeNumeric.Value;
            _appSettings.Worker.RetryStaleStartedAfterMinutes = (int)staleStartedNumeric.Value;
            _appSettings.Worker.MaxRetryAttempts = (int)maxRetryAttemptsNumeric.Value;
            _appSettings.Worker.RetryBackoffMinutes = (int)retryBackoffNumeric.Value;
            _appSettings.Worker.FfmpegPath = ffmpegPathTextBox.Text.Trim();

            SaveCurrentSettings();

        }


        static string ConvertTranscriptionProfileSelection(string? value)
            => HardwareAssessmentService.NormalizeTranscriptionProfile(value);

        bool TryApplyDialogToSettings(bool persistToken)
        {
            try
            {
                ApplyDialogToSettings(persistToken);
                return true;
            }
            catch (Exception ex)
            {
                AppRuntime.LogException("Settings save/token persistence failed", ex);
                ShowOwnedMessage(dialog, ex.Message, "Instellingen opslaan mislukt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            finally
            {
                dialog.UseWaitCursor = false;
                ResetUiWaitCursorState();
            }
        }

        workerEnabledCheckbox.CheckedChanged += (_, _) => UpdateWorkerSettingsEnabledState();
        retentionEnabledCheckbox.CheckedChanged += (_, _) => UpdateWorkerSettingsEnabledState();
        UpdateWorkerSettingsEnabledState();


        connectionTestButton.Click += async (_, _) =>
        {
            if (!TryApplyDialogToSettings(persistToken: true))
            {
                return;
            }

            connectionTestButton.Enabled = false;
            var originalText = connectionTestButton.Text;
            connectionTestButton.Text = "Test draait...";
            try
            {
                await RunWorkerConnectionTestAsync(this);
            }
            finally
            {
                connectionTestButton.Text = originalText;
                connectionTestButton.Enabled = true;
            }
        };

        exportDiagnosticsButton.Click += (_, _) =>
        {
            if (!TryApplyDialogToSettings(persistToken: true))
            {
                return;
            }

            ExportDiagnosticsPackage(this);
        };

        dialog.Controls.Add(root);
        dialog.AcceptButton = saveButton;
        dialog.CancelButton = cancelButton;
        UiTextBinder.Attach(dialog);

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (TryApplyDialogToSettings(persistToken: true))
            {
                ResetUiWaitCursorState();
                BeginInvoke(new Action(() =>
                {
                    ResetUiWaitCursorState();
                    ApplyCurrentLanguageToUi();
                    RefreshShellAfterLanguagePass();
                    SetStatus("Instellingen bijgewerkt.", "Recorder- en workerinstellingen zijn bijgewerkt voor nieuwe sessies.");
                    ResetUiWaitCursorState();
                }));
            }
            else
            {
                ResetUiWaitCursorState();
            }
        }
    }

    private void BrowseButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            ShowNewFolderButton = true,
            InitialDirectory = Directory.Exists(_outputRootTextBox.Text)
                ? _outputRootTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputRootTextBox.Text = dialog.SelectedPath;
        }
    }

    private void OpenOutputFolder()
    {
        try
        {
            var folder = _manifest?.OutputDirectory ?? _outputRootTextBox.Text;
            if (Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = folder,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Open output folder failed", ex);
            ShowOwnedMessage(this, ex.Message, "Uitvoermap openen mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateElapsed()
    {
        if (_recordingStopwatch.IsRunning)
        {
            var running = _recordingStopwatch.Elapsed;
            var text = running.ToString(@"hh\:mm\:ss");
            _elapsedLabel.Text = text;
            _elapsedStatusLabel.Text = text;
            _headerElapsedLabel.Text = text;
            UpdateShellElapsed(text);
            return;
        }

        if (_startedAt is null)
        {
            _elapsedLabel.Text = "00:00:00";
            _elapsedStatusLabel.Text = "00:00:00";
            _headerElapsedLabel.Text = "00:00:00";
            return;
        }

        var elapsed = DateTimeOffset.Now - _startedAt.Value;
        var formatted = elapsed.ToString(@"hh\:mm\:ss");
        _elapsedLabel.Text = formatted;
        _elapsedStatusLabel.Text = formatted;
        _headerElapsedLabel.Text = formatted;
        UpdateShellElapsed(formatted);
    }

    private bool CanStartRecordingNow()
        => _micComboBox.SelectedItem is DeviceInfo && _systemComboBox.SelectedItem is DeviceInfo;

    private void SetRecorderDeviceLoadingState(bool isLoading)
    {
        _audioDeviceLoading = isLoading;
        _audioDevicesLoaded = !isLoading && CanStartRecordingNow();

        if (_session is not null)
        {
            SetRunningState(true);
            return;
        }

        SetRunningState(false);

        if (isLoading)
        {
            _selectedMicLabel.Text = "Audioapparaten laden...";
            _selectedSystemLabel.Text = "Audioapparaten laden...";
        }
    }

    private void UpdateSelectedDeviceLabels()
    {
        // Ook de opmerking onder Instellingen, Opnemen (na laden of opnieuw zoeken).
        RefreshSettingsDeviceNote();
        _selectedMicLabel.Text = (_micComboBox.SelectedItem as DeviceInfo)?.FriendlyName ?? "Niet geselecteerd";
        _selectedSystemLabel.Text = (_systemComboBox.SelectedItem as DeviceInfo)?.FriendlyName ?? "Niet geselecteerd";
        _audioDevicesLoaded = CanStartRecordingNow();

        SetRunningState(_session is not null);
    }

    private void ResetMeters()
    {
        _micLevelMeter.Value = 0f;
        _systemLevelMeter.Value = 0f;
        _headerSignalRibbon.ResetHistory();
        _micLevelValueLabel.Text = "0%";
        _systemLevelValueLabel.Text = "0%";
        _headerMicLevelMeter.Value = 0;
        _headerSystemLevelMeter.Value = 0;
        _recordingPageMicMeter.Value = 0;
        _recordingPageSystemMeter.Value = 0;
    }

    private void Session_LevelsUpdated(object? sender, CaptureLevelsEventArgs e)
    {
        if (!IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ApplyLevels(e)));
            return;
        }

        ApplyLevels(e);
    }

    private void ApplyLevels(CaptureLevelsEventArgs e)
    {
        var micPeak = Math.Clamp(e.MicPeak, 0f, 1f);
        var systemPeak = Math.Clamp(e.SystemPeak, 0f, 1f);
        _micLevelMeter.Value = micPeak;
        _systemLevelMeter.Value = systemPeak;
        _headerSignalRibbon.AddSample(micPeak, systemPeak);
        _micLevelValueLabel.Text = $"{Math.Round(micPeak * 100)}%";
        _systemLevelValueLabel.Text = $"{Math.Round(systemPeak * 100)}%";
        _headerMicLevelMeter.Value = micPeak;
        _headerSystemLevelMeter.Value = systemPeak;
        _recordingPageMicMeter.Value = micPeak;
        _recordingPageSystemMeter.Value = systemPeak;

        var threshold = (float)Math.Clamp(_appSettings.Ui.Monitoring.SilenceThresholdPercent / 100.0, 0.001, 1.0);
        var now = DateTimeOffset.Now;
        if (micPeak >= threshold)
        {
            _lastMicSignalAt = now;
        }

        if (systemPeak >= threshold)
        {
            _lastSystemSignalAt = now;
        }

        UpdateRecordingHealthState();
    }

    private void ConfigureTray()
    {
        _notifyIcon.Icon = Branding.GetAppIconOrDefault();
        _notifyIcon.Visible = true;
        _notifyIcon.ContextMenuStrip = _trayMenu;
        _notifyIcon.DoubleClick += (_, _) => ToggleTrayVisibility();

        _trayShowHideItem.Click += (_, _) => ToggleTrayVisibility();
        _trayToggleRecordItem.Click += (_, _) => BeginInvoke(new Action(() => _ = RunRecordingUiActionAsync(_session is null ? "Opname starten" : "Opname stoppen", ToggleRecordingAsync)));
        _trayOpenFolderItem.Click += (_, _) => OpenOutputFolder();
        _trayExitItem.Click += (_, _) => BeginInvoke(new Action(Close));

        _trayWaitingItem.Click += (_, _) =>
        {
            // Ook een geminimaliseerd venster (Visible is dan nog true) naar voren halen.
            if (!Visible || WindowState == FormWindowState.Minimized) ShowFromTray();
            else Activate();
            NavigateShell(ShellNavToday);
        };
        _trayMenu.Items.AddRange(new ToolStripItem[]
        {
            _trayWaitingItem,
            _trayShowHideItem,
            _trayToggleRecordItem,
            new ToolStripSeparator(),
            _trayOpenFolderItem,
            new ToolStripSeparator(),
            _trayExitItem
        });
    }

    private void MainForm_Shown(object? sender, EventArgs e)
    {
        // Bovenaan: een fout verderop in Shown mag toonverzoeken van een tweede start niet voorgoed blokkeren. Beide
        // zetten alleen werk in de wachtrij, dat pas na Shown wordt uitgevoerd.
        ApplyInitialWindowLaunchState();
        // Na de (in de wachtrij gezette) minimaliseeractie, zodat een vroeg toonverzoek het venster echt laat staan.
        ReleasePendingShowRequests();
        RegisterHotKeys();
        UpdateRecordingHealthLabels("Idle", "Niet actief", "Niet actief", "Niet actief");
        UpdateWorkerObservabilityLabels("Nog niet gescand", "Nog niet gescand", "Nog niet gescand", "Nog niet gescand", "Nog niet gescand", "Geen actieve sessie", false);
        SetStatus("Gereed.", "Recorder klaar. Gebruik Instellingen en admin voor transcriptiewerker, diagnosemodus, bibliotheekverrijking en verbindingstesten.");
        _ = RunPostStartupInitializationAsync();
    }

    private async Task RunPostStartupInitializationAsync()
    {
        var startupWatch = Stopwatch.StartNew();
        await Task.Yield();

        if (IsDisposed)
        {
            return;
        }

        AppRuntime.LogMessage($"Startup timing: constructor/UI-opbouw {_startupConstructorElapsedMs} ms; post-startup initialisatie gestart na eerste vensterweergave.");

        _notificationPollTimer.Start();
        StartShellBackgroundRefresh();
        UpdateMeetingDetectionTimerState();
        _ = Task.Run(EnsureWindowsStartupRegistration);

        _ = RunStartupStepAsync("audio-device-load", () => LoadDevicesAsync(showMessageBoxOnFailure: false));

        await Task.Delay(150).ConfigureAwait(true);
        _ = RunStartupStepAsync("hardware-profile-detect", RunStartupHardwareDetectionAsync);

        await Task.Delay(250).ConfigureAwait(true);
        _ = RunStartupStepAsync("recovery", RecoverInterruptedSessionsOnStartupAsync);

        await Task.Delay(250).ConfigureAwait(true);
        _ = RunStartupStepAsync("worker-start", () =>
        {
            EnsureWorkerProcessState();
            return Task.CompletedTask;
        });

        await Task.Delay(250).ConfigureAwait(true);
        _ = RunStartupStepAsync("background-poll", PollBackgroundStateAsync);

        if (_mainTabs?.SelectedTab?.Text == UiText.T("Sessiebibliotheek"))
        {
            try
            {
                EnsureLibraryTabBuilt();
                await RunLibraryUiActionAsync("Post-startup bibliotheek verversen", ct => RefreshLibraryAsync(ct));
            }
            catch (Exception ex)
            {
                AppRuntime.LogException("Post-startup library refresh failed", ex);
            }
        }
        else
        {
            _workerStatusLabel.Text = "Bibliotheekmetadata start bij openen bibliotheektab";
            _queueStatusLabel.Text = "Bibliotheek niet gescand bij startup";
        }

        AppRuntime.LogMessage($"Post-startup initialisatie gepland/afgerond in {startupWatch.ElapsedMilliseconds} ms. Startup prioriteit: eerst opnamegereedheid, daarna worker/recovery/status.");
    }

    private async Task RunStartupStepAsync(string stepName, Func<Task> action)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            await action().ConfigureAwait(true);
            AppRuntime.LogMessage($"Startup timing: {stepName} afgerond in {watch.ElapsedMilliseconds} ms.");
        }
        catch (Exception ex)
        {
            AppRuntime.LogException($"Startup step failed: {stepName}", ex);
        }
    }

    private void MainForm_Resize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized && _minimizeToTrayCheckbox.Checked)
        {
            HideToTray();
        }
    }

    private void HideToTray(bool showBalloon = true)
    {
        ShowInTaskbar = false;
        Hide();
        _trayShowHideItem.Text = UiText.T("Venster tonen");

        if (showBalloon && !_trayBalloonShown)
        {
            _notifyIcon.ShowBalloonTip(2500, Branding.DisplayName, UiText.T("Applicatie draait in de systeemtray."), ToolTipIcon.Info);
            _trayBalloonShown = true;
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        Activate();
        BringToFront();
        _trayShowHideItem.Text = UiText.T("Venster verbergen");
    }

    // Toonverzoeken van een tweede start (SingleInstanceGuard). Een verzoek dat binnenkomt voordat Shown
    // klaar is, wacht tot daarna: anders zou het verloren gaan (nog geen venster) of zou de minimaliseeractie uit
    // ApplyInitialWindowLaunchState het venster meteen weer verbergen.
    private readonly object _showRequestLock = new();
    private readonly List<(Action Acknowledge, DateTime RequestedUtc)> _pendingShowAcknowledgements = new();
    private bool _showRequestsReady;

    // Aangeroepen op een threadpoolthread, een keer per verzoek. De bevestiging volgt pas nadat de UI-thread het
    // venster getoond heeft: zo blijkt ook dat dit exemplaar reageert.
    internal void ShowFromOtherInstance(Action acknowledge)
    {
        (Action Acknowledge, DateTime RequestedUtc) request = (acknowledge, DateTime.UtcNow);
        lock (_showRequestLock)
        {
            if (!_showRequestsReady)
            {
                _pendingShowAcknowledgements.Add(request);
                return;
            }
        }

        PostShowFromOtherInstance(new[] { request });
    }

    private void ReleasePendingShowRequests()
    {
        (Action Acknowledge, DateTime RequestedUtc)[] pending;
        lock (_showRequestLock)
        {
            _showRequestsReady = true;
            pending = _pendingShowAcknowledgements.ToArray();
            _pendingShowAcknowledgements.Clear();
        }

        if (pending.Length > 0)
        {
            PostShowFromOtherInstance(pending);
        }
    }

    private void PostShowFromOtherInstance(IReadOnlyList<(Action Acknowledge, DateTime RequestedUtc)> requests)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            BeginInvoke(new Action(() =>
            {
                ShowFromTray();
                foreach (var request in requests)
                {
                    // Een te late bevestiging blijft staan en zou een volgende start ten onrechte geruststellen.
                    if (DateTime.UtcNow - request.RequestedUtc < SingleInstanceGuard.ShowAcknowledgeWindow)
                    {
                        request.Acknowledge();
                    }
                }
            }));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // Het venster sluit net; een onafgevangen fout op een threadpoolthread zou de recorder laten crashen.
        }
    }

    private void ToggleTrayVisibility()
    {
        if (Visible && WindowState != FormWindowState.Minimized)
        {
            HideToTray(showBalloon: false);
        }
        else
        {
            ShowFromTray();
        }
    }

    private void RegisterHotKeys()
    {
        if (_hotkeysRegistered || !IsHandleCreated)
        {
            return;
        }

        // Elke sneltoets apart bijhouden: lukt er maar een, dan moet die bij het afsluiten ook weer worden vrijgegeven.
        _recordHotkeyRegistered = NativeMethods.RegisterHotKey(Handle, RecordToggleHotKeyId, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, NativeMethods.VK_F9);
        _showHideHotkeyRegistered = NativeMethods.RegisterHotKey(Handle, ShowHideHotKeyId, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, NativeMethods.VK_F10);
        _hotkeysRegistered = true;

        if (!_recordHotkeyRegistered || !_showHideHotkeyRegistered)
        {
            SetStatus("Hotkeys niet volledig geregistreerd.", "Globale hotkeys konden niet volledig worden geregistreerd. De recorder blijft wel bruikbaar via GUI en tray.", isError: true);
        }
    }

    private void UnregisterHotKeys()
    {
        if (!_hotkeysRegistered || !IsHandleCreated)
        {
            return;
        }

        if (_recordHotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, RecordToggleHotKeyId);
            _recordHotkeyRegistered = false;
        }

        if (_showHideHotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, ShowHideHotKeyId);
            _showHideHotkeyRegistered = false;
        }

        _hotkeysRegistered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            var id = m.WParam.ToInt32();
            if (id == RecordToggleHotKeyId)
            {
                BeginInvoke(new Action(() => _ = RunRecordingUiActionAsync(_session is null ? "Hotkey opname starten" : "Hotkey opname stoppen", ToggleRecordingAsync)));
            }
            else if (id == ShowHideHotKeyId)
            {
                BeginInvoke(new Action(ToggleTrayVisibility));
            }
        }

        base.WndProc(ref m);
    }

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_stopFinalizeInProgress && e.CloseReason == CloseReason.UserClosing)
        {
            var result = ShowOwnedMessage(
                this,
                "Captivox Recorder rondt de opname nog af. Wil je afsluiten zodra afronden en mixen gereed zijn?",
                "Opname wordt afgerond",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            e.Cancel = true;
            if (result == DialogResult.Yes)
            {
                _closeAfterFinalize = true;
                SetStatus("Afsluiten wacht op afronden.", "Captivox Recorder sluit automatisch af zodra de lopende afronding gereed is.");
            }
            return;
        }

        if (_session is not null && !_closingAfterStop && e.CloseReason == CloseReason.UserClosing)
        {
            var result = ShowOwnedMessage(
                this,
                "Er loopt nog een opname. Weet je zeker dat Captivox Recorder moet afsluiten? De actieve opname wordt dan eerst gestopt.",
                "Opname nog actief",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            e.Cancel = true;
            _closingAfterStop = true;
            try
            {
                _closeAfterFinalize = true;
                await StopRecordingAsync();
            }
            finally
            {
                _closingAfterStop = false;
            }
            return;
        }

        SaveCurrentSettings();
        StopWorkerProcess();
        CleanupUiResources();
    }

    private void CleanupUiResources()
    {
        if (_uiResourcesCleaned)
        {
            return;
        }

        _uiResourcesCleaned = true;
        foreach (var dialog in _transcriptReadyDialogs.Values.ToList())
        {
            try
            {
                if (!dialog.IsDisposed)
                {
                    dialog.Close();
                    dialog.Dispose();
                }
            }
            catch
            {
            }
        }
        _transcriptReadyDialogs.Clear();
        UnregisterHotKeys();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _todayRefreshTimer.Stop(); _meetingsSearchTimer.Stop();
        DisposeShellTrayIcons();
    }

    private void UpdateRecordingHealthLabels(string overall, string mic, string system, string silence)
    {
        _recordingHealthLabel.Text = overall;
        _micHealthLabel.Text = mic;
        _systemHealthLabel.Text = system;
        _silenceStateLabel.Text = silence;
        UpdateShellHealth(overall, mic, system);
    }

    private void UpdateRecordingHealthState()
    {
        if (!_appSettings.Ui.Monitoring.HealthMonitorEnabled)
        {
            UpdateRecordingHealthLabels("Uitgeschakeld", "Uitgeschakeld", "Uitgeschakeld", "Uitgeschakeld");
            return;
        }

        if (_session is null || !_recordingStopwatch.IsRunning)
        {
            if (_silenceStateLabel.Text == UiText.T("Wacht op transcriptie"))
            {
                return;
            }

            UpdateRecordingHealthLabels("Idle", "Niet actief", "Niet actief", "Niet actief");
            return;
        }

        var now = DateTimeOffset.Now;
        if (_session.CaptureFailed)
        {
            // De opname is dood; dat mag niet stil blijven tot de gebruiker zelf stopt.
            var failureText = _session.CaptureFailureSummary ?? "onbekende opnamefout";
            UpdateRecordingHealthLabels("Opname mislukt", "Gestopt", "Gestopt", failureText);
            NotifyRecordingFailure(now, "Opname is gestopt", $"capture is voortijdig gestopt: {failureText}");
            return;
        }

        if (_session.CapacityReached)
        {
            // De stemwriter kapt stil af op de 4 GiB-grens: de capture leeft door en
            // de niveaus blijven binnenkomen, maar er wordt niets meer opgeslagen. Voor de gebruiker
            // is dat net zo erg als een dode capture, dus hetzelfde alarm.
            const string capacityText = "WAV-grens van 4 GiB bereikt; nieuwe audio wordt niet meer opgeslagen. Stop de opname en start een nieuwe.";
            UpdateRecordingHealthLabels("Opname vol", "Afgekapt", "Afgekapt", capacityText);
            NotifyRecordingFailure(now, "Opname is vol", capacityText);
            return;
        }

        var remainingCapacity = _session.RemainingCapacity;
        var capacityStage = RecordingHealthPolicy.CapacityWarningStage(remainingCapacity);
        if (capacityStage > _capacityWarningStage && remainingCapacity is { } remaining)
        {
            // Een keer per waarschuwingsmoment, niet per cooldown: dit is een aftelling, geen storing.
            _capacityWarningStage = capacityStage;
            var warningText = $"Nog ongeveer {Math.Ceiling(remaining.TotalMinutes)} minuten opnameruimte. Stop de opname en start een nieuwe om niets te verliezen.";
            AppRuntime.LogMessage($"Opname-health: opname bijna vol: {warningText}");
            _ = _notificationDispatcher.NotifyAsync(NotificationEventType.RecordingFailure, "Opname bijna vol", warningText, ToolTipIcon.Warning);
        }

        var micSilentFor = now - _lastMicSignalAt;
        var systemSilentFor = now - _lastSystemSignalAt;
        var monitoring = _appSettings.Ui.Monitoring;
        var micSilent = monitoring.SilenceDetectionEnabled && micSilentFor.TotalSeconds >= monitoring.MicSilenceWarningSeconds;
        var systemSilent = monitoring.SilenceDetectionEnabled && systemSilentFor.TotalSeconds >= monitoring.SystemSilenceWarningSeconds;
        var bothSilent = monitoring.SilenceDetectionEnabled && micSilent && systemSilent && Math.Min(micSilentFor.TotalSeconds, systemSilentFor.TotalSeconds) >= monitoring.BothSilenceWarningSeconds;

        var micStatus = micSilent ? $"Stil ({Math.Floor(micSilentFor.TotalSeconds)}s)" : "OK";
        var systemStatus = systemSilent ? $"Stil ({Math.Floor(systemSilentFor.TotalSeconds)}s)" : "OK";
        var overall = bothSilent ? "Waarschuwing" : (micSilent || systemSilent ? "Let op" : "Healthy");
        if (capacityStage > 0 && remainingCapacity is { } left)
        {
            overall = $"Bijna vol (nog {Math.Ceiling(left.TotalMinutes)} min)";
        }
        var silence = bothSilent ? "Beide kanalen stil" : micSilent ? "Mic stil" : systemSilent ? "System stil" : "Geen stilte-waarschuwing";
        UpdateRecordingHealthLabels(overall, micStatus, systemStatus, silence);

        // Komt er weer geluid van de anderen, dan geldt een bevestigde stilte niet meer. Een bevestigde stilte van de anderen
        // geeft (tien minuten) geen kanaalmelding; zijn beide kanalen stil, dan blijft die melding gewoon staan.
        if (!systemSilent) _systemSilenceAcknowledgedAt = null;
        var acknowledged = RecordingHealthPolicy.SilenceAcknowledgementActive(_systemSilenceAcknowledgedAt, now);
        _ = MaybeSendMonitoringNotificationAsync(now, bothSilent, micSilent || (systemSilent && !acknowledged), silence, overall);
    }

    private void NotifyRecordingFailure(DateTimeOffset now, string title, string message)
    {
        // Herhaalt per cooldown zolang de opname doorloopt: de gebruiker moet zelf stoppen.
        if (now - _lastCaptureFailureNotificationAt < TimeSpan.FromSeconds(Math.Max(10, _appSettings.Ui.Monitoring.WarningCooldownSeconds)))
        {
            return;
        }

        _lastCaptureFailureNotificationAt = now;
        AppRuntime.LogMessage($"Opname-health: {message}");
        _ = _notificationDispatcher.NotifyAsync(NotificationEventType.RecordingFailure, title, message, ToolTipIcon.Error);
    }

    private async Task MaybeSendMonitoringNotificationAsync(DateTimeOffset now, bool bothSilent, bool anyChannelSilent, string silenceMessage, string healthState)
    {
        var eventType = RecordingHealthPolicy.SilenceNotificationType(bothSilent, anyChannelSilent);
        var cooldown = Math.Max(10, _appSettings.Ui.Monitoring.WarningCooldownSeconds);
        if (!_appSettings.Ui.Monitoring.SilenceDetectionEnabled || eventType is null || now - _lastSilenceNotificationAt < TimeSpan.FromSeconds(cooldown))
        {
            return;
        }

        _lastSilenceNotificationAt = now;
        // Ook loggen: zonder dit was achteraf niet te zien dat er stilte was, en de
        // Windows-melding kan uit staan.
        AppRuntime.LogMessage($"Opname-health: {silenceMessage} (status {healthState}).");
        var title = eventType == NotificationEventType.SilenceWarning ? "Stilte gedetecteerd" : "Kanaal stil";
        await _notificationDispatcher.NotifyAsync(eventType.Value, title, silenceMessage, ToolTipIcon.Warning);
    }

    private void UpdateWorkerObservabilityLabels(string workerStatus, string queueStatus, string workerActivity, string retentionStatus, string workerPhase, string workerSession, bool isProcessing)
    {
        _workerStatusLabel.Text = workerStatus;
        _queueStatusLabel.Text = queueStatus;
        _workerActivityLabel.Text = workerActivity;
        _retentionStatusLabel.Text = retentionStatus;
        _workerPhaseLabel.Text = workerPhase;
        _workerSessionLabel.Text = workerSession;
        if (isProcessing)
        {
            _workerProgressBar.Style = ProgressBarStyle.Marquee;
            _workerProgressBar.MarqueeAnimationSpeed = 28;
            _footerWorkerProgressBar.Style = ProgressBarStyle.Marquee;
            _footerWorkerProgressBar.MarqueeAnimationSpeed = 28;
            _footerWorkerProgressBar.ToolTipText = "Worker actief: " + workerPhase;
        }
        else
        {
            _workerProgressBar.Style = ProgressBarStyle.Blocks;
            _workerProgressBar.MarqueeAnimationSpeed = 0;
            _workerProgressBar.Value = 0;
            _footerWorkerProgressBar.Style = ProgressBarStyle.Blocks;
            _footerWorkerProgressBar.MarqueeAnimationSpeed = 0;
            _footerWorkerProgressBar.Value = 0;
            _footerWorkerProgressBar.ToolTipText = "Worker gereed: " + workerStatus;
        }
    }

    private async Task PollBackgroundStateAsync()
    {
        if (_backgroundPollBusy)
        {
            return;
        }

        _backgroundPollBusy = true;
        try
        {
            EnsureWorkerProcessState();
            var settingsSnapshot = _appSettings;
            var result = await Task.Run(() =>
            {
                var queue = SessionInventoryService.ScanQueue(settingsSnapshot, 250);
                var workerRuntime = SessionInventoryService.TryLoadWorkerRuntime();
                var retentionRuntime = RetentionPolicyService.TryLoadRuntime();
                return (queue, workerRuntime, retentionRuntime);
            });

            if (IsDisposed)
            {
                return;
            }

            if (await ProcessTranscriptionNotificationsAsync(result.queue))
            {
                // Een opname veranderde van stand (uitgeschreven, mislukt): de lijst Overleggen meteen bijwerken.
                _ = RefreshMeetingsAsync();
            }

            ApplyWorkerAndQueueStatus(result.queue, result.workerRuntime, result.retentionRuntime);
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Polling worker and queue status failed", ex);
        }
        finally
        {
            _backgroundPollBusy = false;
        }
    }

    // Geeft true als een opname van stand veranderde sinds de vorige ronde.
    private async Task<bool> ProcessTranscriptionNotificationsAsync(SessionQueueSnapshot snapshot)
    {
        var changed = false;
        if (!_notificationStatesPrimed)
        {
            foreach (var manifest in snapshot.SessionManifests)
            {
                var initialStatus = NormalizeStatus(manifest.TranscriptionStatus);
                if (!string.IsNullOrWhiteSpace(initialStatus) && !string.IsNullOrWhiteSpace(manifest.MeetingId))
                {
                    _sessionNotificationStates[manifest.MeetingId] = initialStatus;
                }
            }

            _notificationStatesPrimed = true;
            return false;
        }

        foreach (var manifest in snapshot.SessionManifests)
        {
            if (string.IsNullOrWhiteSpace(manifest.MeetingId))
            {
                continue;
            }

            var status = NormalizeStatus(manifest.TranscriptionStatus);
            if (string.IsNullOrWhiteSpace(status))
            {
                continue;
            }

            _sessionNotificationStates.TryGetValue(manifest.MeetingId, out var previousStatus);
            if (string.Equals(previousStatus, status, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _sessionNotificationStates[manifest.MeetingId] = status;
            changed = true;

            if (status == "completed")
            {
                // Uitgeschreven: een melding met een klik naar de tekst (als die aan staat).
                ShowTranscriptReadyActionsDialog(manifest);
            }
            else if (status is "failed" or "error")
            {
                var message = TranscriptionRuntimeStatus.LooksLikeRuntimeFailure(manifest.TranscriptionError) || TranscriptionRuntimeStatus.IsRuntimeMissing(_appSettings.Worker)
                    ? $"Transcriptie mislukt voor {manifest.MeetingId}. {TranscriptionRuntimeStatus.MissingMessage}"
                    : !string.IsNullOrWhiteSpace(manifest.TranscriptionError)
                        ? $"Transcriptie mislukt voor {manifest.MeetingId}: {manifest.TranscriptionError}"
                        : $"Transcriptie mislukt voor {manifest.MeetingId}.";
                await _notificationDispatcher.NotifyAsync(NotificationEventType.TranscriptionFailed, "Transcriptie mislukt", message, ToolTipIcon.Error);
            }
        }

        return changed;
    }

    private void ApplyWorkerAndQueueStatus(SessionQueueSnapshot queue, WorkerRuntimeSnapshot? workerRuntime, RetentionRuntimeSnapshot? retentionRuntime)
    {
        var queueStatus = queue.ToSummaryText();
        var queueDetails = $"{queueStatus} | Actieve READY: {queue.ReadyMarkerCount} | Claimed: {queue.ClaimedCount} | Klaar: {queue.CompletedCount} | Gescand: {queue.ScannedSessionCount}";
        var retentionDetails = BuildRetentionStatusText(retentionRuntime);
        var expectedWorkerVersion = AppRuntime.GetWorkerCompatibilityVersionTag();
        if (workerRuntime is not null && !string.Equals(workerRuntime.Version, expectedWorkerVersion, StringComparison.OrdinalIgnoreCase))
        {
            var staleVersion = workerRuntime.Version ?? "unknown";
            var shouldLogStaleStatus = !string.Equals(_lastIgnoredWorkerStatusVersion, staleVersion, StringComparison.OrdinalIgnoreCase)
                || DateTimeOffset.UtcNow - _lastIgnoredWorkerStatusLogAt >= StaleWorkerStatusLogInterval;
            if (shouldLogStaleStatus)
            {
                AppRuntime.LogMessage($"Verouderde workerstatus genegeerd: status={staleVersion}, verwacht={expectedWorkerVersion}.");
                _lastIgnoredWorkerStatusVersion = staleVersion;
                _lastIgnoredWorkerStatusLogAt = DateTimeOffset.UtcNow;
            }
            workerRuntime = null;
        }

        if (!_appSettings.Worker.WhisperXEnabled)
        {
            UpdateWorkerObservabilityLabels("Uitgeschakeld", queueDetails, "Worker uitgeschakeld in instellingen", retentionDetails, "Worker uitgeschakeld", "Geen actieve sessie", false);
            return;
        }

        var freshnessWindow = TimeSpan.FromSeconds(Math.Max(15, _appSettings.Worker.PollIntervalSec * 2 + 10));
        var nowUtc = DateTimeOffset.UtcNow;
        var isProcessAlive = _workerProcess is not null && !_workerProcess.HasExited;
        var hasFreshHeartbeat = workerRuntime?.LastSeenUtc is DateTimeOffset lastSeenUtc && nowUtc - lastSeenUtc <= freshnessWindow;

        var workerStatus = "Geen heartbeat";
        var workerActivity = isProcessAlive ? "Workerproces actief, wacht op statusupdate" : "Workerproces niet actief";
        var workerPhase = ResolveWorkerPhase(queue, workerRuntime, out var isProcessing);
        var workerSession = ResolveWorkerSessionName(workerRuntime);

        if (workerRuntime is not null && hasFreshHeartbeat)
        {
            var normalizedState = NormalizeStatus(workerRuntime.State);
            workerStatus = normalizedState switch
            {
                "starting" => "Startend",
                "idle" => "Gereed",
                "scanning" => "Scannen",
                "processing" => "Verwerken",
                "warning" => "Waarschuwing",
                "error" => "Fout",
                "disabled" => "Uitgeschakeld",
                "stopped" => isProcessAlive ? "Startend" : "Gestopt",
                _ => string.IsNullOrWhiteSpace(workerRuntime.State) ? "Gereed" : workerRuntime.State!
            };

            workerActivity = BuildWorkerActivityText(workerRuntime);
        }
        else if (isProcessAlive)
        {
            workerStatus = "Startend";
            workerPhase = queue.PendingCount > 0 ? "Wacht op worker" : "Worker start op";
        }

        UpdateWorkerObservabilityLabels(workerStatus, queueDetails, workerActivity, retentionDetails, workerPhase, workerSession, isProcessing);
        ClearStaleAiErrorStatus(workerSession, workerPhase, isProcessing);
    }

    private string BuildRetentionStatusText(RetentionRuntimeSnapshot? runtime)
    {
        if (!_appSettings.Worker.Retention.Enabled)
        {
            return "Uitgeschakeld";
        }

        if (!_appSettings.Worker.WhisperXEnabled)
        {
            return "Ingeschakeld | worker uitgeschakeld";
        }

        if (runtime is null)
        {
            return "Ingeschakeld | wacht op worker-run";
        }

        var normalizedSummary = runtime.Summary ?? string.Empty;
        var parts = new List<string> { normalizedSummary };
        if (runtime.LastRunAt != default)
        {
            parts.Add($"Laatste run: {runtime.LastRunAt.LocalDateTime:dd-MM HH:mm}");
        }

        return string.Join(" | ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private string BuildWorkerActivityText(WorkerRuntimeSnapshot runtime)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(runtime.Message))
        {
            parts.Add(TranscriptionRuntimeStatus.LooksLikeRuntimeFailure(runtime.Message) ? TranscriptionRuntimeStatus.MissingMessage : runtime.Message!);
        }

        if (!string.IsNullOrWhiteSpace(runtime.ActiveSessionDir))
        {
            parts.Add($"Sessie: {Path.GetFileName(runtime.ActiveSessionDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}");
        }

        if (runtime.LastActivityUtc is DateTimeOffset lastActivity)
        {
            parts.Add($"Activiteit: {lastActivity.LocalDateTime:HH:mm:ss}");
        }
        else if (runtime.LastSeenUtc is DateTimeOffset lastSeen)
        {
            parts.Add($"Heartbeat: {lastSeen.LocalDateTime:HH:mm:ss}");
        }

        return parts.Count == 0 ? "Geen aanvullende workeractiviteit" : string.Join(" | ", parts);
    }

    private string ResolveWorkerPhase(SessionQueueSnapshot queue, WorkerRuntimeSnapshot? runtime, out bool isProcessing)
    {
        isProcessing = false;

        if (!_appSettings.Worker.WhisperXEnabled)
        {
            return "Worker uitgeschakeld";
        }

        var message = NormalizeStatus(runtime?.Message);
        var state = NormalizeStatus(runtime?.State);
        if (state == "processing" || queue.ProcessingCount > 0)
        {
            isProcessing = true;
            if (message.Contains("claim")) return "Sessie claimen";
            if (message.Contains("voorbewer") || message.Contains("preprocess")) return "Voorbewerking";
            if (message.Contains("align")) return "Uitlijnen";
            if (message.Contains("diar")) return "Sprekerherkenning";
            if (message.Contains("export") || message.Contains("resultaat") || message.Contains("verwerk")) return "Exporteren";
            if (message.Contains("transcrib")) return "Transcriberen";
            return "Transcriptie actief";
        }

        if (queue.PendingCount > 0)
        {
            return "Wacht op worker";
        }

        if (queue.FailedCount > 0)
        {
            return "Fout aanwezig";
        }

        if (queue.CompletedCount > 0)
        {
            return "Geen actieve verwerking";
        }

        return "Geen actieve verwerking";
    }

    private static string ResolveWorkerSessionName(WorkerRuntimeSnapshot? runtime)
    {
        if (!string.IsNullOrWhiteSpace(runtime?.ActiveSessionDir))
        {
            return Path.GetFileName(runtime.ActiveSessionDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return "Geen actieve sessie";
    }

    private static string NormalizeStatus(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private void SetStatus(string summary, string details, bool isError = false, UiStatusSource source = UiStatusSource.General, string? meetingId = null)
    {
        _statusSource = source;
        _statusMeetingId = string.IsNullOrWhiteSpace(meetingId) ? null : meetingId.Trim();
        _statusLabel.Text = UiText.T(summary);
        _statusLabel.ForeColor = isError ? StopColor : Color.FromArgb(31, 41, 55);
        _detailsTextBox.Text = UiText.T(details);
        _statusStripLabel.Text = UiText.T(summary);
        _statusStripLabel.ForeColor = isError ? StopColor : Color.FromArgb(31, 41, 55);
        _sessionLabel.Text = _manifest?.MeetingId ?? UiText.T("Nog geen opname gestart.");
        UpdateRecorderOverviewState();
        _trayShowHideItem.Text = UiText.T(Visible ? "Venster verbergen" : "Venster tonen");
    }

    private void ClearStaleAiErrorStatus(string workerSession, string workerPhase, bool isProcessing)
    {
        if (!isProcessing || _statusSource != UiStatusSource.AiPostProcessing || _statusLabel.ForeColor != StopColor)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(workerSession) || string.Equals(_statusMeetingId, workerSession, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SetStatus(
            "Transcriptie actief.",
            $"Sessie: {workerSession}{Environment.NewLine}Workerfase: {workerPhase}{Environment.NewLine}Een eerdere AI-foutmelding blijft vastgelegd in de betreffende sessie-artefacten.",
            source: UiStatusSource.General,
            meetingId: workerSession);
    }

    private void UpdateRecorderOverviewState()
    {
        _overviewLastSessionValueLabel.Text = _manifest?.MeetingId ?? ResolveLatestLibrarySessionName();
        _overviewLibraryCountValueLabel.Text = _libraryListView.Items.Count.ToString();
        _overviewWorkerStatusValueLabel.Text = string.IsNullOrWhiteSpace(_workerStatusLabel.Text) ? UiText.T("Onbekend") : _workerStatusLabel.Text;
        _overviewLastProcessingValueLabel.Text = ResolveRecorderOverviewLastProcessing();
        _overviewAutomationValueLabel.Text = UiText.T("Uitgeschakeld");
        _overviewLastOrganizationValueLabel.Text = ResolveLatestLibraryOrganization();
        _overviewLastProjectValueLabel.Text = ResolveLatestLibraryProject();

        _recorderReadinessStatusValueLabel.Text = string.IsNullOrWhiteSpace(_statusLabel.Text) ? UiText.T("Gereed.") : _statusLabel.Text;
        _recorderReadinessSessionValueLabel.Text = string.IsNullOrWhiteSpace(_sessionLabel.Text) ? UiText.T("Nog geen opname gestart.") : _sessionLabel.Text;
        _recorderReadinessMicValueLabel.Text = string.IsNullOrWhiteSpace(_selectedMicLabel.Text) ? UiText.T("Niet geselecteerd") : _selectedMicLabel.Text;
        _recorderReadinessSystemValueLabel.Text = string.IsNullOrWhiteSpace(_selectedSystemLabel.Text) ? UiText.T("Niet geselecteerd") : _selectedSystemLabel.Text;
        _recorderReadinessProfileValueLabel.Text = string.IsNullOrWhiteSpace(_profileLabel.Text) ? UiText.T("Onbekend") : _profileLabel.Text;
        _recorderReadinessWorkerValueLabel.Text = string.IsNullOrWhiteSpace(_workerStatusLabel.Text) ? UiText.T("Onbekend") : _workerStatusLabel.Text;
        _recorderReadinessStatusValueLabel.ForeColor = _statusLabel.ForeColor;
    }

    private string ResolveLatestLibrarySessionName()
    {
        if (_libraryListView.Items.Count == 0)
        {
            return UiText.T("Nog geen sessie");
        }

        return _libraryListView.Items[0].SubItems.Count > 1
            ? _libraryListView.Items[0].SubItems[1].Text
            : UiText.T("Nog geen sessie");
    }

    private string ResolveLatestLibraryOrganization()
    {
        if (_libraryListView.Items.Count == 0 || _libraryListView.Items[0].SubItems.Count <= 3)
        {
            return "-";
        }

        var value = _libraryListView.Items[0].SubItems[3].Text?.Trim();
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private string ResolveLatestLibraryProject()
    {
        if (_libraryListView.Items.Count == 0 || _libraryListView.Items[0].SubItems.Count <= 4)
        {
            return "-";
        }

        var value = _libraryListView.Items[0].SubItems[4].Text?.Trim();
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private string ResolveRecorderOverviewLastProcessing()
    {
        if (!string.IsNullOrWhiteSpace(_workerPhaseLabel.Text)
            && !string.Equals(_workerPhaseLabel.Text, UiText.T("Nog niet gescand"), StringComparison.OrdinalIgnoreCase))
        {
            return _workerPhaseLabel.Text;
        }

        if (!string.IsNullOrWhiteSpace(_workerActivityLabel.Text)
            && !string.Equals(_workerActivityLabel.Text, UiText.T("Nog niet gescand"), StringComparison.OrdinalIgnoreCase))
        {
            return _workerActivityLabel.Text;
        }

        return UiText.T("Nog geen verwerking");
    }

    private void RestoreSelection(ComboBox comboBox, string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            for (var i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is DeviceInfo info && string.Equals(info.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }
        }

        // Geen (bestaande) voorkeur: het standaardapparaat van Windows, niet het eerste in de lijst (dat was bijvoorbeeld
        // het geluid van een beeldscherm via HDMI). Alleen als Windows geen standaard kent: het eerste.
        for (var i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is DeviceInfo { IsDefault: true })
            {
                comboBox.SelectedIndex = i;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private static void AddSettingsRow(TableLayoutPanel layout, ref int row, string caption, Control control)
    {
        var label = new Label
        {
            Text = caption,
            AutoSize = true,
            ForeColor = Color.FromArgb(73, 82, 96),
            Font = new Font("Segoe UI", 9.25F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 8, 14, 10)
        };

        AddSettingsRow(layout, ref row, label, control);
    }

    private static void AddSettingsRow(TableLayoutPanel layout, ref int row, Control labelControl, Control valueControl)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        labelControl.Margin = new Padding(0, 8, 14, 10);
        valueControl.Margin = new Padding(0, 4, 0, 10);
        valueControl.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        layout.Controls.Add(labelControl, 0, row);
        layout.Controls.Add(valueControl, 1, row);
        row++;
    }

    private static BufferedFlowLayoutPanel CreateInlineCheckboxPanel(params Control[] controls)
    {
        var panel = new BufferedFlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            BackColor = Color.Transparent,
        };

        foreach (var control in controls)
        {
            control.Margin = new Padding(0, 2, 14, 2);
            panel.Controls.Add(control);
        }

        return panel;
    }

    private static BufferedFlowLayoutPanel CreateStatusRow(string caption, Control valueControl)
    {
        valueControl.Margin = new Padding(0, 2, 0, 0);
        valueControl.MaximumSize = new Size(300, 0);
        var panel = new BufferedFlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 12),
            BackColor = Color.Transparent,
        };
        panel.Controls.Add(new Label { Text = caption, AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point), ForeColor = Color.FromArgb(73, 82, 96) });
        panel.Controls.Add(valueControl);
        return panel;
    }

    private static void AddStatusTableRow(TableLayoutPanel layout, int row, string caption, Control valueControl)
    {
        var captionLabel = new Label
        {
            Text = caption,
            AutoSize = true,
            Margin = new Padding(0, 4, 14, 10),
            ForeColor = Color.FromArgb(73, 82, 96),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point)
        };

        valueControl.AutoSize = true;
        valueControl.Margin = new Padding(0, 4, 0, 10);
        valueControl.MaximumSize = new Size(520, 0);
        layout.Controls.Add(captionLabel, 0, row);
        layout.Controls.Add(valueControl, 1, row);
    }

    private static Label CreateMeterCaption(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 2, 8, 0),
            ForeColor = Color.FromArgb(55, 65, 81),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point)
        };

    private static void AddSectionTitle(TableLayoutPanel layout, string title, int row)
    {
        var label = new Label
        {
            Text = title,
            UseMnemonic = false,
            Font = SectionFont,
            AutoSize = true,
            ForeColor = Color.FromArgb(22, 28, 36),
            Margin = new Padding(0, 0, 0, 10),
        };
        layout.Controls.Add(label, 0, row);
        if (layout.ColumnCount > 1)
        {
            layout.SetColumnSpan(label, layout.ColumnCount);
        }
    }

    private void AddFieldRow(TableLayoutPanel layout, int row, string labelText, Control mainControl, Control? actionControl)
    {
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Color.FromArgb(55, 65, 81),
            Margin = new Padding(0, 6, 8, 0)
        };

        mainControl.Margin = new Padding(0, 2, 0, 6);
        if (mainControl is not FlowLayoutPanel && mainControl is not TableLayoutPanel)
        {
            mainControl.Dock = DockStyle.Fill;
        }

        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(mainControl, 1, row);
        if (actionControl is not null)
        {
            actionControl.Anchor = AnchorStyles.Left;
            actionControl.Margin = new Padding(10, 2, 0, 6);
            layout.Controls.Add(actionControl, 2, row);
        }
    }

    private static Label CreateInlineLabel(string text, int leftMargin = 0)
        => new() { Text = text, AutoSize = true, Margin = new Padding(leftMargin, 5, 8, 0), ForeColor = Color.FromArgb(55, 65, 81) };

    private static Panel CreateRecorderSurfaceCard(bool autoSize = false)
        => new RecorderSurfacePanel
        {
            Dock = autoSize ? DockStyle.Top : DockStyle.Fill,
            AutoSize = autoSize,
            AutoSizeMode = autoSize ? AutoSizeMode.GrowAndShrink : AutoSizeMode.GrowOnly,
            Margin = new Padding(0),
            Padding = new Padding(18),
            BackColor = CardBackground
        };

    // De teksten in de taal van de interface: het hele venster en het menu van het systeemvak, ook wat later verandert of
    // wordt toegevoegd (UiTextBinder).
    private void ApplyCurrentLanguageToUi()
    {
        UiTextBinder.Attach(this);
        UiTextBinder.Attach(_trayMenu);
        RefreshLibraryUiLanguageState();
        SetUiToolTip(_settingsButton, "Instellingen en admin");
        _trayShowHideItem.Text = UiText.T(Visible ? "Venster verbergen" : "Venster tonen");
        var isRunning = _session is not null;
        _trayToggleRecordItem.Text = UiText.T(isRunning ? "Opname stoppen" : "Opname starten");
        _trayOpenFolderItem.Text = UiText.T("Opnamemap openen");
        _trayExitItem.Text = UiText.T("Afsluiten");
    }

    private void ConfigureCompactShellLabels()
    {
        foreach (var label in new[]
                 {
                     _overviewLastSessionValueLabel,
                     _overviewWorkerStatusValueLabel,
                     _overviewLastProcessingValueLabel,
                     _overviewAutomationValueLabel,
                     _overviewLastOrganizationValueLabel,
                     _overviewLastProjectValueLabel,
                     _recorderReadinessStatusValueLabel,
                     _recorderReadinessSessionValueLabel,
                     _recorderReadinessMicValueLabel,
                     _recorderReadinessSystemValueLabel,
                     _recorderReadinessProfileValueLabel,
                     _recorderReadinessWorkerValueLabel
                 })
        {
            PrepareCompactShellValueLabel(label);
        }
    }

    private void PrepareCompactShellValueLabel(Label label)
    {
        if (Equals(label.Tag, "compact-shell"))
        {
            return;
        }

        label.AutoSize = false;
        label.AutoEllipsis = true;
        label.Dock = DockStyle.Fill;
        label.MinimumSize = new Size(0, 22);
        label.MaximumSize = Size.Empty;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Tag = "compact-shell";

        void SyncTooltip(object? _, EventArgs __)
        {
            SetUiToolTip(label, string.IsNullOrWhiteSpace(label.Text) ? string.Empty : label.Text);
        }

        label.TextChanged += SyncTooltip;
        SyncTooltip(this, EventArgs.Empty);
    }

    private static Panel CreateCardPanel(bool autoSize = false)
        => new CardPanel
        {
            Dock = autoSize ? DockStyle.Top : DockStyle.Fill,
            AutoSize = autoSize,
            AutoSizeMode = autoSize ? AutoSizeMode.GrowAndShrink : AutoSizeMode.GrowOnly,
            Margin = new Padding(0),
            Padding = new Padding(16),
            BackColor = CardBackground
        };

    private void StyleInputs()
    {
        foreach (var control in new Control[] { _outputRootTextBox, _micComboBox, _systemComboBox, _micGainNumeric, _systemGainNumeric, _detailsTextBox, _librarySearchTextBox })
        {
            control.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        }

        foreach (var checkBox in new[] { _minimizeToTrayCheckbox })
        {
            checkBox.ForeColor = Color.FromArgb(43, 51, 63);
            checkBox.Margin = new Padding(0, 0, 0, 8);
        }

        _qualityLabel.ForeColor = Color.FromArgb(33, 40, 50);
        _mixAdviceLabel.ForeColor = Color.FromArgb(89, 98, 112);
        _hotkeyLabel.ForeColor = Color.FromArgb(89, 98, 112);
        _librarySearchTextBox.BackColor = Color.White;
        _libraryListView.BackColor = Color.White;
        _libraryListView.BorderStyle = BorderStyle.None;
        _libraryListView.Font = new Font("Segoe UI", 9.25F, FontStyle.Regular, GraphicsUnit.Point);
        _detailsTextBox.ForeColor = Color.FromArgb(31, 41, 55);
        _elapsedLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
        _elapsedLabel.ForeColor = AccentColor;
        _sessionLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _profileLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _workerStatusLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _queueStatusLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _workerActivityLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _workerPhaseLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _workerSessionLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _workerProgressBar.ForeColor = AccentColor;
        _recoveryStatusLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _selectedMicLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _selectedSystemLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _recorderReadinessStatusValueLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _recorderReadinessSessionValueLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _recorderReadinessMicValueLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _recorderReadinessSystemValueLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _recorderReadinessProfileValueLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _recorderReadinessWorkerValueLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _micLevelValueLabel.ForeColor = Color.FromArgb(31, 41, 55);
        _systemLevelValueLabel.ForeColor = Color.FromArgb(31, 41, 55);
    }

    private static void StyleButton(Button button, Color backColor, Color hoverColor, Color foreColor)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(191, 201, 214);
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 0, 8, 8);
        button.MouseEnter -= Button_MouseEnter;
        button.MouseLeave -= Button_MouseLeave;
        button.Tag = (backColor, hoverColor);
        button.MouseEnter += Button_MouseEnter;
        button.MouseLeave += Button_MouseLeave;
    }

    private static void Button_MouseEnter(object? sender, EventArgs e)
    {
        if (sender is Button button && button.Tag is ValueTuple<Color, Color> tuple)
        {
            button.BackColor = tuple.Item2;
        }
    }

    private static void Button_MouseLeave(object? sender, EventArgs e)
    {
        if (sender is Button button && button.Tag is ValueTuple<Color, Color> tuple)
        {
            button.BackColor = tuple.Item1;
        }
    }

    private static string ConvertLanguageProfileSelection(string? selection)
    {
        var candidate = (selection ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return LanguageProfileCatalog.Normalize(null);
        }

        var option = LanguageProfileCatalog.GetOptions()
            .FirstOrDefault(x => string.Equals(x.DisplayName, candidate, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(x.Id, candidate, StringComparison.OrdinalIgnoreCase));

        return option?.Id ?? LanguageProfileCatalog.Normalize(candidate);
    }

    private sealed class RecorderSurfacePanel : Panel
    {
        public RecorderSurfacePanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRoundedRegion();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = ClientRectangle;
            if (rect.Width <= 1 || rect.Height <= 1)
            {
                return;
            }

            rect.Inflate(-1, -1);
            using var path = CreateRoundedRectanglePath(rect, 18);
            using var fill = new SolidBrush(CardBackground);
            using var border = new Pen(Color.FromArgb(224, 228, 235));
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        private void ApplyRoundedRegion()
        {
            var rect = ClientRectangle;
            if (rect.Width <= 1 || rect.Height <= 1)
            {
                return;
            }

            rect.Inflate(-1, -1);
            using var path = CreateRoundedRectanglePath(rect, 18);
            Region?.Dispose();
            Region = new Region(path);
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Max(2, radius * 2);
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawRectangle(pen, rect);
        }
    }
}
