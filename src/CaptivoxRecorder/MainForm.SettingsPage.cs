using System.Drawing;
using System.Globalization;
using LocalCaptureAgent.Controls;
using LocalCaptureAgent.Models;
using LocalCaptureAgent.Services;

namespace LocalCaptureAgent;

// De nieuwe pagina Instellingen (fase 3 van de nieuwe interface). Links de onderwerpen, rechts de instellingen die het
// meest gebruikt worden; een wijziging wordt meteen bewaard. Bewust niet via SaveCurrentSettings: die neemt ook de apparaten
// van het opnamescherm mee (ontbreekt de headset bij het starten, dan zou elke klik hier apparaat 0 als voorkeur vastleggen).
// Wat hier niet staat, blijft in het uitgebreide venster (Geavanceerd). De panelen worden een keer opgebouwd en daarna alleen
// bijgewerkt: het uitgebreide-instellingenknopje (_settingsButton) mag nooit worden weggegooid.
public sealed partial class MainForm
{
    private readonly Dictionary<string, Control> _settingsPanels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RadioButton> _settingsCategoryButtons = new(StringComparer.Ordinal);
    private Panel? _settingsContentHost;
    private string _settingsCategory = "algemeen";
    private bool _settingsSyncing;

    private Label? _settingsDevicesLabel;
    private Label? _settingsOutputLabel;
    private Button? _settingsOutputButton;
    private CheckBox? _settingsStartWithWindows;
    private CheckBox? _settingsStartMinimized;
    private CheckBox? _settingsMinimizeToTray;

    private CheckBox? _settingsNotifyReady;
    private CheckBox? _settingsNotifyStopped;
    private CheckBox? _settingsNotifyFailure;
    private Label? _settingsFailureNote;

    private Label? _settingsRetentionLabel;

    private TableLayoutPanel? _settingsAmdSection;
    private CheckBox? _settingsAmdFastRoute;

    private ComboBox? _settingsInterfaceLanguage;
    private ComboBox? _settingsRecordingLanguage;
    private Label? _settingsLanguageRestartNote;
    private Button? _settingsLanguageRestartButton;

    private TabPage BuildSettingsPage()
    {
        var page = ShellNewPage("Instellingen overzicht");
        page.AutoScroll = false;
        _settingsHubTabPage = page;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent, Padding = new Padding(4, 4, 12, 4), Margin = new Padding(0) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(ShellLabel("Instellingen", 18F, FontStyle.Bold, ShellPalette.Text, new Padding(0, 0, 0, 12)), 0, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(240)));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var categories = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 0, 16, 0), AccessibleName = "Onderwerpen" };
        foreach (var (key, text) in new[] { ("algemeen", "Algemeen"), ("opnemen", "Opnemen"), ("meldingen", "Meldingen"), ("opslag", "Opslag en bewaren"), ("geavanceerd", "Geavanceerd") })
        {
            var selected = key == _settingsCategory;
            var option = new RadioButton
            {
                Text = text,
                Appearance = Appearance.Button,
                AutoSize = false,
                Width = LogicalToDeviceUnits(220),
                Height = LogicalToDeviceUnits(42),
                Padding = new Padding(12, 0, 8, 0),
                FlatStyle = FlatStyle.Flat,
                Font = ShellFont("Segoe UI", 10.5F, selected ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point),
                TextAlign = ContentAlignment.MiddleLeft,
                Checked = selected,
                Margin = new Padding(0, 0, 0, 4),
                Cursor = Cursors.Hand,
                ForeColor = selected ? ShellPalette.Accent : ShellPalette.Text,
                BackColor = Color.Transparent,
            };
            option.FlatAppearance.BorderSize = 0;
            option.FlatAppearance.CheckedBackColor = ShellPalette.AccentSoft;
            option.FlatAppearance.MouseOverBackColor = ShellPalette.Hover;
            option.CheckedChanged += (_, _) =>
            {
                option.Font = ShellFont("Segoe UI", 10.5F, option.Checked ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
                option.ForeColor = option.Checked ? ShellPalette.Accent : ShellPalette.Text;
                if (option.Checked) ShowSettingsCategory(key);
            };
            categories.Controls.Add(option);
            _settingsCategoryButtons[key] = option;
        }

        body.Controls.Add(categories, 0, 0);
        var card = new ShellCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        _settingsContentHost = new Panel { Dock = DockStyle.Fill, BackColor = ShellPalette.Surface, AutoScroll = true };
        card.Controls.Add(_settingsContentHost);
        body.Controls.Add(card, 1, 0);
        root.Controls.Add(body, 0, 1);
        root.Controls.Add(ShellLabel("Vinkjes en keuzes worden meteen bewaard.", 9F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(0, 4, 0, 4)), 0, 2);

        AddSettingsPanel("algemeen", BuildSettingsGeneralPanel());
        AddSettingsPanel("opnemen", BuildSettingsRecordingPanel());
        AddSettingsPanel("meldingen", BuildSettingsNotificationsPanel());
        AddSettingsPanel("opslag", BuildSettingsStoragePanel());
        AddSettingsPanel("geavanceerd", BuildSettingsAdvancedPanel());
        page.Controls.Add(root);
        ShowSettingsCategory(_settingsCategory);
        return page;
    }

    private void AddSettingsPanel(string key, Control panel)
    {
        panel.Dock = DockStyle.Top;
        panel.Visible = false;
        _settingsPanels[key] = panel;
        _settingsContentHost!.Controls.Add(panel);
    }

    private void ShowSettingsCategory(string key)
    {
        _settingsCategory = key;
        // Ook als een onderwerp vanuit de code wordt gekozen: de knop links mee.
        if (_settingsCategoryButtons.TryGetValue(key, out var button) && !button.Checked) button.Checked = true;
        foreach (var (name, panel) in _settingsPanels) panel.Visible = name == key;
        RefreshSettingsPage();
    }

    private void OnSettingsPageShown() => RefreshSettingsPage();

    // ---------- Bouwstenen ----------

    private static TableLayoutPanel SettingsPanel(string title, string intro)
    {
        var stack = ShellStack();
        ShellStackAdd(stack, ShellLabel(title, 14F, FontStyle.Bold, ShellPalette.Text, new Padding(0, 0, 0, 4)));
        if (intro.Length > 0)
        {
            var label = ShellLabel(intro, 9.75F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(0, 0, 0, 14));
            label.MaximumSize = new Size(640, 0);
            ShellStackAdd(stack, label);
        }

        return stack;
    }

    private CheckBox SettingsCheck(TableLayoutPanel stack, string text, string detail, Func<bool> read, Action<bool> write)
    {
        var check = new CheckBox { Text = UiText.T(text), AutoSize = true, Font = ShellFont("Segoe UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point), Margin = new Padding(0, 6, 0, detail.Length > 0 ? 0 : 6), BackColor = Color.Transparent };
        check.CheckedChanged += (_, _) =>
        {
            if (_settingsSyncing) return;
            var before = read();
            try
            {
                write(check.Checked);
                SaveSettingsFromSettingsPage(outputChanged: false);
            }
            catch (Exception ex)
            {
                write(before);
                AppRuntime.LogException("Instelling bewaren mislukt", ex);
                ShowOwnedMessage(this, "Bewaren lukte niet; de oude instelling blijft staan. " + ex.Message, "Instellingen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RefreshSettingsPage();
            }
        };
        ShellStackAdd(stack, check);
        if (detail.Length > 0)
        {
            var label = ShellLabel(detail, 9F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(LogicalToDeviceUnits(20), 0, 0, 8));
            label.MaximumSize = new Size(620, 0);
            ShellStackAdd(stack, label);
        }

        return check;
    }

    // Alleen wat deze pagina wijzigt: geen apparaten en geen versterking (die blijven zoals ze bewaard zijn).
    private void SaveSettingsFromSettingsPage(bool outputChanged)
    {
        _appSettings.Ui.MinimizeToTray = _minimizeToTrayCheckbox.Checked;
        if (outputChanged)
        {
            _appSettings.Ui.OutputRoot = _outputRootTextBox.Text.Trim();
            _appSettings.Worker.KnownSessionRoots ??= new List<string>();
            if (!string.IsNullOrWhiteSpace(_appSettings.Ui.OutputRoot)) _appSettings.Worker.KnownSessionRoots.Add(_appSettings.Ui.OutputRoot);
            SettingsStore.NormalizeKnownSessionRoots(_appSettings);
        }

        _editionService.ApplyEditionPolicy(_appSettings, _editionState);
        SettingsStore.Save(_appSettings);
        EnsureWindowsStartupRegistration();
        if (outputChanged) EnsureWorkerProcessState();
    }

    private static Control SettingsDivider()
        => new Panel { Height = 1, Dock = DockStyle.Top, BackColor = ShellPalette.LineSoft, Margin = new Padding(0, 14, 0, 14) };

    // ---------- Opnemen ----------

    private Control BuildSettingsRecordingPanel()
    {
        var stack = SettingsPanel("Opnemen", "Welke apparaten Captivox opneemt en waar de opnames komen.");
        MoveDeviceControlsIntoSettings(stack);
        _settingsDevicesLabel = ShellLabel(string.Empty, 9.5F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(0, 4, 0, 6));
        _settingsDevicesLabel.MaximumSize = new Size(640, 0);
        ShellStackAdd(stack, _settingsDevicesLabel);
        ShellStackAdd(stack, SettingsDivider());
        _settingsOutputLabel = ShellLabel(string.Empty, 10F, FontStyle.Regular, ShellPalette.Text, new Padding(0, 0, 0, 6));
        _settingsOutputLabel.MaximumSize = new Size(640, 0);
        ShellStackAdd(stack, _settingsOutputLabel);
        _settingsOutputButton = ShellButton("Andere map kiezen", false, (_, _) =>
        {
            var before = _outputRootTextBox.Text;
            BrowseButton_Click(null, EventArgs.Empty);
            if (!string.Equals(before, _outputRootTextBox.Text, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    SaveSettingsFromSettingsPage(outputChanged: true);
                }
                catch (Exception ex)
                {
                    _outputRootTextBox.Text = before;
                    _appSettings.Ui.OutputRoot = before;
                    AppRuntime.LogException("Opslagmap bewaren mislukt", ex);
                    ShowOwnedMessage(this, "Bewaren lukte niet; de oude map blijft. " + ex.Message, "Instellingen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            RefreshSettingsPage();
        });
        ShellStackAdd(stack, SettingsActionRow(_settingsOutputButton));
        // Alleen met een AMD-videokaart: de snelle maar minder nauwkeurige route. Standaard uit (zie WorkerSettings.AmdGpuFastRoute).
        _settingsAmdSection = ShellStack();
        ShellStackAdd(_settingsAmdSection, SettingsDivider());
        ShellStackAdd(_settingsAmdSection, ShellSectionTitle("Uitschrijven"));
        _settingsAmdFastRoute = SettingsCheck(_settingsAmdSection, "Snel via de videokaart (AMD), iets minder nauwkeurig",
            "Standaard schrijft Captivox uit op de processor: nauwkeurig, op lage prioriteit zodat je pc bruikbaar blijft. Met deze keuze gebruikt Captivox je AMD-videokaart: veel sneller, maar met meer fouten en soms verzonnen tekst in stiltes.",
            () => _appSettings.Worker.AmdGpuFastRoute,
            value =>
            {
                _appSettings.Worker.AmdGpuFastRoute = value;
                // Uit: weer automatisch (NVIDIA via CUDA, anders de processor).
                _appSettings.Worker.TranscriptionProfile = value ? "amd-gpu" : "auto";
            });
        ShellStackAdd(stack, _settingsAmdSection);
        return stack;
    }

    // ---------- Algemeen ----------

    // De taal van de interface (geldt na opnieuw starten), de taal van de opnames en hoe Captivox start.
    private Control BuildSettingsGeneralPanel()
    {
        var stack = SettingsPanel("Algemeen", "De taal van Captivox en van je opnames, en hoe Captivox start.");
        Label Caption(string text) => ShellLabel(text, 10F, FontStyle.Bold, ShellPalette.Text, new Padding(0, 6, 0, 4));
        ComboBox LanguageCombo(IEnumerable<(string Id, string Name)> options, string selectedId)
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = ShellFont("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
                Width = LogicalToDeviceUnits(260),
                Margin = new Padding(0, 2, 8, 0),
            };
            foreach (var (id, name) in options) combo.Items.Add(new SettingsLanguageOption(id, name));
            combo.SelectedItem = combo.Items.Cast<SettingsLanguageOption>().FirstOrDefault(o => o.Id == selectedId);
            // Het muiswiel kiest geen taal terwijl de gebruiker alleen door de pagina scrolt.
            combo.MouseWheel += (_, e) =>
            {
                if (!combo.DroppedDown && e is HandledMouseEventArgs handled) handled.Handled = true;
            };
            return combo;
        }

        ShellStackAdd(stack, Caption("Taal van Captivox"));
        // Elke taal in haar eigen naam, zodat iedereen zijn taal terugvindt.
        _settingsInterfaceLanguage = LanguageCombo(UiText.SupportedLanguages.Select(id => (id, UiText.DisplayName(id))), _appSettings.Ui.InterfaceLanguage);
        _settingsInterfaceLanguage.AccessibleName = UiText.T("Taal van Captivox");
        _settingsInterfaceLanguage.SelectionChangeCommitted += (_, _) =>
        {
            if (_settingsSyncing || _settingsInterfaceLanguage.SelectedItem is not SettingsLanguageOption option) return;
            var before = _appSettings.Ui.InterfaceLanguage;
            try
            {
                _appSettings.Ui.InterfaceLanguage = option.Id;
                SaveSettingsFromSettingsPage(outputChanged: false);
            }
            catch (Exception ex)
            {
                _appSettings.Ui.InterfaceLanguage = before;
                AppRuntime.LogException("Taal van de interface bewaren mislukt", ex);
                ShowOwnedMessage(this, "Bewaren lukte niet; de oude instelling blijft staan. " + ex.Message, "Instellingen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            RefreshSettingsPage();
        };
        ShellStackAdd(stack, SettingsActionRow(_settingsInterfaceLanguage));
        _settingsLanguageRestartNote = ShellLabel("De nieuwe taal geldt nadat Captivox opnieuw is gestart.", 9.5F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(0, 6, 0, 6));
        _settingsLanguageRestartNote.MaximumSize = new Size(640, 0);
        ShellStackAdd(stack, _settingsLanguageRestartNote);
        _settingsLanguageRestartButton = ShellButton("Nu opnieuw starten", false, (_, _) => RestartForInterfaceLanguage());
        ShellStackAdd(stack, SettingsActionRow(_settingsLanguageRestartButton));

        ShellStackAdd(stack, Caption("Taal van de opnames"));
        _settingsRecordingLanguage = LanguageCombo(LanguageProfileCatalog.GetOptions().Select(o => (o.Id, o.DisplayName)), _appSettings.Ui.LanguageProfile);
        _settingsRecordingLanguage.AccessibleName = UiText.T("Taal van de opnames");
        _settingsRecordingLanguage.SelectionChangeCommitted += (_, _) =>
        {
            if (_settingsSyncing || _settingsRecordingLanguage.SelectedItem is not SettingsLanguageOption option) return;
            var before = _appSettings.Ui.LanguageProfile;
            try
            {
                _appSettings.Ui.LanguageProfile = option.Id;
                // SettingsStore.Save neemt de transcriptietaal van de worker hieruit over.
                SaveSettingsFromSettingsPage(outputChanged: false);
            }
            catch (Exception ex)
            {
                _appSettings.Ui.LanguageProfile = before;
                AppRuntime.LogException("Taal van de opnames bewaren mislukt", ex);
                ShowOwnedMessage(this, "Bewaren lukte niet; de oude instelling blijft staan. " + ex.Message, "Instellingen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            RefreshSettingsPage();
        };
        ShellStackAdd(stack, SettingsActionRow(_settingsRecordingLanguage));
        var recordingNote = ShellLabel("De taal die in je overleggen gesproken wordt. Captivox schrijft nieuwe opnames in deze taal uit.", 9.5F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(0, 6, 0, 0));
        recordingNote.MaximumSize = new Size(640, 0);
        ShellStackAdd(stack, recordingNote);

        ShellStackAdd(stack, SettingsDivider());
        _settingsStartWithWindows = SettingsCheck(stack, "Starten met Windows", string.Empty, () => _appSettings.Ui.StartWithWindows, value => _appSettings.Ui.StartWithWindows = value);
        _settingsStartMinimized = SettingsCheck(stack, "Geminimaliseerd starten", "Geldt vanaf de volgende keer dat Captivox start.", () => _appSettings.Ui.StartMinimized, value => _appSettings.Ui.StartMinimized = value);
        // Werkt bij minimaliseren; het kruisje sluit Captivox echt af (en daarmee de opnamebewaking en de meldingen).
        _settingsMinimizeToTray = SettingsCheck(stack, "Bij minimaliseren naar het systeemvak", "Captivox verdwijnt dan uit de taakbalk en blijft rechtsonder bereikbaar. Het kruisje sluit Captivox af.",
            () => _minimizeToTrayCheckbox.Checked, value => _minimizeToTrayCheckbox.Checked = value);
        return stack;
    }

    // Een taal in een keuzelijst: de naam zoals hij getoond wordt, de code als waarde.
    private sealed record SettingsLanguageOption(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    // Opnieuw starten voor een andere taal: een nieuw exemplaar wacht tot dit exemplaar is afgesloten (Program.WaitForPidArgument).
    // Niet tijdens een opname of het afronden ervan.
    private void RestartForInterfaceLanguage()
    {
        if (_session is not null || _recordingBusy || _stopFinalizeInProgress)
        {
            ShowOwnedMessage(this, "Er loopt een opname. Start Captivox opnieuw nadat je de opname hebt gestopt.", "Opnieuw starten", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var executable = Environment.ProcessPath ?? Application.ExecutablePath;
            var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false };
            start.ArgumentList.Add(Program.WaitForPidArgument);
            start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            System.Diagnostics.Process.Start(start)?.Dispose();
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Opnieuw starten voor een andere taal mislukt", ex);
            ShowOwnedMessage(this, "Opnieuw starten lukte niet. Sluit Captivox en start het opnieuw. " + ex.Message, "Opnieuw starten", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Close();
    }

    // De apparaatkeuze verhuist van het oude opnamescherm naar hier: dezelfde controls (met hun bestaande gedrag:
    // opnieuw zoeken, voorkeur onthouden, uit tijdens een opname). Op het oude scherm komt een verwijzing hierheen. Een keuze
    // van de gebruiker wordt kort daarna bewaard, en alleen wat hij wijzigde (niet de terugvalkeuze van de andere lijst).
    private readonly System.Windows.Forms.Timer _deviceSaveTimer = new() { Interval = 700 };
    private readonly HashSet<string> _devicePendingSave = new(StringComparer.Ordinal);

    private void MoveDeviceControlsIntoSettings(TableLayoutPanel stack)
    {
        var oldLayout = _micComboBox.Parent as TableLayoutPanel;
        var gainPanel = _micGainNumeric.Parent;
        var micCell = oldLayout?.GetPositionFromControl(_micComboBox);
        var systemRow = oldLayout?.GetPositionFromControl(_systemComboBox).Row ?? -1;
        var gainRow = gainPanel is not null && oldLayout is not null ? oldLayout.GetPositionFromControl(gainPanel).Row : -1;

        Label Caption(string text) => ShellLabel(text, 10F, FontStyle.Bold, ShellPalette.Text, new Padding(0, 6, 0, 4));
        ShellStackAdd(stack, Caption("Microfoon"));
        var micRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 4) };
        micRow.Controls.Add(_micComboBox);
        micRow.Controls.Add(_refreshDevicesButton);
        ShellStackAdd(stack, micRow);
        ShellStackAdd(stack, Caption("Geluid van de anderen (waar Teams op afspeelt)"));
        var systemHolder = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 4) };
        systemHolder.Controls.Add(_systemComboBox);
        ShellStackAdd(stack, systemHolder);
        if (gainPanel is not null)
        {
            ShellStackAdd(stack, Caption("Versterking"));
            var gainHolder = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
            gainPanel.Dock = DockStyle.None;
            gainHolder.Controls.Add(gainPanel);
            ShellStackAdd(stack, gainHolder);
        }

        var advice = ShellLabel("Kies bij Geluid van de anderen het apparaat waar Teams op afspeelt (bij een headset: die headset). Tijdens een opname kun je dit niet wijzigen.", 9.5F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(0, 8, 0, 0));
        advice.MaximumSize = new Size(640, 0);
        ShellStackAdd(stack, advice);

        // Het muiswiel kiest geen apparaat of versterking terwijl de gebruiker alleen door de pagina scrolt: alleen in een geopende lijst
        // of een vak dat hij zelf heeft geselecteerd.
        foreach (var control in new Control[] { _micComboBox, _systemComboBox, _micGainNumeric, _systemGainNumeric })
        {
            control.MouseWheel += (_, e) =>
            {
                var allowed = control is ComboBox combo ? combo.DroppedDown : control.ContainsFocus;
                if (!allowed && e is HandledMouseEventArgs handled) handled.Handled = true;
            };
        }

        // De vertaalslag zette de versterking terug naar de tekst van het opstarten.

        _micComboBox.SelectionChangeCommitted += (_, _) => QueueDeviceSave("mic");
        _systemComboBox.SelectionChangeCommitted += (_, _) => QueueDeviceSave("system");
        _micGainNumeric.ValueChanged += (_, _) => { if (_micGainNumeric.ContainsFocus) QueueDeviceSave("gain"); };
        _systemGainNumeric.ValueChanged += (_, _) => { if (_systemGainNumeric.ContainsFocus) QueueDeviceSave("gain"); };
        // Getypt en dan weg uit het vak: ValueChanged komt dan zonder focus; bij verlaten alsnog bewaren als het afwijkt.
        _micGainNumeric.Leave += (_, _) => { if ((double)_micGainNumeric.Value != _appSettings.Ui.MicGain) QueueDeviceSave("gain"); };
        _systemGainNumeric.Leave += (_, _) => { if ((double)_systemGainNumeric.Value != _appSettings.Ui.SystemGain) QueueDeviceSave("gain"); };
        _deviceSaveTimer.Tick += (_, _) =>
        {
            _deviceSaveTimer.Stop();
            SaveDeviceChoice();
        };

        // Op het oude scherm: een verwijzing op de plek van de microfoon, en de losse labels van de verhuisde rijen weg.
        if (oldLayout is not null && micCell is { } cell)
        {
            var pointer = new LinkLabel { Text = "Apparaten kiezen staat nu onder Instellingen, Opnemen", AutoSize = true, LinkColor = ShellPalette.Accent, ActiveLinkColor = ShellPalette.Accent, Margin = new Padding(0, 8, 0, 8) };
            pointer.LinkClicked += (_, _) => NavigateToSettings("opnemen");
            oldLayout.Controls.Add(pointer, cell.Column, cell.Row);
            foreach (var row in new[] { systemRow, gainRow })
            {
                if (row >= 0 && oldLayout.GetControlFromPosition(0, row) is { } label) label.Visible = false;
            }
        }
    }

    // Na de oude opmaak in de constructor (StyleInputs/StyleButton draaien na het opbouwen van de pagina's).
    private void StyleMovedDeviceControls()
    {
        foreach (var combo in new[] { _micComboBox, _systemComboBox })
        {
            combo.Dock = DockStyle.None;
            combo.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            combo.Width = LogicalToDeviceUnits(420);
            combo.Font = ShellFont("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            combo.Margin = new Padding(0, 2, 8, 0);
        }

        _refreshDevicesButton.Dock = DockStyle.None;
        _refreshDevicesButton.Margin = new Padding(0);
        _refreshDevicesButton.Text = "Opnieuw zoeken";
        _refreshDevicesButton.FlatStyle = FlatStyle.Flat;
        _refreshDevicesButton.BackColor = ShellPalette.Surface;
        _refreshDevicesButton.ForeColor = ShellPalette.Text;
        _refreshDevicesButton.FlatAppearance.BorderColor = Color.FromArgb(185, 194, 204);
        _refreshDevicesButton.FlatAppearance.BorderSize = 1;
        _refreshDevicesButton.FlatAppearance.MouseOverBackColor = ShellPalette.Hover;
        _refreshDevicesButton.Font = ShellFont("Segoe UI", 9.75F, FontStyle.Bold, GraphicsUnit.Point);
        _refreshDevicesButton.AutoSize = true;
        _refreshDevicesButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _refreshDevicesButton.MinimumSize = new Size(0, LogicalToDeviceUnits(32));
        _refreshDevicesButton.Padding = new Padding(10, 0, 10, 0);
    }

    private void QueueDeviceSave(string what)
    {
        _devicePendingSave.Add(what);
        // Kort wachten: pijltjes in de lijst of ingedrukt houden in de versterking samen een keer bewaren.
        _deviceSaveTimer.Stop();
        _deviceSaveTimer.Start();
    }

    // Alleen wat de gebruiker wijzigde: de microfoon, het geluid van de anderen of de versterking. Niet via SaveCurrentSettings, die ook
    // de terugvalkeuze van de andere lijst als voorkeur vastlegt.
    private void SaveDeviceChoice()
    {
        if (_devicePendingSave.Count == 0) return;
        var pending = _devicePendingSave.ToList();
        _devicePendingSave.Clear();
        try
        {
            if (pending.Contains("mic") && _micComboBox.SelectedItem is DeviceInfo mic)
            {
                _appSettings.Ui.PreferredMicDeviceId = mic.Id;
                _preferredMicDeviceId = mic.Id;
            }

            if (pending.Contains("system") && _systemComboBox.SelectedItem is DeviceInfo system)
            {
                _appSettings.Ui.PreferredSystemDeviceId = system.Id;
                _preferredSystemDeviceId = system.Id;
            }

            if (pending.Contains("gain"))
            {
                _appSettings.Ui.MicGain = (double)_micGainNumeric.Value;
                _appSettings.Ui.SystemGain = (double)_systemGainNumeric.Value;
            }

            SaveSettingsFromSettingsPage(outputChanged: false);
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Apparaatkeuze bewaren mislukt", ex);
            ShowOwnedMessage(this, "Bewaren lukte niet: " + ex.Message, "Instellingen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        RefreshShellStatus();
        RefreshSettingsDeviceNote();
    }

    private void RefreshSettingsDeviceNote()
    {
        if (_settingsDevicesLabel is null) return;
        _settingsDevicesLabel.Text = _audioDeviceLoading ? "Apparaten worden geladen..."
            : _session is not null ? "Er loopt een opname: de apparaten staan vast tot je stopt."
            : !CanStartRecordingNow() ? "Kies hierboven een microfoon en het geluid van de anderen; dan kun je opnemen."
            : string.Empty;
        _settingsDevicesLabel.Visible = _settingsDevicesLabel.Text.Length > 0;
    }

    // Naar een onderwerp onder Instellingen (bijvoorbeeld vanaf de status bovenaan als er nog geen apparaten gekozen zijn).
    private void NavigateToSettings(string category)
    {
        NavigateShell(ShellNavSettings);
        ShowSettingsCategory(category);
    }

    private static Control SettingsActionRow(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 4) };
        foreach (var control in controls)
        {
            control.Margin = new Padding(0, 0, 8, 0);
            row.Controls.Add(control);
        }

        return row;
    }

    // ---------- Meldingen ----------

    private Control BuildSettingsNotificationsPanel()
    {
        var stack = SettingsPanel("Meldingen", string.Empty);
        ShellStackAdd(stack, ShellSectionTitle("Op de pc"));
        ShellStackAdd(stack, ShellLabel("Een Windows-melding rechtsonder in beeld.", 9.5F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(0, 0, 0, 6)));
        var notifications = () => _appSettings.Ui.Notifications;
        _settingsNotifyReady = SettingsCheck(stack, "Overleg uitgeschreven", "Een klik opent de uitgeschreven tekst.", () => notifications().TranscriptReady.WindowsEnabled, value => notifications().TranscriptReady.WindowsEnabled = value);
        _settingsNotifyStopped = SettingsCheck(stack, "Opname gestopt, wacht op uitschrijven", string.Empty, () => notifications().RecordingStoppedWaitingForTranscription.WindowsEnabled, value => notifications().RecordingStoppedWaitingForTranscription.WindowsEnabled = value);
        _settingsNotifyFailure = SettingsCheck(stack, "Probleem met een opname", string.Empty, () => notifications().RecordingFailure.WindowsEnabled, value => notifications().RecordingFailure.WindowsEnabled = value);
        _settingsFailureNote = ShellLabel(string.Empty, 9F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(LogicalToDeviceUnits(20), 0, 0, 8));
        ShellStackAdd(stack, _settingsFailureNote);
        return stack;
    }

    // ---------- Opslag en bewaren ----------

    private Control BuildSettingsStoragePanel()
    {
        var stack = SettingsPanel("Opslag en bewaren", "Hoe lang Captivox audio, transcripten en sessiegegevens bewaart. Pas dit bewust aan in het uitgebreide venster: opruimen verwijdert opnames.");
        _settingsRetentionLabel = ShellLabel(string.Empty, 10F, FontStyle.Regular, ShellPalette.Text, new Padding(0, 0, 0, 10));
        _settingsRetentionLabel.MaximumSize = new Size(640, 0);
        ShellStackAdd(stack, _settingsRetentionLabel);
        ShellStackAdd(stack, SettingsActionRow(ShellButton("Bewaartermijnen aanpassen", false, (_, _) => OpenFullSettingsDialog())));
        return stack;
    }

    // ---------- Geavanceerd ----------

    private Control BuildSettingsAdvancedPanel()
    {
        var stack = SettingsPanel("Geavanceerd", "Alles wat niet dagelijks nodig is, met de schermen van vroeger voor de functies die (nog) niet op de nieuwe pagina's staan.");
        Control Row(string title, string detail, Control action)
        {
            var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 12) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var text = ShellStack();
            text.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            ShellStackAdd(text, ShellLineLabel(title, 10.5F, FontStyle.Bold, ShellPalette.Text, 26));
            ShellStackAdd(text, ShellLineLabel(detail, 9.5F, FontStyle.Regular, ShellPalette.TextSecondary, 22));
            row.Controls.Add(text, 0, 0);
            action.Anchor = AnchorStyles.Right;
            row.Controls.Add(action, 1, 0);
            return row;
        }

        ShellStackAdd(stack, Row("Alle instellingen", "Het uitgebreide venster: transcriptie, bewaren, diagnose", _settingsButton));
        ShellStackAdd(stack, Row("Uitgebreide bibliotheek", "Alle functies van de oude bibliotheek: onderwerpen, importeren, exporteren", ShellButton("Openen", false, (_, _) => NavigateShell(ShellNavLibrary))));
        ShellStackAdd(stack, Row("Snelstart en opnameoverzicht", "Het overzicht en de gereedheid van de recorder", ShellButton("Openen", false, (_, _) => NavigateToMainWorkflow("Opnemen", null))));
        // Na het sluiten van het uitgebreide venster deze pagina bijwerken (die handler loopt na de bestaande, die het venster toont).
        _settingsButton.Click += (_, _) => RefreshSettingsPage();
        ShellStackAdd(stack, Row("Systeem en diagnose", "Werker, wachtrij, herstel en de gezondheid van de opname", ShellButton("Openen", false, (_, _) => NavigateShell(ShellNavSystem))));
        return stack;
    }

    private void OpenFullSettingsDialog()
    {
        ShowSettingsDialog();
        RefreshSettingsPage();
    }

    // ---------- Bijwerken (zonder dat de vinkjes iets opslaan) ----------

    private void RefreshSettingsPage()
    {
        if (_settingsContentHost is null) return;
        _settingsSyncing = true;
        try
        {
            var ui = _appSettings.Ui;
            RefreshSettingsDeviceNote();

            if (_settingsOutputLabel is not null) _settingsOutputLabel.Text = UiText.F("Opnames komen in: {0}", FirstNonEmpty(_outputRootTextBox.Text, UiText.T("(niet ingesteld)")));
            if (_settingsOutputButton is not null) _settingsOutputButton.Enabled = _session is null;
            if (_settingsInterfaceLanguage is not null)
            {
                _settingsInterfaceLanguage.SelectedItem = _settingsInterfaceLanguage.Items.Cast<SettingsLanguageOption>().FirstOrDefault(o => o.Id == ui.InterfaceLanguage);
                var pending = !string.Equals(ui.InterfaceLanguage, UiText.Language, StringComparison.Ordinal);
                if (_settingsLanguageRestartNote is not null) _settingsLanguageRestartNote.Visible = pending;
                if (_settingsLanguageRestartButton is not null)
                {
                    _settingsLanguageRestartButton.Visible = pending;
                    _settingsLanguageRestartButton.Enabled = _session is null && !_recordingBusy && !_stopFinalizeInProgress;
                }
            }

            if (_settingsRecordingLanguage is not null)
            {
                _settingsRecordingLanguage.SelectedItem = _settingsRecordingLanguage.Items.Cast<SettingsLanguageOption>().FirstOrDefault(o => o.Id == ui.LanguageProfile);
                // Tijdens een opname staat de taal van die opname al vast.
                _settingsRecordingLanguage.Enabled = _session is null;
            }

            if (_settingsStartWithWindows is not null) _settingsStartWithWindows.Checked = ui.StartWithWindows;
            if (_settingsStartMinimized is not null) _settingsStartMinimized.Checked = ui.StartMinimized;
            if (_settingsMinimizeToTray is not null) _settingsMinimizeToTray.Checked = _minimizeToTrayCheckbox.Checked;
            if (_settingsAmdFastRoute is not null) _settingsAmdFastRoute.Checked = _appSettings.Worker.AmdGpuFastRoute;
            if (_settingsAmdSection is not null)
            {
                _settingsAmdSection.Visible = _appSettings.Worker.AmdGpuFastRoute
                    || string.Equals(_appSettings.Hardware?.LastGpuVendor, "amd", StringComparison.OrdinalIgnoreCase);
            }

            var n = ui.Notifications;
            if (_settingsNotifyReady is not null) _settingsNotifyReady.Checked = n.TranscriptReady.WindowsEnabled;
            if (_settingsNotifyStopped is not null) _settingsNotifyStopped.Checked = n.RecordingStoppedWaitingForTranscription.WindowsEnabled;
            if (_settingsNotifyFailure is not null)
            {
                _settingsNotifyFailure.Checked = n.RecordingFailure.WindowsEnabled;
                // Een mislukte opname wil je meteen weten: hier alleen aan te zetten, niet uit.
                _settingsNotifyFailure.Enabled = !n.RecordingFailure.WindowsEnabled;
            }

            if (_settingsFailureNote is not null) _settingsFailureNote.Text = n.RecordingFailure.WindowsEnabled ? "Staat aan en kan hier niet uit: een opname die mislukt, wil je meteen weten." : "Staat uit. Zet dit aan: een mislukte opname wil je meteen weten.";


            if (_settingsRetentionLabel is not null)
            {
                var r = _appSettings.Worker.Retention;
                _settingsRetentionLabel.Text = r.Enabled
                    ? UiText.T("Opruimen staat aan.") + Environment.NewLine + UiText.F("Audio: {0} dagen. Transcripten: {1} dagen. Sessiegegevens: {2} dagen.", r.AudioRetentionDays, r.TranscriptRetentionDays, r.SessionRecordRetentionDays) + Environment.NewLine + RecordingFreeSpaceText()
                    : UiText.T("Opruimen staat uit: Captivox bewaart alles.") + Environment.NewLine + RecordingFreeSpaceText();
            }
        }
        finally
        {
            _settingsSyncing = false;
        }
    }
}
