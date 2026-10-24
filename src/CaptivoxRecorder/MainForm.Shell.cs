using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using LocalCaptureAgent.Controls;
using LocalCaptureAgent.Models;
using LocalCaptureAgent.Services;

namespace LocalCaptureAgent;

// De nieuwe schil. Bovenaan altijd de opnameknop en de stand van Captivox; links de plekken (Vandaag, Overleggen,
// Meldingen, Instellingen) in plaats van tabbladen; het trayicoon laat zien wat er speelt. De bestaande schermen
// blijven tabbladen in een paginahouder zonder koppen, zodat alle bestaande navigatie blijft werken.
public sealed partial class MainForm
{
    private const string ShellNavToday = "vandaag";
    // De teksten voor de apparaatregel, van volledig naar kort (leeg bij een andere stand), en de volledige tekst.
    private string[] _shellStatusDetailVariants = Array.Empty<string>();
    private string _shellStatusDetailFull = string.Empty;

    // "Microfoon (USB-speakerphone 370)" en "Luidsprekers (USB-speakerphone 370)" passen op een gewoon venster niet naast elkaar: eerst de
    // volledige namen, dan alleen het apparaat (wat tussen haakjes staat), dan zo kort als kan.
    internal static string[] DeviceLineVariants(string microphone, string others)
    {
        // Alleen bij de gewone vorm "Soort (Apparaat)" (de naam eindigt op het haakje dat bij het apparaat hoort) en alleen als de
        // soort de gewone is: bij iets anders, zoals "Stereo-mix (...)" als microfoon, moet je dat blijven zien.
        static string Device(string name, string[] plainKinds)
        {
            var trimmed = name.Trim();
            if (!trimmed.EndsWith(')')) return name;
            var depth = 0;
            var open = -1;
            for (var i = trimmed.Length - 1; i >= 0; i--)
            {
                if (trimmed[i] == ')') depth++;
                else if (trimmed[i] == '(' && --depth == 0)
                {
                    open = i;
                    break;
                }
            }

            if (open <= 0) return name;
            var kind = trimmed[..open].Trim();
            var inner = trimmed[(open + 1)..^1].Trim();
            return inner.Length > 0 && plainKinds.Any(k => kind.StartsWith(k, StringComparison.OrdinalIgnoreCase)) ? inner : name;
        }

        var mic = Device(microphone, new[] { "Microfoon", "Microphone", "Headset" });
        var sys = Device(others, new[] { "Luidspreker", "Speaker", "Hoofdtelefoon", "Koptelefoon", "Headphone", "Headset" });
        // Meteen in de taal van de interface: de breedte van de vertaalde tekst bepaalt welke variant past.
        return new[]
        {
            UiText.F("Microfoon: {0}  |  Geluid van anderen: {1}", microphone, others),
            UiText.F("Microfoon: {0}  |  Geluid van anderen: {1}", mic, sys),
            string.Equals(mic, sys, StringComparison.OrdinalIgnoreCase) ? UiText.F("Microfoon en geluid: {0}", mic) : UiText.F("Microfoon: {0}  |  Anderen: {1}", mic, sys),
        }.Distinct(StringComparer.Ordinal).ToArray();
    }

    // Puur: de eerste tekst die past; past er geen, dan de laatste (de kortste); zonder varianten de volledige tekst.
    internal static string ChooseFittingText(IReadOnlyList<string> variants, string full, Func<string, int> width, int available)
    {
        var candidates = variants.Count > 0 ? variants : new[] { full };
        return candidates.FirstOrDefault(c => width(c) <= available) ?? candidates[^1];
    }

    // De regel met microfoon en geluid liep onder de bel door ("Geluid van anderen: Luids"); nu krijgt hij de ruimte die
    // er is en eindigt hij op drie puntjes; de hele tekst staat erbij als je er met de muis op staat. Op een gewoon
    // venster paste hij dan nog niet; nu de langste variant die past.
    private bool FitShellStatusDetail()
    {
        if (_shellStatusDetail?.Parent is not Control status || status.Parent is not TableLayoutPanel layout || _shellStatusTitle is null) return false;
        var widths = layout.GetColumnWidths();
        if (widths.Length < 2 || widths[1] <= 0) return false;
        // Waar de regel begint (na stip, kop en eventueel de klok): alles daarvoor is al gebruikt.
        var used = Math.Max(0, _shellStatusDetail.Left - _shellStatusDetail.Margin.Left);
        var available = widths[1] - used - _shellStatusDetail.Margin.Horizontal - LogicalToDeviceUnits(12);
        int Wanted(string value) => TextRenderer.MeasureText(value, _shellStatusDetail.Font).Width + LogicalToDeviceUnits(6);
        var chosen = ChooseFittingText(_shellStatusDetailVariants, _shellStatusDetailFull, Wanted, available);
        if (!string.Equals(_shellStatusDetail.Text, chosen, StringComparison.Ordinal)) _shellStatusDetail.Text = chosen;
        var wanted = Wanted(chosen);
        // De vaste grens van 560 kapte de tekst af terwijl er ruimte was; de ruimte die er is bepaalt het nu.
        _shellStatusDetail.MaximumSize = Size.Empty;
        _shellStatusDetail.AutoSize = false;
        _shellStatusDetail.AutoEllipsis = true;
        _shellStatusDetail.Size = new Size(Math.Max(LogicalToDeviceUnits(40), Math.Min(wanted, available)), Math.Max(_shellStatusTitle.Height, LogicalToDeviceUnits(20)));
        return true;
    }

    private const string ShellNavMeetings = "overleggen";
    private const string ShellNavNotifications = "meldingen";
    private const string ShellNavSettings = "instellingen";
    private const string ShellNavSystem = "systeem";

    private Panel? _shellTopBar;
    private ShellDot? _shellStatusDot;
    private Label? _shellStatusTitle;
    private Label? _shellStatusDetail;
    private Label? _shellTimerLabel;
    private Button? _shellBellButton;
    private int _shellBellCount;
    private readonly List<ShellNavButton> _shellNavButtons = new();
    private ShellNavButton? _shellSystemNavButton;
    private readonly Dictionary<string, TabPage> _shellPages = new(StringComparer.Ordinal);
    private TabPage? _recordingTabPage;
    private string _shellHealthOverall = string.Empty;
    private string _shellHealthMic = string.Empty;
    private string _shellHealthSystem = string.Empty;
    // "Het is stil, klopt" (tot er weer geluid van de anderen is of een nieuwe opname start).
    private DateTimeOffset? _systemSilenceAcknowledgedAt;
    private bool SystemSilenceAcknowledged => RecordingHealthPolicy.SilenceAcknowledgementActive(_systemSilenceAcknowledgedAt, DateTimeOffset.Now);
    private FlowLayoutPanel? _recordingPageSystemActions;
    private string _shellTrayStateKey = string.Empty;
    private readonly Dictionary<string, Icon> _shellTrayIcons = new(StringComparer.Ordinal);
    private readonly ToolStripMenuItem _trayWaitingItem = new("Niets wacht op je") { Visible = false };
    private bool _shellWasRecording;
    private bool _shellWasBusy;

    private Control BuildShellTopBar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Top,
            Height = LogicalToDeviceUnits(68),
            BackColor = ShellPalette.Surface,
            Margin = new Padding(0),
            Padding = new Padding(24, 12, 24, 12),
        };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(_session is null ? ShellPalette.Line : Color.FromArgb(243, 193, 187));
            e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
        };
        _shellTopBar = bar;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var wordmark = new Label
        {
            Text = Branding.DisplayName,
            AutoSize = true,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellPalette.Accent,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 24, 0),
            BackColor = Color.Transparent,
        };
        layout.Controls.Add(wordmark, 0, 0);

        var status = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Anchor = AnchorStyles.Left,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        _shellStatusDot = new ShellDot { DotColor = ShellPalette.Ok, Size = new Size(14, 14), Margin = new Padding(0, 5, 10, 0) };
        _shellStatusTitle = new Label { AutoSize = true, Text = "Klaar om op te nemen", Font = new Font("Segoe UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point), ForeColor = ShellPalette.Text, Margin = new Padding(0, 2, 12, 0), BackColor = Color.Transparent };
        _shellTimerLabel = new Label { AutoSize = true, Text = string.Empty, Visible = false, Font = new Font("Consolas", 13F, FontStyle.Bold, GraphicsUnit.Point), ForeColor = ShellPalette.Text, Margin = new Padding(0, 0, 12, 0), BackColor = Color.Transparent, Cursor = Cursors.Hand };
        _shellTimerLabel.Click += (_, _) =>
        {
            if (MayLeaveCurrentPage()) NavigateShell("opname");
        };
        _shellStatusDetail = new Label { AutoSize = true, Text = string.Empty, Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point), ForeColor = ShellPalette.TextSecondary, Margin = new Padding(0, 3, 0, 0), BackColor = Color.Transparent, AutoEllipsis = true, MaximumSize = new Size(LogicalToDeviceUnits(560), 0) };
        // Zonder gekozen apparaten: een klik op de stand (stip, kop of toelichting) gaat naar Instellingen, Opnemen.
        foreach (Control part in new Control[] { _shellStatusDot, _shellStatusTitle, _shellStatusDetail })
        {
            part.Click += (_, _) =>
            {
                if (_session is null && !_audioDeviceLoading && !CanStartRecordingNow() && MayLeaveCurrentPage()) NavigateToSettings("opnemen");
            };
        }
        status.Controls.Add(_shellStatusDot);
        status.Controls.Add(_shellStatusTitle);
        status.Controls.Add(_shellTimerLabel);
        status.Controls.Add(_shellStatusDetail);
        layout.Controls.Add(status, 1, 0);
        layout.SizeChanged += (_, _) => _ = FitShellStatusDetail();

        _shellBellButton = new Button
        {
            Size = LogicalToDeviceUnits(new Size(44, 44)),
            FlatStyle = FlatStyle.Flat,
            BackColor = ShellPalette.Surface,
            Margin = new Padding(0, 0, 12, 0),
            Anchor = AnchorStyles.Right,
            Cursor = Cursors.Hand,
            AccessibleName = "Meldingen",
            TabStop = true,
        };
        _shellBellButton.FlatAppearance.BorderSize = 0;
        _shellBellButton.Paint += PaintShellBell;
        _shellBellButton.Click += (_, _) =>
        {
            if (MayLeaveCurrentPage()) NavigateShell(ShellNavNotifications);
        };
        SetUiToolTip(_shellBellButton, "Meldingen");
        layout.Controls.Add(_shellBellButton, 3, 0);

        _recordToggleButton.Margin = new Padding(0);
        // Breed genoeg voor de langste tekst (starten of stoppen) in de taal van de interface; Frans en Duits zijn langer.
        var recordTextWidth = new[] { "Opname starten", "Opname stoppen" }
            .Max(caption => TextRenderer.MeasureText(UiText.T(caption), _recordToggleButton.Font).Width);
        _recordToggleButton.Size = new Size(Math.Max(LogicalToDeviceUnits(200), recordTextWidth + LogicalToDeviceUnits(84)), LogicalToDeviceUnits(44));
        _recordToggleButton.Anchor = AnchorStyles.Right;
        layout.Controls.Add(_recordToggleButton, 4, 0);

        bar.Controls.Add(layout);
        return bar;
    }

    private void PaintShellBell(object? sender, PaintEventArgs e)
    {
        if (sender is not Button button) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(_session is null ? ShellPalette.Surface : ShellPalette.RecordSoft);
        var size = button.Width * 0.5f;
        ShellIconPainter.Draw(g, ShellIcon.Bell, new RectangleF((button.Width - size) / 2f, (button.Height - size) / 2f, size, size), ShellPalette.Text);
        if (_shellBellCount <= 0) return;
        var text = _shellBellCount > 9 ? "9+" : _shellBellCount.ToString(CultureInfo.InvariantCulture);
        var font = ShellFont("Segoe UI", 7.5F, FontStyle.Bold, GraphicsUnit.Point);
        var h = button.LogicalToDeviceUnits(16);
        var w = Math.Max(h, TextRenderer.MeasureText(text, font).Width + button.LogicalToDeviceUnits(4));
        var badge = new RectangleF(button.Width - w - button.LogicalToDeviceUnits(4), button.LogicalToDeviceUnits(5), w, h);
        using (var path = ShellPalette.RoundedRectangle(badge, h / 2f))
        using (var brush = new SolidBrush(ShellPalette.Record))
        {
            g.FillPath(brush, path);
        }

        TextRenderer.DrawText(g, text, font, Rectangle.Round(badge), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private Control BuildShellBody(Control pages)
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = ShellPalette.Ground,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(232)));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var nav = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = ShellPalette.Surface,
            Margin = new Padding(0),
            Padding = new Padding(12, 16, 12, 16),
            AutoScroll = false,
        };
        nav.Paint += (_, e) =>
        {
            using var pen = new Pen(ShellPalette.Line);
            e.Graphics.DrawLine(pen, nav.Width - 1, 0, nav.Width - 1, nav.Height);
        };

        ShellNavButton AddNav(string key, string text, ShellIcon icon)
        {
            var button = new ShellNavButton { Key = key, Text = text, Icon = icon, Width = LogicalToDeviceUnits(206), AccessibleName = UiText.T(text) };
            button.Click += (_, _) =>
            {
                if (!MayLeaveCurrentPage()) return;
                if (key == ShellNavSystem) OpenShellSystemStatusTarget();
                else NavigateShell(key);
            };
            _shellNavButtons.Add(button);
            nav.Controls.Add(button);
            return button;
        }

        AddNav(ShellNavToday, "Vandaag", ShellIcon.Home);
        AddNav(ShellNavMeetings, "Overleggen", ShellIcon.Calendar);
        AddNav(ShellNavNotifications, "Meldingen", ShellIcon.Bell);
        var spacer = new Panel { Width = LogicalToDeviceUnits(206), Height = 24, BackColor = ShellPalette.Surface, Margin = new Padding(0) };
        nav.Controls.Add(spacer);
        AddNav(ShellNavSettings, "Instellingen", ShellIcon.Sliders);
        // De gratis versie: een vaste, rustige plek over de betaalde versie, direct onder Instellingen.
        AddNav(ShellNavProOffer, "Captivox Pro", ShellIcon.Star);
        _shellSystemNavButton = AddNav(ShellNavSystem, "Geen problemen gezien", ShellIcon.None);
        _shellSystemNavButton.DotColor = ShellPalette.Ok;
        _shellSystemNavButton.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        nav.Resize += (_, _) =>
        {
            // De onderste twee knoppen onderaan; de ruimte ertussen groeit mee.
            var used = _shellNavButtons.Sum(b => b.Height + b.Margin.Vertical);
            spacer.Height = Math.Max(24, nav.ClientSize.Height - nav.Padding.Vertical - used - 4);
        };

        var contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ShellPalette.Ground,
            Padding = new Padding(20, 16, 20, 8),
            Margin = new Padding(0),
        };
        pages.Dock = DockStyle.Fill;
        contentHost.Controls.Add(pages);

        body.Controls.Add(nav, 0, 0);
        body.Controls.Add(contentHost, 1, 0);
        return body;
    }

    // Mag de gebruiker weg van de pagina die nu openstaat (nog niet bewaarde keuzes)?
    private bool MayLeaveCurrentPage() => true;

    // Een plek kiezen. De pagina's zijn tabbladen in de paginahouder; "opname" is de pagina tijdens een opname.
    private void NavigateShell(string key)
    {
        if (_mainTabs is null) return;
        var target = key == "opname" ? _recordingTabPage : _shellPages.GetValueOrDefault(key);
        if (target is null) return;
        // Een andere pagina: SelectedIndexChanged synchroniseert. Dezelfde pagina: zelf (opnieuw tonen, bijvoorbeeld verversen).
        if (_mainTabs.SelectedTab != target) _mainTabs.SelectedTab = target;
        else SyncShellNavigation();
    }

    private void RegisterShellPage(string key, TabPage page) => _shellPages[key] = page;

    private void SyncShellNavigation()
    {
        if (_mainTabs?.SelectedTab is not { } selected) return;
        var key = _shellPages.FirstOrDefault(kv => kv.Value == selected).Key ?? string.Empty;
        // Oude pagina's die (nog) onder een plek vallen.
        if (key.Length == 0 && selected.Text == UiText.T("Opnemen")) key = ShellNavSettings;
        if (key == ShellNavLibrary) key = ShellNavMeetings;
        foreach (var button in _shellNavButtons) button.Selected = button.Key == key;

        if (selected == _todayTabPage) OnTodayPageShown();
        if (selected == _notificationsTabPage) RefreshNotificationsPage();
        if (selected == _meetingsTabPage) OnMeetingsPageShown();
        if (selected == _settingsHubTabPage) OnSettingsPageShown();
    }

    // Opname gestart of gestopt: bovenbalk rood met de tijd, de opnamepagina vooraan; na het stoppen terug naar Vandaag.
    // De vorige stand staat in een veld (de zichtbaarheid van een label is onbetrouwbaar als het venster verborgen is).
    private void UpdateShellRecordingState(bool isRunning)
    {
        if (_shellTopBar is null) return;
        var wasRecording = _shellWasRecording;
        var busy = _recordingBusy || _stopFinalizeInProgress;
        var changed = isRunning != wasRecording || busy != _shellWasBusy;
        _shellWasRecording = isRunning;
        _shellWasBusy = busy;
        _shellTopBar.BackColor = isRunning ? ShellPalette.RecordSoft : ShellPalette.Surface;
        if (_shellTimerLabel is not null) _shellTimerLabel.Visible = isRunning;
        if (isRunning && !wasRecording)
        {
            NavigateShell("opname");
        }
        else if (!isRunning && wasRecording && _mainTabs?.SelectedTab == _recordingTabPage)
        {
            NavigateShell(ShellNavToday);
        }

        _shellBellButton?.Invalidate();
        _shellTopBar.Invalidate(true);
        RefreshShellStatus();
        // De knop "Opnemen voor dit overleg" hoort alleen te staan als er niets loopt.
        if (changed) RenderToday();
    }

    // De oude vertaalslag loopt over de hele boom; de dynamische teksten van de schil en de opnameknop daarna opnieuw zetten.
    private void RefreshShellAfterLanguagePass()
    {
        SetRunningState(_session is not null);
        RefreshShellStatus();
        RenderToday();
        UpdateRecordingPageHealth();
    }

    private void UpdateShellElapsed(string text)
    {
        if (_shellTimerLabel is not null && _shellTimerLabel.Text != text) _shellTimerLabel.Text = text;
        if (_recordingPageTimerLabel is not null && _recordingPageTimerLabel.Text != text) _recordingPageTimerLabel.Text = text;
        // Elke tik: de opnamepagina (geluid in gewone taal hangt af van hoe lang het stil is) en de tekst bij het trayicoon.
        if (Visible && _mainTabs?.SelectedTab == _recordingTabPage) UpdateRecordingPageHealth();
        UpdateShellTray();
    }

    // De gezondheid van de opname in gewone taal (bovenbalk, opnamepagina, trayicoon).
    private void UpdateShellHealth(string overall, string mic, string system)
    {
        if (overall == _shellHealthOverall && mic == _shellHealthMic && system == _shellHealthSystem) return;
        _shellHealthOverall = overall;
        _shellHealthMic = mic;
        _shellHealthSystem = system;
        UpdateRecordingPageHealth();
        RefreshShellStatus();
    }

    private bool ShellRecordingProblem()
        => _session is not null && (_session.CaptureFailed || _session.CapacityReached);

    // Een door de gebruiker bevestigde stilte van de anderen ("Het is stil, klopt") telt niet als waarschuwing.
    private bool ShellRecordingWarning()
        => _session is not null && (_shellHealthMic.StartsWith("Stil", StringComparison.Ordinal) || (_shellHealthSystem.StartsWith("Stil", StringComparison.Ordinal) && !SystemSilenceAcknowledged) || _shellHealthOverall.StartsWith("Bijna vol", StringComparison.Ordinal));

    private void RefreshShellStatus()
    {
        if (_shellStatusTitle is null || _shellStatusDot is null || _shellStatusDetail is null) return;
        string title;
        string detail;
        var deviceVariants = Array.Empty<string>();
        Color dot;
        var halo = false;
        if (_session is not null)
        {
            halo = true;
            if (ShellRecordingProblem())
            {
                title = "Probleem met de opname";
                detail = _session.CapacityReached ? "Het bestand is vol; stop en start een nieuwe opname." : "De opname is gestopt; stop en start opnieuw.";
                dot = ShellPalette.Bad;
            }
            else if (ShellRecordingWarning())
            {
                title = "Opname loopt";
                detail = RecordingWarningText();
                dot = ShellPalette.Wait;
            }
            else
            {
                title = "Opname loopt";
                detail = "Geluid wordt bewaakt";
                dot = ShellPalette.Record;
            }
        }
        else if (_recordingBusy || _stopFinalizeInProgress)
        {
            title = "Opname afronden";
            detail = "Even geduld";
            dot = ShellPalette.Busy;
        }
        else if (!CanStartRecordingNow())
        {
            title = _audioDeviceLoading ? "Apparaten laden" : "Kies eerst je apparaten";
            detail = _audioDeviceLoading ? string.Empty : "Klik hier of ga naar Instellingen, Opnemen";
            dot = ShellPalette.Wait;
        }
        else
        {
            title = "Klaar om op te nemen";
            deviceVariants = DeviceLineVariants((_micComboBox.SelectedItem as DeviceInfo)?.FriendlyName ?? "-", (_systemComboBox.SelectedItem as DeviceInfo)?.FriendlyName ?? "-");
            detail = deviceVariants[0];
            dot = ShellPalette.Ok;
        }

        _shellStatusTitle.Text = title;
        var pointToSettings = _session is null && !_audioDeviceLoading && !CanStartRecordingNow();
        _shellStatusTitle.Cursor = pointToSettings ? Cursors.Hand : Cursors.Default;
        _shellStatusDetail.Cursor = pointToSettings ? Cursors.Hand : Cursors.Default;
        _shellStatusDot.Cursor = pointToSettings ? Cursors.Hand : Cursors.Default;
        _shellStatusTitle.ForeColor = _session is not null && !ShellRecordingProblem() && !ShellRecordingWarning() ? Color.FromArgb(143, 29, 18) : ShellPalette.Text;
        // Vertaald voordat de breedte wordt gemeten (FitShellStatusDetail).
        detail = UiText.T(detail);
        _shellStatusDetailVariants = deviceVariants;
        _shellStatusDetailFull = detail;
        SetUiToolTip(_shellStatusDetail, detail);
        // Nog geen indeling (heel vroeg bij het opstarten): dan de volledige tekst; de eerste indeling kiest daarna.
        if (!FitShellStatusDetail()) _shellStatusDetail.Text = detail;
        _shellStatusDot.DotColor = dot;
        _shellStatusDot.Halo = halo;
        _shellStatusDot.Invalidate();
        UpdateShellTray();
    }

    // De velden _shellHealth* zijn de Nederlandse standen van de bewaking ("Stil (45s)", "Bijna vol (nog 12 min)"); de
    // tekst hier is meteen in de taal van de interface.
    private string RecordingWarningText()
    {
        if (_shellHealthOverall.StartsWith("Bijna vol", StringComparison.Ordinal)) return UiText.F("Het bestand is bijna vol: {0}", UiText.T(_shellHealthOverall[9..].Trim(' ', '(', ')')));
        if (_shellHealthSystem.StartsWith("Stil", StringComparison.Ordinal) && !SystemSilenceAcknowledged) return UiText.F("Geen geluid van de anderen {0}", _shellHealthSystem[4..].Trim());
        if (_shellHealthMic.StartsWith("Stil", StringComparison.Ordinal)) return UiText.F("Je microfoon is stil {0}", _shellHealthMic[4..].Trim());
        return string.Empty;
    }

    // Systeemstatus links onderaan: in orde, of de eerste reden waarom niet.
    // De Nederlandse brontekst van de systeemstatus: de knop toont de vertaling, de klik kijkt naar de bron.
    private string _shellSystemStatusSource = string.Empty;

    private void SetShellSystemStatus(bool ok, string text)
    {
        if (_shellSystemNavButton is null) return;
        _shellSystemStatusSource = text;
        _shellSystemNavButton.Text = UiText.T(text);
        _shellSystemNavButton.AccessibleName = UiText.T(text);
        _shellSystemNavButton.DotColor = ok ? ShellPalette.Ok : ShellPalette.Wait;
        _shellSystemNavButton.Invalidate();
    }

    // Het trayicoon: grijs (klaar), rode stip (opname), getal (wacht op jou), rood (probleem).
    private void UpdateShellTray()
    {
        var waiting = _todayWaitingCount;
        string key;
        string tooltip;
        if (ShellRecordingProblem())
        {
            key = "bad";
            tooltip = UiText.T("Captivox: probleem met de opname");
        }
        else if (_session is not null)
        {
            // Per minuut, niet per seconde: elke wijziging is een aanroep naar het systeemvak.
            var minutes = _recordingStopwatch.IsRunning ? (int)_recordingStopwatch.Elapsed.TotalMinutes : 0;
            if (ShellRecordingWarning())
            {
                // Ook met het venster weg te zien: oranje stip en de waarschuwing in de tekst.
                key = "recwarn";
                tooltip = "Captivox: " + RecordingWarningText();
            }
            else
            {
                key = "rec";
                tooltip = UiText.F("Captivox: opname loopt ({0} min)", minutes);
            }
        }
        else if (waiting > 0)
        {
            key = "wait" + Math.Min(waiting, 10).ToString(CultureInfo.InvariantCulture);
            tooltip = waiting == 1 ? UiText.T("Captivox: 1 ding wacht op je") : UiText.F("Captivox: {0} dingen wachten op je", waiting);
        }
        else
        {
            key = "ok";
            tooltip = UiText.T("Captivox: klaar om op te nemen");
        }

        _trayWaitingItem.Visible = waiting > 0 && _session is null;
        _trayWaitingItem.Text = waiting == 1 ? UiText.T("1 ding wacht op je") : UiText.F("{0} dingen wachten op je", waiting);
        var trimmed = tooltip.Length > 63 ? tooltip[..63] : tooltip;
        if (_notifyIcon.Text != trimmed) _notifyIcon.Text = trimmed;
        if (key == _shellTrayStateKey) return;
        _shellTrayStateKey = key;
        try
        {
            if (!_shellTrayIcons.TryGetValue(key, out var icon))
            {
                icon = CreateShellTrayIcon(key);
                _shellTrayIcons[key] = icon;
            }

            _notifyIcon.Icon = icon;
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Trayicoon bijwerken mislukt", ex);
        }
    }

    private static Icon CreateShellTrayIcon(string key)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            var problem = key == "bad";
            using (var path = ShellPalette.RoundedRectangle(new RectangleF(1, 1, 28, 28), 7))
            using (var brush = new SolidBrush(problem ? ShellPalette.Bad : Color.FromArgb(91, 102, 114)))
            {
                g.FillPath(brush, path);
            }

            using (var font = new Font("Segoe UI", 15F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var white = new SolidBrush(Color.White))
            {
                g.DrawString(problem ? "!" : "C", font, white, new RectangleF(1, 1, 28, 28), format);
            }

            if (key is "rec" or "recwarn")
            {
                using var ring = new SolidBrush(Color.White);
                using var red = new SolidBrush(key == "recwarn" ? ShellPalette.Wait : ShellPalette.Record);
                g.FillEllipse(ring, 17, 17, 15, 15);
                g.FillEllipse(red, 19, 19, 11, 11);
            }
            else if (key.StartsWith("wait", StringComparison.Ordinal))
            {
                var count = int.Parse(key[4..], CultureInfo.InvariantCulture);
                var text = count >= 10 ? "9+" : count.ToString(CultureInfo.InvariantCulture);
                using var ring = new SolidBrush(Color.White);
                using var amber = new SolidBrush(ShellPalette.Wait);
                g.FillEllipse(ring, 14, 0, 18, 18);
                g.FillEllipse(amber, 16, 2, 14, 14);
                using var font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Pixel);
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using var white = new SolidBrush(Color.White);
                g.DrawString(text, font, white, new RectangleF(16, 2, 14, 14), format);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeShellMethods.DestroyIcon(handle);
        }
    }

    private void DisposeShellTrayIcons()
    {
        foreach (var icon in _shellTrayIcons.Values)
        {
            try { icon.Dispose(); } catch { }
        }

        _shellTrayIcons.Clear();
    }

    private static class NativeShellMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr handle);
    }
}
