using System.Drawing;
using System.Globalization;
using LocalCaptureAgent.Controls;
using LocalCaptureAgent.Models;
using LocalCaptureAgent.Services;

namespace LocalCaptureAgent;

// De pagina Overleggen (fase 2 van de nieuwe interface). Links de lijst met zoeken en filters (wacht op jou,
// periode), rechts het gekozen overleg met de uitgeschreven tekst, en een menu met de bestaande acties uit de oude bibliotheek. Die acties worden hergebruikt: ze lezen de selectie via
// GetSelectedLibraryEntry, die tijdens de actie het hier gekozen overleg teruggeeft (_libraryEntryOverride).
public sealed partial class MainForm
{
    private const string ShellNavLibrary = "bibliotheek";

    private TabPage? _meetingsTabPage;
    private TextBox? _meetingsSearchBox;
    private CheckBox? _meetingsWaitingToggle;
    private ComboBox? _meetingsPeriodCombo;
    private ShellRowList? _meetingsList;
    private Label? _meetingsListState;
    private Panel? _meetingsDetailHost;
    private List<MeetingListService.MeetingRow> _meetingsAll = new();
    private CancellationTokenSource? _meetingsLoadCancellation;
    private readonly System.Windows.Forms.Timer _meetingsSearchTimer = new() { Interval = 400 };
    private DateTimeOffset _meetingsLoadedAt = DateTimeOffset.MinValue;
    private string _meetingsLoadedQuery = string.Empty;
    // De stand van de laatste lijst zonder zoekopdracht, apart bewaard: een zoekopdracht daarna zet het getal niet terug.
    private int _meetingsWaitingCount;
    private HashSet<string> _meetingsWaitingKeys = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _meetingsWaitingLoadedAt = DateTimeOffset.MinValue;
    private string _meetingsDetailTab = "tekst";
    private string? _meetingsSelectedDirectory;
    private CancellationTokenSource? _meetingsDetailCancellation;
    private TranscriptLibraryEntry? _libraryEntryOverride;

    private TabPage BuildMeetingsPage()
    {
        var page = ShellNewPage("Overleggen");
        page.AutoScroll = false;
        _meetingsTabPage = page;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent, Padding = new Padding(4, 4, 12, 4), Margin = new Padding(0) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(ShellLabel("Overleggen", 18F, FontStyle.Bold, ShellPalette.Text, new Padding(0, 0, 0, 12)), 0, 0);
        root.Controls.Add(BuildMeetingsFilterBar(), 0, 1);

        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0) };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var listCard = new ShellCard { Dock = DockStyle.Fill, Padding = new Padding(2, 8, 2, 8), Margin = new Padding(0, 0, 16, 8) };
        _meetingsList = new ShellRowList { Dock = DockStyle.Fill, AccessibleName = "Lijst met overleggen" };
        _meetingsList.ItemHeight = LogicalToDeviceUnits(62);
        _meetingsList.Painter = PaintMeetingRow;
        _meetingsList.SelectedIndexChanged += (_, _) => ShowSelectedMeeting();
        _meetingsListState = ShellLabel(string.Empty, 10F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(16, 12, 16, 0));
        _meetingsListState.Dock = DockStyle.Top;
        listCard.Controls.Add(_meetingsList);
        listCard.Controls.Add(_meetingsListState);
        columns.Controls.Add(listCard, 0, 0);

        var detailCard = new ShellCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        _meetingsDetailHost = new Panel { Dock = DockStyle.Fill, BackColor = ShellPalette.Surface };
        detailCard.Controls.Add(_meetingsDetailHost);
        columns.Controls.Add(detailCard, 1, 0);
        root.Controls.Add(columns, 0, 2);

        // De oude bibliotheek staat onder Instellingen, Geavanceerd.

        page.Controls.Add(root);
        _meetingsSearchTimer.Tick += (_, _) =>
        {
            _meetingsSearchTimer.Stop();
            _ = RefreshMeetingsAsync();
        };
        RenderMeetingDetail(null);
        return page;
    }

    // Het getal bij Overleggen in het menu, de regel op Vandaag en het systeemvak volgen de lijst van Overleggen (zonder zoekopdracht).
    private void UpdateMeetingsNavCount()
    {
        if (_meetingsWaitingLoadedAt == DateTimeOffset.MinValue) return;
        RenderToday();
    }

    private Control BuildMeetingsFilterBar()
    {
        var bar = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 12), Dock = DockStyle.Top };
        _meetingsSearchBox = new TextBox
        {
            PlaceholderText = "Zoek op titel, persoon of wat er gezegd is",
            Font = ShellFont("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point),
            Width = LogicalToDeviceUnits(310),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 4, 10, 4),
            AccessibleName = "Zoeken in overleggen",
            AccessibleDescription = "skip-ui-localization",
        };
        _meetingsSearchBox.TextChanged += (_, _) =>
        {
            _meetingsSearchTimer.Stop();
            _meetingsSearchTimer.Start();
        };
        // Een zichtbaar label: de tekst in het vak verdwijnt zodra je typt.
        bar.Controls.Add(ShellLabel("Zoeken", 10F, FontStyle.Bold, ShellPalette.Text, new Padding(0, 9, 8, 0)));
        bar.Controls.Add(_meetingsSearchBox);

        _meetingsWaitingToggle = new CheckBox
        {
            Text = "Wacht op jou",
            Appearance = Appearance.Button,
            AutoSize = true,
            MinimumSize = new Size(LogicalToDeviceUnits(170), LogicalToDeviceUnits(36)),
            Padding = new Padding(10, 0, 10, 0),
            FlatStyle = FlatStyle.Flat,
            Font = ShellFont("Segoe UI", 9.75F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 2, 10, 2),
            Cursor = Cursors.Hand,
            BackColor = ShellPalette.Surface,
            ForeColor = ShellPalette.Text,
            AccessibleDescription = "skip-ui-localization",
        };
        _meetingsWaitingToggle.FlatAppearance.BorderColor = Color.FromArgb(185, 194, 204);
        _meetingsWaitingToggle.FlatAppearance.CheckedBackColor = ShellPalette.AccentSoft;
        _meetingsWaitingToggle.CheckedChanged += (_, _) =>
        {
            _meetingsWaitingToggle.ForeColor = _meetingsWaitingToggle.Checked ? ShellPalette.Accent : ShellPalette.Text;
            _meetingsWaitingToggle.FlatAppearance.BorderColor = _meetingsWaitingToggle.Checked ? ShellPalette.Accent : Color.FromArgb(185, 194, 204);
            ApplyMeetingsFilter();
        };
        bar.Controls.Add(_meetingsWaitingToggle);

        ComboBox Combo(string name, string[] items, int selected, int width)
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = ShellFont("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
                Width = LogicalToDeviceUnits(width),
                Margin = new Padding(0, 6, 10, 4),
                AccessibleName = name,
                AccessibleDescription = "skip-ui-localization",
            };
            combo.Items.AddRange(items);
            combo.SelectedIndex = selected;
            combo.SelectedIndexChanged += (_, _) => ApplyMeetingsFilter();
            bar.Controls.Add(combo);
            return combo;
        }

        _meetingsPeriodCombo = Combo("Periode", new[] { "Afgelopen 7 dagen", "Afgelopen 30 dagen", "Afgelopen 90 dagen", "Alles" }, 1, 170);
        // Een opname van elders (telefoon, dictafoon) toevoegen; die wordt daarna net zo uitgeschreven.
        var import = ShellButton("Audio importeren", false, async (_, _) =>
        {
            await RunBusyUiActionAsync("Audio importeren", ImportAudioFromDialogAsync);
            await RefreshMeetingsAsync();
        });
        import.Anchor = AnchorStyles.Left;
        import.MinimumSize = new Size(0, LogicalToDeviceUnits(36));
        import.Margin = new Padding(0, 2, 10, 2);
        bar.Controls.Add(import);
        // Bij een smal venster sprong de laatste keuzelijst naar een tweede regel; het zoekvak geeft nu ruimte (tot 150).
        var fitting = false;
        void FitSearchBox()
        {
            if (fitting || _meetingsSearchBox is null) return;
            // De breedte die de balk krijgt: van het vak eromheen (dat meet de balk voordat het hem plaatst), anders de eigen.
            var barWidth = bar.Parent is { } parent ? parent.ClientSize.Width - parent.Padding.Horizontal - bar.Margin.Horizontal : bar.ClientSize.Width;
            if (barWidth <= 0) return;
            fitting = true;
            try
            {
                var others = 0;
                foreach (Control control in bar.Controls)
                {
                    if (control != _meetingsSearchBox) others += control.Width + control.Margin.Horizontal;
                }

                var available = barWidth - bar.Padding.Horizontal - others - _meetingsSearchBox.Margin.Horizontal - LogicalToDeviceUnits(4);
                var width = Math.Clamp(available, LogicalToDeviceUnits(150), LogicalToDeviceUnits(310));
                if (_meetingsSearchBox.Width != width) _meetingsSearchBox.Width = width;
            }
            finally
            {
                fitting = false;
            }
        }

        bar.Layout += (_, _) => FitSearchBox();
        bar.SizeChanged += (_, _) => FitSearchBox();
        // Voor het vak eromheen de balk meet: dan klopt de hoogte in dezelfde ronde (geen lege tweede regel).
        bar.ParentChanged += (_, _) =>
        {
            if (bar.Parent is { } host) host.Layout += (_, _) => FitSearchBox();
        };
        return bar;
    }

    // Bij openen van de pagina: opnieuw inlezen als het ouder is dan een minuut (de werker kan intussen iets hebben afgerond).
    private void OnMeetingsPageShown()
    {
        if (DateTimeOffset.Now - _meetingsLoadedAt > TimeSpan.FromMinutes(1) || _meetingsAll.Count == 0) _ = RefreshMeetingsAsync();
        else ApplyMeetingsFilter();
    }

    // Vanaf Vandaag: alleen wat op de gebruiker wacht.
    private void ShowWaitingMeetings()
    {
        if (_meetingsWaitingToggle is not null) _meetingsWaitingToggle.Checked = true;
        if (_meetingsPeriodCombo is not null) _meetingsPeriodCombo.SelectedIndex = 3;
        NavigateShell(ShellNavMeetings);
    }

    private async Task RefreshMeetingsAsync()
    {
        if (_meetingsList is null || IsDisposed) return;
        _meetingsLoadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _meetingsLoadCancellation = cancellation;
        var query = _meetingsSearchBox?.Text.Trim() ?? string.Empty;
        var settings = _appSettings;
        // Alleen tijdens een opname (en het afronden ervan): daarna wijst _manifest nog naar het afgeronde overleg.
        var activeDirectory = _session is not null || _stopFinalizeInProgress ? _manifest?.OutputDirectory : null;
        if (_meetingsAll.Count == 0 && _meetingsListState is not null) _meetingsListState.Text = "Bezig met laden...";
        try
        {
            var rows = await Task.Run(() => MeetingListService.Load(settings, query, activeDirectory, DateTime.Now, cancellation.Token), cancellation.Token);
            if (IsDisposed || cancellation.IsCancellationRequested) return;
            void Show()
            {
                if (IsDisposed || cancellation.IsCancellationRequested) return;
                _meetingsAll = rows;
                _meetingsLoadedAt = DateTimeOffset.Now;
                _meetingsLoadedQuery = query;
                if (query.Length == 0)
                {
                    _meetingsWaitingCount = rows.Count(r => r.Waiting);
                    _meetingsWaitingKeys = MeetingListService.WaitingKeys(rows);
                    _meetingsWaitingLoadedAt = _meetingsLoadedAt;
                }

                ApplyMeetingsFilter();
                UpdateMeetingsNavCount();
            }

            // Altijd op de schermthread (ook als de aanroeper geen schermcontext had).
            if (InvokeRequired) BeginInvoke(new Action(Show));
            else Show();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Overleggen laden mislukt", ex);
            if (_meetingsListState is not null) _meetingsListState.Text = "Laden lukte niet; de details staan in het logboek.";
        }
        finally
        {
            if (_meetingsLoadCancellation == cancellation) _meetingsLoadCancellation = null;
            cancellation.Dispose();
        }
    }

    private void ApplyMeetingsFilter()
    {
        if (_meetingsList is null || _meetingsListState is null) return;
        var org = string.Empty;
        var days = _meetingsPeriodCombo?.SelectedIndex switch { 0 => 7, 1 => 30, 2 => 90, _ => 0 };
        var waitingOnly = _meetingsWaitingToggle?.Checked == true;
        var since = DateTime.Now.Date.AddDays(-days);
        // Wacht op jou telt en toont alles wat wacht, ongeacht de periode (zoals het getal in het menu).
        var inOrganization = _meetingsAll
            .Where(r => org.Length == 0 || string.Equals(r.Organization, org, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var waitingCount = inOrganization.Count(r => r.Waiting);
        var rows = waitingOnly ? inOrganization.Where(r => r.Waiting).ToList() : inOrganization.Where(r => days == 0 || r.When >= since).ToList();
        if (_meetingsWaitingToggle is not null) _meetingsWaitingToggle.Text = $"Wacht op jou ({waitingCount})";

        var keep = _meetingsSelectedDirectory;
        _meetingsList.BeginUpdate();
        _meetingsList.Items.Clear();
        foreach (var row in rows) _meetingsList.Items.Add(row);
        _meetingsList.EndUpdate();
        _meetingsListState.Text = rows.Count > 0 ? string.Empty
            : _meetingsAll.Count == 0 ? (_meetingsLoadedAt == DateTimeOffset.MinValue ? "Bezig met laden..." : "Nog geen overleggen opgenomen.")
            : waitingOnly ? "Niets vraagt je aandacht. Mooi."
            : _meetingsLoadedQuery.Length > 0 ? "Niets gevonden voor deze zoekopdracht."
            : "Geen overleggen in deze periode.";
        _meetingsListState.Visible = _meetingsListState.Text.Length > 0;

        var index = keep is null ? -1 : rows.FindIndex(r => string.Equals(r.Entry.DirectoryPath, keep, StringComparison.OrdinalIgnoreCase));
        if (index < 0 && rows.Count > 0) index = 0;
        if (index >= 0) _meetingsList.SelectedIndex = index;
        else RenderMeetingDetail(null);
    }

    private MeetingListService.MeetingRow? SelectedMeetingRow()
        => _meetingsList?.SelectedItem as MeetingListService.MeetingRow;

    private void PaintMeetingRow(Graphics g, Rectangle bounds, object item, bool selected)
    {
        if (item is not MeetingListService.MeetingRow row) return;
        var pad = LogicalToDeviceUnits(16);
        var titleFont = ShellFont("Segoe UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
        var smallFont = ShellFont("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        var statusFont = ShellFont("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        var statusColor = row.StatusKind switch
        {
            "rec" or "bad" => ShellPalette.Bad,
            "busy" => ShellPalette.Busy,
            "wait" => ShellPalette.Wait,
            "ok" => ShellPalette.Ok,
            _ => ShellPalette.TextSecondary,
        };

        // Rechts de stand met een stip; links titel en tijd.
        var statusSize = TextRenderer.MeasureText(g, row.StatusText, statusFont, Size.Empty, TextFormatFlags.NoPadding);
        var dot = LogicalToDeviceUnits(8);
        var statusX = bounds.Right - pad - statusSize.Width;
        var midY = bounds.Top + bounds.Height / 2;
        using (var dotBrush = new SolidBrush(statusColor))
        {
            g.FillEllipse(dotBrush, statusX - dot - LogicalToDeviceUnits(6), midY - dot / 2, dot, dot);
        }

        TextRenderer.DrawText(g, row.StatusText, statusFont, new Point(statusX, midY - statusSize.Height / 2), statusColor, TextFormatFlags.NoPadding);
        var textRight = statusX - dot - LogicalToDeviceUnits(16);
        var lineHeight = titleFont.Height;
        var top = bounds.Top + (bounds.Height - lineHeight - smallFont.Height - LogicalToDeviceUnits(4)) / 2;
        TextRenderer.DrawText(g, row.Title, titleFont, new Rectangle(bounds.Left + pad, top, Math.Max(10, textRight - bounds.Left - pad), lineHeight), ShellPalette.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        var whenSize = TextRenderer.MeasureText(g, row.WhenText, smallFont, Size.Empty, TextFormatFlags.NoPadding);
        var secondY = top + lineHeight + LogicalToDeviceUnits(4);
        TextRenderer.DrawText(g, row.WhenText, smallFont, new Point(bounds.Left + pad, secondY), ShellPalette.TextSecondary, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        using var line = new Pen(ShellPalette.LineSoft);
        g.DrawLine(line, bounds.Left + pad, bounds.Bottom - 1, bounds.Right - pad, bounds.Bottom - 1);
    }

    // ---------- Het gekozen overleg ----------

    private void ShowSelectedMeeting()
    {
        var row = SelectedMeetingRow();
        _meetingsSelectedDirectory = row?.Entry.DirectoryPath;
        RenderMeetingDetail(row);
    }

    private void RenderMeetingDetail(MeetingListService.MeetingRow? row)
    {
        var host = _meetingsDetailHost;
        if (host is null) return;
        _meetingsDetailCancellation?.Cancel();
        host.SuspendLayout();
        ShellClearControls(host);
        if (row is null)
        {
            host.Controls.Add(ShellLabel("Kies links een overleg.", 10.5F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(4)));
            host.ResumeLayout(true);
            return;
        }

        var entry = row.Entry;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent, Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Kop: titel en wanneer; rechts de belangrijkste actie en het menu met de overige acties.
        var header = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = true, Dock = DockStyle.Top, BackColor = Color.Transparent, Margin = new Padding(0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titles = ShellStack();
        titles.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        ShellStackAdd(titles, ShellLineLabel(row.Title, 14F, FontStyle.Bold, ShellPalette.Text, 32));
        var when = row.When.ToString("dddd d MMMM HH:mm", DutchCulture) + (MeetingListService.Duration(entry) is { Length: > 0 } d ? ", " + d : string.Empty)
                   + (entry.Participants.Count > 0 ? $", {entry.Participants.Count} deelnemer{(entry.Participants.Count == 1 ? string.Empty : "s")}" : string.Empty);
        var whenRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 0), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
        whenRow.Controls.Add(ShellLabel(char.ToUpperInvariant(when[0]) + when[1..], 9.75F, FontStyle.Regular, ShellPalette.TextSecondary, new Padding(2, 2, 0, 0)));
        ShellStackAdd(titles, whenRow);
        header.Controls.Add(titles, 0, 0);
        if (row.Waiting && CanRetranscribe(entry))
        {
            header.Controls.Add(ShellButton("Opnieuw uitschrijven", true, async (_, _) => await RunMeetingActionAsync(entry, RetranscribeSelectedLibrarySessionAsync)), 1, 0);
        }

        var more = ShellButton("...", false, (_, _) => { });
        more.AccessibleName = "Meer acties";
        more.Click += (_, _) => BuildMeetingMenu(entry).Show(more, new Point(0, more.Height));
        header.Controls.Add(more, 2, 0);
        layout.Controls.Add(header, 0, 0);

        // Tabbladen als knoppen; de inhoud eronder.
        var tabs = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Color.Transparent, Margin = new Padding(0, 14, 0, 8) };
        var content = new Panel { Dock = DockStyle.Fill, BackColor = ShellPalette.Surface, Margin = new Padding(0) };
        foreach (var (key, text) in new[] { ("tekst", "Uitgeschreven tekst") })
        {
            var tab = new RadioButton
            {
                Text = text,
                Appearance = Appearance.Button,
                AutoSize = true,
                MinimumSize = new Size(0, LogicalToDeviceUnits(36)),
                Padding = new Padding(8, 0, 8, 0),
                FlatStyle = FlatStyle.Flat,
                Font = ShellFont("Segoe UI", 9.75F, key == _meetingsDetailTab ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point),
                TextAlign = ContentAlignment.MiddleCenter,
                Checked = key == _meetingsDetailTab,
                Margin = new Padding(0, 0, 4, 0),
                Cursor = Cursors.Hand,
                ForeColor = key == _meetingsDetailTab ? ShellPalette.Accent : ShellPalette.Text,
                BackColor = ShellPalette.Surface,
                AccessibleDescription = "skip-ui-localization",
            };
            tab.FlatAppearance.BorderSize = 0;
            tab.FlatAppearance.CheckedBackColor = ShellPalette.AccentSoft;
            tab.CheckedChanged += (_, _) =>
            {
                tab.Font = ShellFont("Segoe UI", 9.75F, tab.Checked ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
                tab.ForeColor = tab.Checked ? ShellPalette.Accent : ShellPalette.Text;
                if (!tab.Checked) return;
                _meetingsDetailTab = key;
                FillMeetingDetailContent(content, entry);
            };
            tabs.Controls.Add(tab);
        }

        layout.Controls.Add(tabs, 0, 1);
        layout.Controls.Add(content, 0, 2);
        if (row.Waiting)
        {
            var notice = new Label
            {
                Text = CanRetranscribe(entry) ? "Hier ging iets mis. Met Opnieuw uitschrijven probeert Captivox het nog een keer." : "Hier ging iets mis; de details staan in de map van de opname.",
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = ShellLineHeight(10F, FontStyle.Regular, 40),
                Padding = new Padding(12, 0, 12, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = ShellPalette.WaitSoft,
                ForeColor = Color.FromArgb(61, 42, 10),
                Font = ShellFont("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
                Margin = new Padding(0, 10, 0, 0),
                AccessibleDescription = "skip-ui-localization",
            };
            layout.Controls.Add(notice, 0, 3);
        }

        host.Controls.Add(layout);
        host.ResumeLayout(true);
        FillMeetingDetailContent(content, entry);
    }

    // De inhoud van een tabblad uit de bestaande bibliotheekopbouw (op de achtergrond).
    private void FillMeetingDetailContent(Panel content, TranscriptLibraryEntry entry)
    {
        _meetingsDetailCancellation?.Cancel();
        ShellClearControls(content);

        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = ShellPalette.Surface,
            ForeColor = ShellPalette.Text,
            Font = ShellFont("Segoe UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point),
            Dock = DockStyle.Fill,
            Text = "Bezig met laden...",
            AccessibleName = "Uitgeschreven tekst",
            AccessibleDescription = "skip-ui-localization",
        };
        content.Controls.Add(text);
        var cancellation = new CancellationTokenSource();
        _meetingsDetailCancellation = cancellation;
        _ = Task.Run(() => BuildLibraryDetailSnapshot(entry, cancellation.Token), cancellation.Token).ContinueWith(task =>
        {
            if (cancellation.IsCancellationRequested || IsDisposed || text.IsDisposed) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (text.IsDisposed || cancellation.IsCancellationRequested) return;
                    if (task.IsFaulted)
                    {
                        AppRuntime.LogException("Overleg laden mislukt", task.Exception?.GetBaseException() ?? new InvalidOperationException("onbekend"));
                        text.Text = "Laden lukte niet; de details staan in het logboek.";
                        return;
                    }

                    var snapshot = task.Result;
                    var value = snapshot.TranscriptPreview;
                    text.Text = string.IsNullOrWhiteSpace(value) ? "Nog niets beschikbaar. Is het overleg al uitgeschreven?" : value.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
                    text.SelectionStart = 0;
                }));
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
            }
        }, TaskScheduler.Default);
    }

    // Het menu met de overige acties: de bestaande functies uit de oude bibliotheek, op dit overleg.
    private ContextMenuStrip BuildMeetingMenu(TranscriptLibraryEntry entry)
    {
        var menu = new ContextMenuStrip { Font = ShellFont("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point), ShowImageMargin = false };
        menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));
        void Add(ToolStripItemCollection items, string text, Func<Task> action, bool enabled = true)
        {
            var item = new ToolStripMenuItem(text) { Enabled = enabled, AccessibleDescription = "skip-ui-localization" };
            item.Click += async (_, _) => await RunMeetingActionAsync(entry, action);
            items.Add(item);
        }

        var hasTranscript = File.Exists(entry.TranscriptJsonPath ?? Path.Combine(entry.DirectoryPath, "transcript.json"));
        var hasTranscriptText = !string.IsNullOrWhiteSpace(entry.TranscriptTextPath) && File.Exists(entry.TranscriptTextPath);
        Add(menu.Items, "Map openen", () => { OpenSelectedLibrarySessionFolder(); return Task.CompletedTask; });
        Add(menu.Items, "Uitgeschreven tekst opslaan", () => { ExportSelectedLibraryTranscript("txt"); return Task.CompletedTask; }, hasTranscriptText || hasTranscript);
        Add(menu.Items, "Ondertitels opslaan (voor de meeste videospelers)", () => { ExportSelectedLibraryTranscript("srt"); return Task.CompletedTask; }, hasTranscript);
        Add(menu.Items, "Ondertitels opslaan (voor webspelers)", () => { ExportSelectedLibraryTranscript("vtt"); return Task.CompletedTask; }, hasTranscript);
        menu.Items.Add(new ToolStripSeparator());
        Add(menu.Items, "Titel wijzigen", () => { EditSelectedLibrarySessionTitle(); return Task.CompletedTask; });
        Add(menu.Items, "Opnieuw uitschrijven", RetranscribeSelectedLibrarySessionAsync, CanRetranscribe(entry));
        menu.Items.Add(new ToolStripSeparator());
        var delete = new ToolStripMenuItem("Naar de Prullenbak") { ForeColor = ShellPalette.Bad, AccessibleDescription = "skip-ui-localization" };
        delete.Click += async (_, _) => await RunMeetingActionAsync(entry, DeleteSelectedLibrarySessionAsync);
        menu.Items.Add(delete);
        return menu;
    }

    // Een bestaande bibliotheekactie op dit overleg. Die acties lezen de selectie een keer, meteen bij de start (voor de eerste
    // await); de selectie wijst alleen dan naar dit overleg en is daarna meteen weer gewoon, zodat een actie die lang
    // wacht geen andere actie op het verkeerde overleg kan laten uitkomen. Daarna de lijst opnieuw.
    private async Task RunMeetingActionAsync(TranscriptLibraryEntry entry, Func<Task> action)
    {
        try
        {
            Task running;
            _libraryEntryOverride = entry;
            try
            {
                running = action();
            }
            finally
            {
                _libraryEntryOverride = null;
            }

            await running;
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Actie op overleg mislukt", ex);
            ShowOwnedMessage(this, ex.Message, "Dat lukte niet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        await RefreshMeetingsAsync();
    }
}
