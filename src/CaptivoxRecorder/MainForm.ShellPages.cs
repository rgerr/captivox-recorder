using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using LocalCaptureAgent.Controls;
using LocalCaptureAgent.Models;
using LocalCaptureAgent.Services;

namespace LocalCaptureAgent;

// De nieuwe pagina's van de schil: Vandaag (startscherm), de opnamepagina, Meldingen en het overzicht onder
// Instellingen.
public sealed partial class MainForm
{
    private static readonly CultureInfo DutchCulture = CultureInfo.GetCultureInfo("nl-NL");

    private TabPage? _todayTabPage;
    private TabPage? _notificationsTabPage;
    private TabPage? _settingsHubTabPage;
    private Label? _todayDateLabel;
    private Label? _todayGreetingLabel;
    private ShellCard? _todayNextMeetingCard;
    private ShellCard? _todayWaitingCard;
    private ShellCard? _todayNotificationsCard;
    private int _todayWaitingCount;
    private readonly System.Windows.Forms.Timer _todayRefreshTimer = new() { Interval = 5 * 60 * 1000 };
    private DateTimeOffset _notificationsSeenUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset _recordingFreeSpaceCheckedAt = DateTimeOffset.MinValue;
    private string _recordingFreeSpaceText = string.Empty;

    private Label? _recordingPageTimerLabel;
    private Label? _recordingPageMicState;
    private Label? _recordingPageSystemState;
    private Label? _recordingPageMicDevice;
    private Label? _recordingPageSystemDevice;
    private Panel? _recordingPageSystemRow;
    private Label? _recordingPageSystemAdvice;
    private Label? _recordingPageChecks;
    private readonly LevelMeterControl _recordingPageMicMeter = new() { Height = 16, Margin = new Padding(0, 6, 0, 2) };
    private readonly LevelMeterControl _recordingPageSystemMeter = new() { Height = 16, Margin = new Padding(0, 6, 0, 2) };

    private FlowLayoutPanel? _notificationsList;
    private Label? _notificationsWatcherLabel;

    // ---------- Gedeelde opbouw ----------

    // Lettertypen worden gedeeld: de kaarten worden bij elke verversing opnieuw opgebouwd en een nieuw lettertype per label
    // liep in een programma dat dagen draait langzaam vol. Een gedeeld lettertype wordt nooit weggegooid.
    private static readonly Dictionary<(string Family, float Size, FontStyle Style), Font> ShellFonts = new();

    private static Font ShellFont(string family, float size, FontStyle style, GraphicsUnit unit)
    {
        lock (ShellFonts)
        {
            if (!ShellFonts.TryGetValue((family, size, style), out var font))
            {
                font = new Font(family, size, style, unit);
                ShellFonts[(family, size, style)] = font;
            }

            return font;
        }
    }

    // Oude onderdelen opruimen voor een nieuwe opbouw (Controls.Clear gooit ze niet weg: handles bleven bestaan).
    private static void ShellClearControls(Control host)
    {
        var old = host.Controls.Cast<Control>().ToList();
        host.Controls.Clear();
        foreach (var control in old) control.Dispose();
    }

    // Hoogte van een regel: minstens de ontworpen hoogte, en altijd genoeg voor de letter (bij een grotere schermschaal).
    private static int ShellLineHeight(float size, FontStyle style, int minimum)
    {
        var lines = Math.Max(1, (int)Math.Round(minimum / 22.0));
        return Math.Max(minimum, lines * (ShellFont("Segoe UI", size, style, GraphicsUnit.Point).Height + 5));
    }

    // De teksten van de schil zijn al gewone taal en vaak dynamisch: de oude vertaalslag slaat ze over.
    private static Label ShellLabel(string text, float size = 10F, FontStyle style = FontStyle.Regular, Color? color = null, Padding? margin = null)
        => new()
        {
            AccessibleDescription = "skip-ui-localization",
            Text = text,
            AutoSize = true,
            Font = ShellFont("Segoe UI", size, style, GraphicsUnit.Point),
            ForeColor = color ?? ShellPalette.Text,
            Margin = margin ?? new Padding(0),
            BackColor = Color.Transparent,
            UseMnemonic = false,
        };

    // Een regel die meeschaalt met de breedte en afkapt met "...".
    // rowLabel: een regel waarop dubbelklikken iets opent; zie ShellRowLabel.
    private static Label ShellLineLabel(string text, float size = 10F, FontStyle style = FontStyle.Regular, Color? color = null, int height = 22, bool rowLabel = false)
    {
        Label label = rowLabel ? new ShellRowLabel() : new Label();
        label.AccessibleDescription = "skip-ui-localization";
        label.Text = text;
        label.AutoSize = false;
        label.AutoEllipsis = true;
        label.Height = ShellLineHeight(size, style, height);
        label.Dock = DockStyle.Top;
        label.Font = ShellFont("Segoe UI", size, style, GraphicsUnit.Point);
        label.ForeColor = color ?? ShellPalette.Text;
        label.Margin = new Padding(0);
        label.BackColor = Color.Transparent;
        label.UseMnemonic = false;
        label.TextAlign = ContentAlignment.MiddleLeft;
        return label;
    }

    // Wat er op de gebruiker wacht (het getal in het systeemvak), volgens de lijst van Overleggen.
    private int TodayWaitingTotal() => _meetingsWaitingCount;

    private static Label ShellSectionTitle(string text)
        => ShellLabel(text.ToUpperInvariant(), 8.5F, FontStyle.Bold, ShellPalette.TextSecondary, new Padding(0, 0, 0, 10));

    // Twee regels (kop en toelichting) in een vak dat de breedte van zijn cel vult en netjes afkapt.
    private static Control ShellTwoLines(string title, string detail, float titleSize = 10.5F)
    {
        var topHeight = ShellLineHeight(titleSize, FontStyle.Bold, 27);
        var bottomHeight = ShellLineHeight(9.5F, FontStyle.Regular, 23);
        var box = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill, Height = topHeight + bottomHeight, Margin = new Padding(0), Padding = new Padding(0), BackColor = Color.Transparent };
        box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, topHeight));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, bottomHeight));
        var top = ShellLineLabel(title, titleSize, FontStyle.Bold, ShellPalette.Text, 27);
        top.Dock = DockStyle.Fill;
        var bottom = ShellLineLabel(detail, 9.5F, FontStyle.Regular, ShellPalette.TextSecondary, 23);
        bottom.Dock = DockStyle.Fill;
        box.Controls.Add(top, 0, 0);
        box.Controls.Add(bottom, 0, 1);
        return box;
    }

    private static ShellCard ShellNewCard()
        => new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Dock = DockStyle.Top };

    private static TableLayoutPanel ShellStack()
    {
        var stack = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Margin = new Padding(0), Padding = new Padding(0), BackColor = Color.Transparent };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return stack;
    }

    private static void ShellStackAdd(TableLayoutPanel stack, Control control)
    {
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        stack.Controls.Add(control, 0, stack.RowCount++);
    }

    private static Button ShellButton(string text, bool primary, EventHandler onClick, Color? fill = null)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 40),
            Padding = new Padding(12, 0, 12, 0),
            FlatStyle = FlatStyle.Flat,
            Font = ShellFont("Segoe UI", 9.75F, FontStyle.Bold, GraphicsUnit.Point),
            BackColor = primary ? fill ?? ShellPalette.Accent : ShellPalette.Surface,
            ForeColor = primary ? Color.White : ShellPalette.Text,
            Cursor = Cursors.Hand,
            Margin = new Padding(8, 0, 0, 0),
            Anchor = AnchorStyles.Right,
            UseMnemonic = false,
        };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(185, 194, 204);
        button.Click += onClick;
        return button;
    }

    private static TabPage ShellNewPage(string text)
        => new BufferedTabPage(text) { BackColor = ShellPalette.Ground, AutoScroll = true, Padding = new Padding(0) };

    // ---------- Vandaag ----------

    private TabPage BuildTodayPage()
    {
        var page = ShellNewPage("Vandaag");
        _todayTabPage = page;

        var root = ShellStack();
        root.Padding = new Padding(4, 4, 12, 12);

        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 16) };
        _todayDateLabel = ShellLabel(string.Empty, 10F, FontStyle.Regular, ShellPalette.TextSecondary);
        _todayGreetingLabel = ShellLabel(string.Empty, 18F, FontStyle.Bold, ShellPalette.Text, new Padding(0, 2, 0, 0));
        titles.Controls.Add(_todayDateLabel);
        titles.Controls.Add(_todayGreetingLabel);
        ShellStackAdd(root, titles);

        var columns = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, BackColor = Color.Transparent, Margin = new Padding(0) };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        var left = ShellStack();
        left.Margin = new Padding(0, 0, 16, 0);
        var right = ShellStack();
        _todayNextMeetingCard = ShellNewCard();
        _todayWaitingCard = ShellNewCard();
        _todayNotificationsCard = ShellNewCard();
        ShellStackAdd(left, _todayNextMeetingCard);
        ShellStackAdd(left, _todayWaitingCard);
        ShellStackAdd(right, _todayNotificationsCard);
        left.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        right.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        columns.Controls.Add(left, 0, 0);
        columns.Controls.Add(right, 1, 0);
        ShellStackAdd(root, columns);

        page.Controls.Add(root);
        RenderToday();
        _todayRefreshTimer.Tick += (_, _) =>
        {
            try
            {
                // De datum en de begroeting schuiven met de tijd mee.
                if (_mainTabs?.SelectedTab == _todayTabPage && Visible) RenderToday();
                RefreshNotificationBadge();
            }
            catch (Exception ex)
            {
                AppRuntime.LogException("Vandaag automatisch bijwerken mislukt", ex);
            }
        };
        return page;
    }

    private void OnTodayPageShown() => RenderToday();

    private void RenderToday()
    {
        if (_todayGreetingLabel is null || _todayDateLabel is null) return;
        var now = DateTime.Now;
        var date = now.ToString("dddd d MMMM", DutchCulture);
        _todayDateLabel.Text = char.ToUpperInvariant(date[0]) + date[1..];
        _todayGreetingLabel.Text = now.Hour < 12 ? "Goedemorgen" : now.Hour < 18 ? "Goedemiddag" : "Goedenavond";

        RenderTodayNextMeeting();
        RenderTodayWaiting();
        RenderTodayNotifications();

        _todayWaitingCount = TodayWaitingTotal();
        foreach (var button in _shellNavButtons)
        {
            if (button.Key != ShellNavMeetings) continue;
            button.CountLabel = "open";
            button.CountCapped = false;
            button.Count = _todayWaitingCount;
            button.Invalidate();
        }

        RefreshShellSystemStatus();
        UpdateShellTray();
    }

    private void RenderTodayNextMeeting()
    {
        var card = _todayNextMeetingCard;
        if (card is null) return;
        card.SuspendLayout();
        ShellClearControls(card);
        var stack = ShellStack();
        ShellStackAdd(stack, ShellSectionTitle("Vandaag"));
        ShellStackAdd(stack, ShellLineLabel("Nog niets voor vandaag.", 10.5F, FontStyle.Regular, ShellPalette.TextSecondary));
        if (_session is null && !_recordingBusy && !_stopFinalizeInProgress)
        {
            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 10, 0, 0) };
            var record = ShellButton("Opname starten", primary: true, async (_, _) =>
            {
                if (_session is not null || _recordingBusy || _stopFinalizeInProgress) return;
                await RunRecordingUiActionAsync("Opname starten", StartRecordingAsync);
            }, ShellPalette.Record);
            record.Margin = new Padding(0);
            actions.Controls.Add(record);
            ShellStackAdd(stack, actions);
        }

        card.Controls.Add(stack);
        card.ResumeLayout(true);
    }

    // Wat op de gebruiker wacht, volgens de lijst van Overleggen (lokaal).
    private void RenderTodayWaiting()
    {
        var card = _todayWaitingCard;
        if (card is null) return;
        card.SuspendLayout();
        ShellClearControls(card);
        var stack = ShellStack();
        ShellStackAdd(stack, ShellSectionTitle("Wacht op jou"));
        var reviews = TodayWaitingTotal();
        if (_meetingsWaitingLoadedAt == DateTimeOffset.MinValue)
        {
            ShellStackAdd(stack, ShellLineLabel("Open Overleggen om te zien wat er klaarstaat.", 10F, FontStyle.Regular, ShellPalette.TextSecondary));
        }
        else if (reviews > 0)
        {
            // Opnames waarbij het opnemen of uitschrijven misging of onderbroken werd: opnieuw uitschrijven kan vanuit Overleggen.
            ShellStackAdd(stack, TodayWaitingRow(ShellIcon.Calendar, ShellPalette.WaitSoft, ShellPalette.Wait, $"{reviews} {(reviews == 1 ? "opname vraagt" : "opnames vragen")} aandacht", "Mislukt of onderbroken; je kunt opnieuw laten uitschrijven",
                "Bekijken", true, (_, _) => ShowWaitingMeetings()));
        }
        else
        {
            ShellStackAdd(stack, ShellLineLabel("Niets wacht op je. Mooi.", 10.5F, FontStyle.Regular, ShellPalette.Ok));
        }

        card.Controls.Add(stack);
        card.ResumeLayout(true);
    }

    private Control TodayWaitingRow(ShellIcon icon, Color tile, Color iconColor, string title, string detail, string action, bool primary, EventHandler onClick)
    {
        // Kolommen: pictogram, tekst (vult de breedte), knop. Op de tweede regel eventuele labels en de toelichting.
        var row = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, BackColor = Color.Transparent, Margin = new Padding(0, 6, 0, 6), Padding = new Padding(0, 4, 0, 4) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(new ShellIconTile { Glyph = icon, TileColor = tile, ForeColor = iconColor }, 0, 0);

        var topHeight = ShellLineHeight(10.5F, FontStyle.Bold, 26);
        var bottomHeight = ShellLineHeight(9.5F, FontStyle.Bold, 26);
        var box = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill, Height = topHeight + bottomHeight, Margin = new Padding(0), BackColor = Color.Transparent };
        box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, topHeight));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, bottomHeight));
        var top = ShellLineLabel(title, 10.5F, FontStyle.Bold, ShellPalette.Text, 26);
        top.Dock = DockStyle.Fill;
        box.Controls.Add(top, 0, 0);
        var second = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Color.Transparent };
        second.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        second.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var pills = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
        second.Controls.Add(pills, 0, 0);
        var bottom = ShellLineLabel(detail, 9.5F, FontStyle.Regular, ShellPalette.TextSecondary, bottomHeight);
        bottom.Dock = DockStyle.Fill;
        bottom.Margin = new Padding(4, 0, 0, 0);
        second.Controls.Add(bottom, 1, 0);
        box.Controls.Add(second, 0, 1);
        row.Controls.Add(box, 1, 0);

        var button = ShellButton(action, primary, onClick);
        button.Margin = new Padding(12, 6, 0, 0);
        row.Controls.Add(button, 2, 0);
        row.Paint += (_, e) =>
        {
            using var pen = new Pen(ShellPalette.LineSoft);
            e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
        };
        return row;
    }

    private void RenderTodayNotifications()
    {
        var card = _todayNotificationsCard;
        if (card is null) return;
        card.SuspendLayout();
        ShellClearControls(card);
        var stack = ShellStack();
        ShellStackAdd(stack, ShellSectionTitle("Laatste meldingen"));
        var items = NotificationHistoryStore.ReadRecent(AppRuntime.GetSettingsDirectory(), DateTimeOffset.UtcNow, TimeSpan.FromDays(3), 3);
        if (items.Count == 0) ShellStackAdd(stack, ShellLineLabel("Nog geen meldingen.", 9.5F, FontStyle.Regular, ShellPalette.TextSecondary));
        foreach (var item in items)
        {
            ShellStackAdd(stack, ShellLineLabel(item.Title, 10F, FontStyle.Bold, ShellPalette.Text, 22));
            ShellStackAdd(stack, ShellLineLabel(item.At.LocalDateTime.ToString("HH:mm", DutchCulture) + "  |  " + item.Message, 9.25F, FontStyle.Regular, ShellPalette.TextSecondary, 22));
        }

        var all = new LinkLabel { Text = "Alle meldingen", AutoSize = true, Margin = new Padding(0, 8, 0, 0), LinkColor = ShellPalette.Accent, ActiveLinkColor = ShellPalette.Accent, Font = ShellFont("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point) };
        all.LinkClicked += (_, _) => NavigateShell(ShellNavNotifications);
        ShellStackAdd(stack, all);
        card.Controls.Add(stack);
        card.ResumeLayout(true);
    }

    // Het geluid van de anderen komt niet binnen: de opname stoppen (bewaard), dan het apparaat kiezen en opnieuw starten.
    private async Task ChooseOtherSystemDeviceAsync()
    {
        if (_session is null || _stopFinalizeInProgress) return;
        var answer = ShowOwnedMessage(this, "De opname stopt en wordt bewaard. Daarna kies je onder Instellingen, Opnemen het apparaat waar Teams op afspeelt, en start je een nieuwe opname." + Environment.NewLine + Environment.NewLine + "Opname nu stoppen?", "Ander apparaat kiezen", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        await RunRecordingUiActionAsync("Opname stoppen", StopRecordingAsync);
        if (_session is null) NavigateToSettings("opnemen");
    }

    // Systeemstatus links onder: wat als eerste aandacht vraagt.
    private void RefreshShellSystemStatus()
    {
        if (!_audioDeviceLoading && !CanStartRecordingNow())
        {
            SetShellSystemStatus(false, "Kies je apparaten");
        }
        else
        {
            SetShellSystemStatus(true, "Geen problemen gezien");
        }
    }

    // Waar een klik op de systeemstatus heen gaat: naar de plek waar het probleem speelt; in orde naar het systeemoverzicht.
    private void OpenShellSystemStatusTarget()
    {
        var text = _shellSystemNavButton?.Text ?? string.Empty;
        if (text == "Kies je apparaten") NavigateToSettings("opnemen");
        else NavigateShell(ShellNavSystem);
    }

    // ---------- Opname loopt ----------

    private TabPage BuildRecordingPage()
    {
        var page = ShellNewPage("Opname loopt");
        _recordingTabPage = page;
        var root = ShellStack();
        root.Padding = new Padding(4, 4, 12, 12);

        var header = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 16) };
        header.Controls.Add(ShellLabel("Opname loopt", 18F, FontStyle.Bold, Color.FromArgb(143, 29, 18), new Padding(0, 0, 16, 0)));
        _recordingPageTimerLabel = new Label { Text = "00:00:00", AutoSize = true, Font = ShellFont("Consolas", 18F, FontStyle.Bold, GraphicsUnit.Point), ForeColor = ShellPalette.Text, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 0) };
        header.Controls.Add(_recordingPageTimerLabel);
        ShellStackAdd(root, header);

        var columns = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, BackColor = Color.Transparent };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        var left = ShellStack();
        left.Margin = new Padding(0, 0, 16, 0);
        var right = ShellStack();


        var soundCard = ShellNewCard();
        var soundStack = ShellStack();
        ShellStackAdd(soundStack, ShellSectionTitle("Geluid"));
        ShellStackAdd(soundStack, RecordingSoundRow(ShellIcon.Microphone, "Jouw microfoon", _recordingPageMicMeter, out _recordingPageMicState, out _recordingPageMicDevice, out _));
        ShellStackAdd(soundStack, RecordingSoundRow(ShellIcon.Speaker, "Geluid van de anderen (Teams)", _recordingPageSystemMeter, out _recordingPageSystemState, out _recordingPageSystemDevice, out _recordingPageSystemRow));
        _recordingPageSystemAdvice = ShellLineLabel("Hoor jij de anderen wel? Dan neemt Captivox het verkeerde apparaat op. Stop de opname, kies onder Instellingen, Opnemen het apparaat waar Teams op afspeelt en start opnieuw.", 9.5F, FontStyle.Regular, Color.FromArgb(61, 42, 10), 66);
        _recordingPageSystemAdvice.Visible = false;
        ShellStackAdd(soundStack, _recordingPageSystemAdvice);
        // Wat de gebruiker kan doen: een ander apparaat kiezen (de opname stopt eerst), of bevestigen dat het echt stil is.
        _recordingPageSystemActions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 6, 0, 0), Visible = false };
        var otherDevice = ShellButton("Ander apparaat kiezen", false, async (_, _) => await ChooseOtherSystemDeviceAsync());
        otherDevice.Margin = new Padding(0, 0, 8, 0);
        _recordingPageSystemActions.Controls.Add(otherDevice);
        _recordingPageSystemActions.Controls.Add(ShellButton("Het is stil, klopt", false, (_, _) =>
        {
            _systemSilenceAcknowledgedAt = DateTimeOffset.Now;
            UpdateRecordingPageHealth();
            RefreshShellStatus();
        }));
        ShellStackAdd(soundStack, _recordingPageSystemActions);
        soundCard.Controls.Add(soundStack);
        ShellStackAdd(left, soundCard);

        var checkCard = ShellNewCard();
        var checkStack = ShellStack();
        ShellStackAdd(checkStack, ShellSectionTitle("Stand van de opname"));
        _recordingPageChecks = ShellLabel(string.Empty, 9.75F, FontStyle.Regular, ShellPalette.Text);
        _recordingPageChecks.MaximumSize = new Size(380, 0);
        ShellStackAdd(checkStack, _recordingPageChecks);
        checkCard.Controls.Add(checkStack);
        ShellStackAdd(right, checkCard);

        var afterCard = ShellNewCard();
        var afterStack = ShellStack();
        ShellStackAdd(afterStack, ShellSectionTitle("Na het stoppen"));
        ShellStackAdd(afterStack, ShellLineLabel("1. Uitschrijven (op deze pc, na elkaar)", 9.75F));
        ShellStackAdd(afterStack, ShellLineLabel("2. Klaar: je krijgt een melding en vindt de tekst onder Overleggen", 9.75F, FontStyle.Regular, ShellPalette.Text, 40));
        afterCard.Controls.Add(afterStack);
        ShellStackAdd(right, afterCard);

        left.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        right.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        columns.Controls.Add(left, 0, 0);
        columns.Controls.Add(right, 1, 0);
        ShellStackAdd(root, columns);
        page.Controls.Add(root);
        page.VisibleChanged += (_, _) => { if (page.Visible) UpdateRecordingPageHealth(); };
        return page;
    }

    private Control RecordingSoundRow(ShellIcon icon, string title, LevelMeterControl meter, out Label state, out Label device, out Panel rowPanel)
    {
        var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, BackColor = Color.Transparent, Margin = new Padding(0, 4, 0, 12), Padding = new Padding(8) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new ShellIconTile { Glyph = icon, TileColor = Color.FromArgb(230, 243, 234), ForeColor = ShellPalette.Ok, Size = new Size(44, 44) }, 0, 0);
        var text = ShellStack();
        var top = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, BackColor = Color.Transparent, Margin = new Padding(0) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(ShellLineLabel(title, 10.5F, FontStyle.Bold), 0, 0);
        state = ShellLabel("-", 9.75F, FontStyle.Bold, ShellPalette.Ok, new Padding(8, 2, 0, 0));
        top.Controls.Add(state, 1, 0);
        ShellStackAdd(text, top);
        meter.Dock = DockStyle.Top;
        ShellStackAdd(text, meter);
        device = ShellLineLabel(string.Empty, 9F, FontStyle.Regular, ShellPalette.TextSecondary, 20);
        ShellStackAdd(text, device);
        row.Controls.Add(text, 1, 0);
        rowPanel = row;
        return row;
    }

    // De opnamepagina bijwerken: overleg, geluid in gewone taal, ruimte.
    private void UpdateRecordingPageHealth()
    {
        if (_recordingPageMicState is null || _recordingPageSystemState is null || _recordingPageChecks is null) return;

        // Wat er echt binnenkomt: geluid in de laatste vijftien seconden. "Stil" is de waarschuwing van de bewaking (na de
        // ingestelde tijd); daartussen is het "even stil". Staat de bewaking uit, dan telt alleen het geluid zelf.
        var nowOffset = DateTimeOffset.Now;
        var active = _session is not null;
        var startedAt = _startedAt ?? DateTimeOffset.MinValue;
        (string Text, Color Color, bool Hearing) Plain(string state, DateTimeOffset lastSignal)
        {
            if (!active) return ("Niet actief", ShellPalette.TextSecondary, false);
            if (state is "Gestopt" or "Afgekapt") return ("Gestopt", ShellPalette.Bad, false);
            if (state.StartsWith("Stil", StringComparison.Ordinal)) return ("Geen geluid " + state[4..].Trim(), ShellPalette.Wait, false);
            // Bij de start staat het laatste signaal op de starttijd: dat is nog geen geluid.
            if (lastSignal <= startedAt) return ("Wacht op geluid", ShellPalette.TextSecondary, false);
            var quiet = nowOffset - lastSignal;
            if (quiet <= TimeSpan.FromSeconds(15)) return ("Geluid komt binnen", ShellPalette.Ok, true);
            return ($"Even stil ({Math.Min(9999, (int)quiet.TotalSeconds)}s)", ShellPalette.TextSecondary, false);
        }

        var mic = Plain(_shellHealthMic, _lastMicSignalAt);
        var system = Plain(_shellHealthSystem, _lastSystemSignalAt);
        _recordingPageMicState.Text = mic.Text;
        _recordingPageMicState.ForeColor = mic.Color;
        _recordingPageSystemState.Text = system.Text;
        _recordingPageSystemState.ForeColor = system.Color;
        if (_recordingPageMicDevice is not null) _recordingPageMicDevice.Text = (_micComboBox.SelectedItem as DeviceInfo)?.FriendlyName ?? string.Empty;
        if (_recordingPageSystemDevice is not null) _recordingPageSystemDevice.Text = (_systemComboBox.SelectedItem as DeviceInfo)?.FriendlyName ?? string.Empty;
        var systemSilent = active && _shellHealthSystem.StartsWith("Stil", StringComparison.Ordinal) && !SystemSilenceAcknowledged;
        if (SystemSilenceAcknowledged && active && _shellHealthSystem.StartsWith("Stil", StringComparison.Ordinal))
        {
            _recordingPageSystemState.Text = "Stil (bevestigd)";
            _recordingPageSystemState.ForeColor = ShellPalette.TextSecondary;
        }

        if (_recordingPageSystemAdvice is not null) _recordingPageSystemAdvice.Visible = systemSilent;
        if (_recordingPageSystemActions is not null) _recordingPageSystemActions.Visible = systemSilent;
        if (_recordingPageSystemRow is not null) _recordingPageSystemRow.BackColor = systemSilent ? ShellPalette.WaitSoft : Color.Transparent;

        var lines = new List<string>();
        if (ShellRecordingProblem())
        {
            lines.Add(_session!.CapacityReached ? "Het bestand is vol: nieuwe audio wordt niet meer opgeslagen. Stop en start een nieuwe opname." : "De opname is gestopt: " + (_session.CaptureFailureSummary ?? "onbekende fout") + ". Stop en start opnieuw.");
        }
        else if (active)
        {
            lines.Add(mic.Hearing && system.Hearing ? "De opname loopt en het geluid komt binnen." : "De opname loopt.");
            if (_session!.RemainingCapacity is { } remaining) lines.Add($"Ruimte in het bestand: nog ongeveer {Math.Max(0, (int)remaining.TotalHours)} uur {Math.Max(0, remaining.Minutes)} minuten.");
        }
        else
        {
            lines.Add("Er loopt geen opname.");
        }

        var free = RecordingFreeSpaceText();
        if (free.Length > 0) lines.Add(free);
        _recordingPageChecks.Text = string.Join(Environment.NewLine, lines);
    }

    // Hoogstens eens per halve minuut kijken (de pagina wordt elke seconde bijgewerkt).
    private string RecordingFreeSpaceText()
    {
        if (DateTimeOffset.Now - _recordingFreeSpaceCheckedAt < TimeSpan.FromSeconds(30)) return _recordingFreeSpaceText;
        _recordingFreeSpaceCheckedAt = DateTimeOffset.Now;
        _recordingFreeSpaceText = ReadRecordingFreeSpace();
        return _recordingFreeSpaceText;
    }

    private string ReadRecordingFreeSpace()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(FirstNonEmpty(_outputRootTextBox.Text, Branding.DefaultRecordingsRoot)));
            if (string.IsNullOrWhiteSpace(root)) return string.Empty;
            var drive = new DriveInfo(root);
            var gb = drive.AvailableFreeSpace / (1024d * 1024 * 1024);
            return gb < 5 ? $"Let op: nog maar {gb:0.0} GB vrij op {drive.Name}." : $"Schijfruimte: {gb:0} GB vrij op {drive.Name}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return string.Empty;
        }
    }

    // ---------- Meldingen ----------

    private TabPage BuildNotificationsPage()
    {
        var page = ShellNewPage("Meldingen");
        _notificationsTabPage = page;
        var root = ShellStack();
        root.Padding = new Padding(4, 4, 12, 12);
        ShellStackAdd(root, ShellLabel("Meldingen", 18F, FontStyle.Bold, ShellPalette.Text, new Padding(0, 0, 0, 6)));
        _notificationsWatcherLabel = ShellLabel(string.Empty, 9.75F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(0, 0, 0, 14));
        ShellStackAdd(root, _notificationsWatcherLabel);
        var card = ShellNewCard();
        _notificationsList = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, BackColor = Color.Transparent, Margin = new Padding(0) };
        card.Controls.Add(_notificationsList);
        ShellStackAdd(root, card);
        page.Controls.Add(root);
        return page;
    }

    private void RefreshNotificationsPage()
    {
        if (_notificationsList is null || _notificationsWatcherLabel is null) return;
        _notificationsWatcherLabel.Text = "De meldingen van Captivox op deze pc in de laatste drie dagen.";
        _notificationsWatcherLabel.ForeColor = ShellPalette.TextSecondary;
        var items = NotificationHistoryStore.ReadRecent(AppRuntime.GetSettingsDirectory(), DateTimeOffset.UtcNow, TimeSpan.FromDays(3), 80);
        _notificationsList.SuspendLayout();
        ShellClearControls(_notificationsList);
        var width = Math.Max(400, (_notificationsList.Parent?.ClientSize.Width ?? 800) - 48);
        if (items.Count == 0) _notificationsList.Controls.Add(ShellLabel("Geen meldingen in de laatste drie dagen.", 10F, FontStyle.Regular, ShellPalette.TextSecondary));
        string? lastDay = null;
        foreach (var item in items)
        {
            var local = item.At.LocalDateTime;
            var day = local.ToString("dddd d MMMM", DutchCulture);
            if (day != lastDay)
            {
                lastDay = day;
                _notificationsList.Controls.Add(ShellLabel((char.ToUpperInvariant(day[0]) + day[1..]).ToUpperInvariant(), 8.5F, FontStyle.Bold, ShellPalette.TextSecondary, new Padding(0, 12, 0, 6)));
            }

            var title = ShellLabel(local.ToString("HH:mm", DutchCulture) + "   " + item.Title, 10F, FontStyle.Bold, ShellPalette.Text, new Padding(0, 4, 0, 0));
            var message = ShellLabel(item.Message, 9.75F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(46, 2, 0, 6));
            message.MaximumSize = new Size(width - 46, 0);
            _notificationsList.Controls.Add(title);
            _notificationsList.Controls.Add(message);
        }

        _notificationsList.ResumeLayout(true);
        _notificationsSeenUtc = DateTimeOffset.UtcNow;
        RefreshNotificationBadge();
    }

    private void RefreshNotificationBadge()
    {
        try
        {
            var items = NotificationHistoryStore.ReadRecent(AppRuntime.GetSettingsDirectory(), DateTimeOffset.UtcNow, TimeSpan.FromDays(1), 50);
            var unseen = _mainTabs?.SelectedTab == _notificationsTabPage ? 0 : items.Count(n => n.At > _notificationsSeenUtc);
            if (unseen != _shellBellCount)
            {
                _shellBellCount = unseen;
                _shellBellButton?.Invalidate();
                if (_shellBellButton is not null) _shellBellButton.AccessibleName = unseen > 0 ? $"Meldingen, {unseen} nieuw" : "Meldingen";
            }
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Meldingenteller bijwerken mislukt", ex);
        }
    }

    // ---------- Instellingen (overzicht) ----------

    // Het overzicht is de nieuwe pagina Instellingen (MainForm.SettingsPage).
    private TabPage BuildSettingsHubPage() => BuildSettingsPage();

    // Na de oude opmaak in de constructor: de knop staat nu als gewone knop op het overzicht onder Instellingen.
    private void StyleShellSettingsButton()
    {
        _settingsButton.Text = "Instellingen openen";
        _settingsButton.Font = ShellFont("Segoe UI", 9.75F, FontStyle.Bold, GraphicsUnit.Point);
        _settingsButton.AutoSize = true;
        _settingsButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _settingsButton.MinimumSize = new Size(0, 40);
        _settingsButton.Padding = new Padding(12, 0, 12, 0);
        _settingsButton.Margin = new Padding(8, 0, 0, 0);
        _settingsButton.BackColor = ShellPalette.Accent;
        _settingsButton.ForeColor = Color.White;
        _settingsButton.FlatStyle = FlatStyle.Flat;
        _settingsButton.FlatAppearance.BorderSize = 0;
        _settingsButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(7, 63, 83);
    }

    // Na de start: de lijst van Overleggen inlezen (voor "Wacht op jou" en het getal in het menu) en elke vijf minuten Vandaag
    // en de meldingenteller bijwerken.
    private void StartShellBackgroundRefresh()
    {
        _todayRefreshTimer.Start();
        RefreshNotificationBadge();
        RefreshShellSystemStatus();
        _ = RefreshMeetingsAsync();
    }
}
