using System.Drawing;
using System.Text;
using System.Text.Json;
using LocalCaptureAgent.Controls;
using LocalCaptureAgent.Models;
using LocalCaptureAgent.Services;

namespace LocalCaptureAgent;

public sealed partial class MainForm
{
    private readonly ComboBox _libraryContextFilterComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ComboBox _librarySubcontextFilterComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ComboBox _libraryMeetingTypeFilterComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly ComboBox _libraryTagFilterComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly Button _libraryEditTagsButton = new() { Text = "Beheer indeling", Width = 150, Height = 32 };
    private readonly Button _libraryResetFiltersButton = new() { Text = "Reset filters", Width = 124, Height = 32 };
    private readonly Label _librarySessionSelectionSummaryLabel = new() { AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = Color.FromArgb(97, 107, 121) };
    private readonly Label _libraryFilterSummaryLabel = new() { AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = Color.FromArgb(97, 107, 121), Text = "Alle sessies zichtbaar." };
    private readonly Label _librarySessionOrganizationValueLabel = new() { AutoSize = true, MaximumSize = new Size(260, 0), Text = "Nog niet toegewezen" };
    private readonly Label _librarySessionProjectValueLabel = new() { AutoSize = true, MaximumSize = new Size(260, 0), Text = "Nog niet toegewezen" };
    private readonly Label _librarySessionMeetingTypeValueLabel = new() { AutoSize = true, MaximumSize = new Size(260, 0), Text = "Nog niet toegewezen" };
    private readonly Label _librarySessionCalendarSubjectValueLabel = new() { AutoSize = true, MaximumSize = new Size(320, 0), Text = "Nog geen agenda-match" };
    private readonly Label _librarySessionParticipantsValueLabel = new() { AutoSize = true, MaximumSize = new Size(320, 0), Text = "Nog geen deelnemers" };
    private readonly Label _librarySessionAgendaStatusValueLabel = new() { AutoSize = true, MaximumSize = new Size(320, 0), Text = "Nog geen agenda-status" };
    private readonly Label _librarySessionTopicsValueLabel = new() { AutoSize = true, MaximumSize = new Size(320, 0), Text = "Geen onderwerpen" };
    private readonly Label _librarySessionRuntimeStatusValueLabel = new() { AutoSize = true, MaximumSize = new Size(420, 0), Text = "Nog geen status" };
    private readonly Label _libraryArtifactStatusLabel = new() { AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = Color.FromArgb(97, 107, 121), Text = "Nog geen artefactstatus" };
    private readonly ToolStripSeparator _libraryContextAssignmentSeparator = new();
    private readonly ToolStripMenuItem _libraryContextAssignOrganizationItem = new("Toewijzen aan organisatie");
    private readonly ToolStripMenuItem _libraryContextAssignProjectItem = new("Toewijzen aan project / dossier");
    private readonly ToolStripMenuItem _libraryContextAssignMeetingTypeItem = new("Toewijzen aan overlegtype");
    private readonly ToolStripMenuItem _libraryContextAssignTopicsItem = new("Onderwerpen toewijzen…");
    private readonly TextBox _libraryTranscriptPreviewTextBox = CreateLibraryPreviewTextBox();
    private readonly TextBox _libraryImportStatusTextBox = CreateLibraryPreviewTextBox();
    private readonly LevelMeterControl _headerMicLevelMeter = new();
    private readonly LevelMeterControl _headerSystemLevelMeter = new();
    private TabControl? _libraryDetailTabs;
    private bool _suppressLibraryTagFilterChanged;
    private bool _suppressLibraryContextFilterChanged;

    private void ConfigureLibraryEnhancements()
    {
        StyleButton(_libraryEditTagsButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(_libraryResetFiltersButton, Color.FromArgb(245, 247, 250), Color.FromArgb(234, 239, 244), Color.FromArgb(67, 78, 92));
        _libraryContextFilterComboBox.DropDownWidth = 220;
        _librarySubcontextFilterComboBox.DropDownWidth = 220;
        _libraryMeetingTypeFilterComboBox.DropDownWidth = 200;
        _libraryTagFilterComboBox.DropDownWidth = 220;
        _libraryContextFilterComboBox.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressLibraryContextFilterChanged)
            {
                return;
            }

            _appSettings.Ui.LibrarySelectedContextFilter = GetSelectedLibraryContextFilter();
            _appSettings.Ui.LibrarySelectedSubcontextFilter = string.Empty;
            await RunLibraryUiActionAsync("Bibliotheek filteren", ct => RefreshLibraryAsync(ct));
        };
        _librarySubcontextFilterComboBox.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressLibraryContextFilterChanged)
            {
                return;
            }

            _appSettings.Ui.LibrarySelectedSubcontextFilter = GetSelectedLibrarySubcontextFilter();
            await RunLibraryUiActionAsync("Bibliotheek filteren", ct => RefreshLibraryAsync(ct));
        };
        _libraryMeetingTypeFilterComboBox.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressLibraryContextFilterChanged)
            {
                return;
            }

            _appSettings.Ui.LibrarySelectedMeetingTypeFilter = GetSelectedLibraryMeetingTypeFilter();
            await RunLibraryUiActionAsync("Bibliotheek filteren", ct => RefreshLibraryAsync(ct));
        };
        _libraryTagFilterComboBox.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressLibraryTagFilterChanged)
            {
                return;
            }

            _appSettings.Ui.LibrarySelectedTagFilter = GetSelectedLibraryTagFilter();
            await RunLibraryUiActionAsync("Bibliotheek filteren", ct => RefreshLibraryAsync(ct));
        };
        _libraryEditTagsButton.Click += (_, _) => ShowLibraryTagsDialog();
        _libraryResetFiltersButton.Click += async (_, _) => await ResetLibraryFiltersAsync();
        _libraryContextMenu.Opening += LibraryContextMenu_Opening;
        _libraryContextAssignMeetingTypeItem.Click += (_, _) => ShowAssignMeetingTypeDialog();
        _libraryContextAssignTopicsItem.Click += (_, _) => ShowAssignTopicsDialog();
        SetUiToolTip(_libraryEditTagsButton, "Beheer organisaties, projecten/dossiers, overlegtypen en onderwerpen voor de bibliotheek.");
        SetUiToolTip(_libraryResetFiltersButton, "Zet organisatie-, project-, overlegtype- en onderwerpfilters terug naar alle sessies.");
        SetUiToolTip(_libraryOpenTranscriptButton, "Open het transcript van de geselecteerde sessie.");
        SetUiToolTip(_libraryOpenFolderButton, "Open de sessiemap van de geselecteerde sessie.");
        SetUiToolTip(_libraryCopyTranscriptButton, "Kopieer het transcript naar het klembord.");
        SetUiToolTip(_libraryDeleteButton, "Verwijder de geselecteerde sessie na bevestiging.");

        if (!_libraryContextMenu.Items.Contains(_libraryContextAssignmentSeparator))
        {
            _libraryContextMenu.Items.Insert(Math.Max(0, _libraryContextMenu.Items.Count - 1), _libraryContextAssignmentSeparator);
            _libraryContextMenu.Items.Insert(Math.Max(0, _libraryContextMenu.Items.Count - 1), _libraryContextAssignOrganizationItem);
            _libraryContextMenu.Items.Insert(Math.Max(0, _libraryContextMenu.Items.Count - 1), _libraryContextAssignProjectItem);
            _libraryContextMenu.Items.Insert(Math.Max(0, _libraryContextMenu.Items.Count - 1), _libraryContextAssignMeetingTypeItem);
            _libraryContextMenu.Items.Insert(Math.Max(0, _libraryContextMenu.Items.Count - 1), _libraryContextAssignTopicsItem);
        }
    }

    private static TextBox CreateLibraryPreviewTextBox()
        => new()
        {
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.25F, FontStyle.Regular, GraphicsUnit.Point)
        };

    private Control BuildLibraryFilterRow()
    {
        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 5,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = CardBackground,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var searchRow = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = CardBackground,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 188));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 196));
        searchRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _librarySearchTextBox.Margin = new Padding(0, 0, 10, 8);
        _libraryContextFilterComboBox.Margin = new Padding(0, 0, 10, 8);
        _librarySubcontextFilterComboBox.Margin = new Padding(0, 0, 0, 8);
        _librarySearchTextBox.Dock = DockStyle.Fill;
        _libraryContextFilterComboBox.Dock = DockStyle.Fill;
        _librarySubcontextFilterComboBox.Dock = DockStyle.Fill;
        searchRow.Controls.Add(_librarySearchTextBox, 0, 0);
        searchRow.Controls.Add(_libraryContextFilterComboBox, 1, 0);
        searchRow.Controls.Add(_librarySubcontextFilterComboBox, 2, 0);

        var actionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = CardBackground,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _libraryMeetingTypeFilterComboBox.Margin = new Padding(0, 0, 10, 8);
        _libraryTagFilterComboBox.Margin = new Padding(0, 0, 10, 8);
        _libraryEditTagsButton.Margin = new Padding(0, 0, 10, 8);
        _libraryResetFiltersButton.Margin = new Padding(0, 0, 0, 8);
        actionRow.Controls.Add(_libraryMeetingTypeFilterComboBox);
        actionRow.Controls.Add(_libraryTagFilterComboBox);
        actionRow.Controls.Add(_libraryEditTagsButton);
        actionRow.Controls.Add(_libraryResetFiltersButton);

        _libraryFilterSummaryLabel.Margin = new Padding(0, 0, 0, 2);

        root.Controls.Add(searchRow, 0, 0);
        root.Controls.Add(actionRow, 0, 1);
        root.Controls.Add(_libraryFilterSummaryLabel, 0, 2);
        return root;
    }

    private async Task ResetLibraryFiltersAsync()
    {
        _librarySearchTextBox.Text = string.Empty;
        _appSettings.Ui.LibrarySelectedContextFilter = string.Empty;
        _appSettings.Ui.LibrarySelectedSubcontextFilter = string.Empty;
        _appSettings.Ui.LibrarySelectedMeetingTypeFilter = string.Empty;
        _appSettings.Ui.LibrarySelectedTagFilter = string.Empty;

        _suppressLibraryContextFilterChanged = true;
        _suppressLibraryTagFilterChanged = true;
        try
        {
            if (_libraryContextFilterComboBox.Items.Count > 0)
            {
                _libraryContextFilterComboBox.SelectedIndex = 0;
            }

            if (_librarySubcontextFilterComboBox.Items.Count > 0)
            {
                _librarySubcontextFilterComboBox.SelectedIndex = 0;
            }

            if (_libraryMeetingTypeFilterComboBox.Items.Count > 0)
            {
                _libraryMeetingTypeFilterComboBox.SelectedIndex = 0;
            }

            if (_libraryTagFilterComboBox.Items.Count > 0)
            {
                _libraryTagFilterComboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            _suppressLibraryContextFilterChanged = false;
            _suppressLibraryTagFilterChanged = false;
        }

        await RunLibraryUiActionAsync("Bibliotheekfilters resetten", ct => RefreshLibraryAsync(ct));
    }

    private void UpdateLibraryFilterSummary(int totalEntries, int visibleEntries)
    {
        var parts = new List<string>();
        var context = GetSelectedLibraryContextFilter();
        var subcontext = GetSelectedLibrarySubcontextFilter();
        var tag = GetSelectedLibraryTagFilter();
        var query = _librarySearchTextBox.Text?.Trim();

        if (!string.IsNullOrWhiteSpace(query))
        {
            parts.Add(UiText.F("zoekterm = \"{0}\"", query));
        }

        if (!string.IsNullOrWhiteSpace(context))
        {
            parts.Add(UiText.F("organisatie = {0}", context));
        }

        if (!string.IsNullOrWhiteSpace(subcontext))
        {
            parts.Add(UiText.F("project / dossier = {0}", subcontext));
        }

        var meetingType = GetSelectedLibraryMeetingTypeFilter();
        if (!string.IsNullOrWhiteSpace(meetingType))
        {
            parts.Add(UiText.F("overlegtype = {0}", meetingType));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            parts.Add(UiText.F("onderwerp = {0}", tag));
        }

        if (parts.Count == 0)
        {
            _libraryFilterSummaryLabel.Text = UiText.F("Alle sessies zichtbaar. {0} sessie(s) in huidige weergave.", visibleEntries);
            _libraryFilterSummaryLabel.ForeColor = Color.FromArgb(97, 107, 121);
            return;
        }

        var detail = string.Join(" · ", parts);
        _libraryFilterSummaryLabel.Text = visibleEntries == 0 && totalEntries > 0
            ? UiText.F("Actieve filters: {0}. Er zijn nu 0 sessies zichtbaar, terwijl de bibliotheek {1} sessie(s) bevat. Gebruik Reset filters om alles weer te tonen.", detail, totalEntries)
            : UiText.F("Actieve filters: {0}. Zichtbaar: {1} van {2} sessie(s).", detail, visibleEntries, totalEntries);
        _libraryFilterSummaryLabel.ForeColor = visibleEntries == 0 && totalEntries > 0
            ? Color.FromArgb(146, 64, 14)
            : Color.FromArgb(97, 107, 121);
    }

    private Control BuildLibrarySessionMetadataPanel()
    {
        var panel = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = false,
            BackColor = CardBackground,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _librarySessionSelectionSummaryLabel.Margin = new Padding(0, 0, 0, 6);
        panel.Controls.Add(_librarySessionSelectionSummaryLabel, 0, 0);

        var summaryCard = CreateRecorderSurfaceCard(autoSize: false);
        summaryCard.Padding = new Padding(10);
        summaryCard.Margin = new Padding(0);
        summaryCard.Dock = DockStyle.Fill;

        var summaryLayout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = CardBackground,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

        AddMetadataSummaryRow(summaryLayout, 0, "Organisatie", _librarySessionOrganizationValueLabel);
        AddMetadataSummaryRow(summaryLayout, 1, "Project / dossier", _librarySessionProjectValueLabel);
        AddMetadataSummaryRow(summaryLayout, 2, "Overlegtype", _librarySessionMeetingTypeValueLabel);

        summaryCard.Controls.Add(summaryLayout);
        panel.Controls.Add(summaryCard, 0, 1);
        return panel;
    }

    private static void AddMetadataSummaryRow(TableLayoutPanel layout, int row, string label, Label valueLabel)
    {
        var caption = new Label
        {
            Text = label,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.25F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(39, 46, 56),
            Margin = new Padding(0, row == 0 ? 0 : 8, 12, 0),
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        valueLabel.Margin = new Padding(0, row == 0 ? 0 : 8, 0, 0);
        valueLabel.ForeColor = Color.FromArgb(39, 46, 56);
        valueLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        layout.Controls.Add(caption, 0, row);
        layout.Controls.Add(valueLabel, 1, row);
    }

    private Control BuildLibraryDetailPanel()
    {
        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = CardBackground,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 192F));

        var detailCard = CreateRecorderSurfaceCard(autoSize: false);
        detailCard.Dock = DockStyle.Fill;
        detailCard.Padding = new Padding(10);
        detailCard.Margin = new Padding(0);

        var detailLayout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = CardBackground,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        detailLayout.Controls.Add(new Label
        {
            Text = "Details van geselecteerde sessie",
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(39, 46, 56),
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);

        _libraryDetailTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Point(18, 6),
            Multiline = false,
            SizeMode = TabSizeMode.FillToRight
        };
        _libraryDetailTabs.TabPages.Add(CreateLibraryPreviewTab("Transcript", _libraryTranscriptPreviewTextBox));
        _libraryDetailTabs.TabPages.Add(CreateLibraryPreviewTab("Import inbox", _libraryImportStatusTextBox));
        detailLayout.Controls.Add(_libraryDetailTabs, 0, 1);

        detailCard.Controls.Add(detailLayout);
        root.Controls.Add(detailCard, 0, 0);

        _libraryArtifactStatusLabel.Margin = new Padding(0, 8, 0, 8);
        root.Controls.Add(_libraryArtifactStatusLabel, 0, 1);

        var actionBar = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Margin = new Padding(0, 0, 0, 0),
            BackColor = CardBackground,
            Padding = new Padding(0)
        };
        actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        actionBar.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
        actionBar.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
        actionBar.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34F));

        ConfigureGridActionButton(_libraryOpenTranscriptButton, 34, rightMargin: 0, bottomMargin: 0);
        ConfigureGridActionButton(_libraryOpenFolderButton, 34, rightMargin: 0, bottomMargin: 0);
        ConfigureGridActionButton(_libraryCopyTranscriptButton, 34, rightMargin: 0, bottomMargin: 0);
        ConfigureGridActionButton(_libraryDeleteButton, 34, rightMargin: 0, bottomMargin: 0);
        _libraryOpenTranscriptButton.Text = "Transcript";
        _libraryOpenFolderButton.Text = "Map";
        _libraryCopyTranscriptButton.Text = "Kopieer";
        _libraryDeleteButton.Text = "Verwijderen";
        foreach (var button in new[] { _libraryOpenTranscriptButton, _libraryOpenFolderButton, _libraryCopyTranscriptButton, _libraryDeleteButton })
        {
            button.Dock = DockStyle.Fill;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.MinimumSize = new Size(140, 34);
            button.Margin = new Padding(0, 0, 8, 8);
        }
        _libraryOpenFolderButton.Margin = new Padding(8, 0, 8, 8);
        _libraryCopyTranscriptButton.Margin = new Padding(8, 0, 0, 8);
        _libraryDeleteButton.Margin = new Padding(0, 8, 8, 8);

        actionBar.Controls.Add(_libraryOpenTranscriptButton, 0, 0);
        actionBar.Controls.Add(_libraryOpenFolderButton, 1, 0);
        actionBar.Controls.Add(_libraryCopyTranscriptButton, 2, 0);
        actionBar.Controls.Add(_libraryDeleteButton, 0, 1);
        root.Controls.Add(actionBar, 0, 2);
        return root;
    }

    private TabPage CreateLibraryPreviewTab(string title, Control content)
    {
        var tab = new BufferedTabPage(title)
        {
            BackColor = AppBackground,
            Padding = new Padding(10)
        };
        tab.Controls.Add(content);
        return tab;
    }

    private Control BuildHeaderSignalCardsRow()
    {
        var row = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = CardBackground,
            Margin = new Padding(0, 8, 0, 0),
            Padding = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.Controls.Add(CreateSignalStatusCard("Mic", _headerMicLevelMeter, _micLevelValueLabel), 0, 0);
        row.Controls.Add(CreateSignalStatusCard("Systeem", _headerSystemLevelMeter, _systemLevelValueLabel), 1, 0);
        return row;
    }

    private Control CreateSignalStatusCard(string label, LevelMeterControl meter, Label percentLabel)
    {
        var card = CreateRecorderSurfaceCard(autoSize: true);
        card.Padding = new Padding(12, 10, 12, 10);
        card.Margin = label == "Mic" ? new Padding(0, 0, 8, 0) : new Padding(8, 0, 0, 0);

        var layout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = CardBackground,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(39, 46, 56),
            Margin = new Padding(0, 0, 0, 6)
        }, 0, 0);

        var percentHost = new Label
        {
            Text = percentLabel.Text,
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(97, 107, 121),
            Margin = new Padding(8, 0, 0, 6)
        };
        percentLabel.TextChanged += (_, _) => percentHost.Text = percentLabel.Text;
        layout.Controls.Add(percentHost, 1, 0);

        var meterHost = CreateSignalHost(meter, 24, 220, dockFill: true, bottomMargin: 0);
        meterHost.Margin = new Padding(0);
        layout.Controls.Add(meterHost, 0, 1);
        layout.SetColumnSpan(meterHost, 2);

        card.Controls.Add(layout);
        return card;
    }

    private async void RefreshLibraryDetailPane()
    {
        var previousCts = _libraryDetailRefreshCts;
        previousCts?.Cancel();
        previousCts?.Dispose();

        var entry = GetSelectedLibraryEntry();
        if (entry is null)
        {
            _libraryDetailRefreshCts = null;
            _libraryTranscriptPreviewTextBox.Text = UiText.T("Selecteer een sessie om transcriptdetails te bekijken.");
            _libraryImportStatusTextBox.Text = "Selecteer een geimporteerde opname om recorded_at en imported_at te bekijken.";
            BindLibrarySessionMetadataControls(null);
            return;
        }

        var refreshId = Interlocked.Increment(ref _libraryDetailRefreshVersion);
        var cts = new CancellationTokenSource();
        _libraryDetailRefreshCts = cts;

        BindLibrarySessionMetadataControls(entry);
        _libraryTranscriptPreviewTextBox.Text = UiText.T("Transcript laden...");
        _libraryImportStatusTextBox.Text = "Importstatus laden...";

        try
        {
            var snapshot = await Task.Run(() => BuildLibraryDetailSnapshot(entry, cts.Token), cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested || refreshId != _libraryDetailRefreshVersion)
            {
                return;
            }

            _libraryTranscriptPreviewTextBox.Text = snapshot.TranscriptPreview;
            _libraryImportStatusTextBox.Text = UiText.T(snapshot.ImportStatusPreview);
            BindLibrarySessionMetadataControls(entry);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (refreshId != _libraryDetailRefreshVersion)
            {
                return;
            }

            AppRuntime.LogException("Library detail refresh failed", ex);
            _libraryTranscriptPreviewTextBox.Text = UiText.F("Sessiedetails konden niet worden geladen: {0}", ex.Message);
            _libraryImportStatusTextBox.Text = "Importstatus laden mislukt.";
        }
    }

    private static LibraryDetailSnapshot BuildLibraryDetailSnapshot(TranscriptLibraryEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transcriptText = TranscriptExportService.ReadTranscriptText(entry.DirectoryPath);
        cancellationToken.ThrowIfCancellationRequested();
        var importStatus = BuildImportStatusText(entry);

        return new LibraryDetailSnapshot
        {
            TranscriptPreview = string.IsNullOrWhiteSpace(transcriptText)
                ? "Transcriptbestand is nog niet beschikbaar of bevat nog geen leesbare tekst voor deze sessie."
                : transcriptText,
            ImportStatusPreview = importStatus
        };
    }

    private static string BuildImportStatusText(TranscriptLibraryEntry entry)
    {
        if (!string.Equals(entry.SourceType, "import", StringComparison.OrdinalIgnoreCase))
        {
            return "Deze sessie is geen externe/importopname.";
        }

        var lines = new List<string>();
        lines.Add("Import inbox");
        lines.Add("");
        lines.Add(UiText.F("Bestand: {0}", entry.SourceFileName ?? UiText.T("onbekend")));
        lines.Add($"Importprofiel: {entry.ImportSourceProfile ?? "unknown"}");
        if (!string.IsNullOrWhiteSpace(entry.ImportSourceProfileReason))
        {
            lines.Add($"Profielreden: {entry.ImportSourceProfileReason}");
        }
        lines.Add(UiText.F("Duur: {0}", entry.DurationSeconds.HasValue ? TimeSpan.FromSeconds(entry.DurationSeconds.Value).ToString(@"hh\:mm\:ss") : UiText.T("onbekend")));
        lines.Add("Embedded recorded_at: " + (entry.EmbeddedRecordedAt.HasValue ? entry.EmbeddedRecordedAt.Value.LocalDateTime.ToString("dd-MM-yyyy HH:mm:ss") : UiText.T("niet beschikbaar")));
        lines.Add("OneDrive observed_at: " + (entry.OneDriveObservedAt.HasValue ? entry.OneDriveObservedAt.Value.LocalDateTime.ToString("dd-MM-yyyy HH:mm:ss") : UiText.T("niet beschikbaar")));
        lines.Add(UiText.F("Opnametijd recorded_at: {0}", entry.RecordedAt.HasValue ? entry.RecordedAt.Value.LocalDateTime.ToString("dd-MM-yyyy HH:mm:ss") : UiText.T("onbekend")));
        lines.Add(UiText.F("Bron recorded_at: {0}", entry.RecordedAtSource ?? UiText.T("onbekend")));
        lines.Add($"Metadata-confidence: {entry.RecordedAtConfidence ?? "unknown"}");
        if (!string.IsNullOrWhiteSpace(entry.RecordedAtConfidenceReason))
        {
            lines.Add($"Confidence-uitleg: {entry.RecordedAtConfidenceReason}");
        }
        lines.Add(UiText.F("Handmatig bevestigd: {0}", UiText.T(entry.ManualRecordedAtConfirmed ? "ja" : "nee")));
        lines.Add(UiText.F("Importtijd imported_at: {0}", entry.ImportedAt.HasValue ? entry.ImportedAt.Value.LocalDateTime.ToString("dd-MM-yyyy HH:mm:ss") : UiText.T("onbekend")));

        return string.Join(Environment.NewLine, lines);
    }

    private void RefreshLibraryFilters(IReadOnlyList<TranscriptLibraryEntry> allEntries, IReadOnlyList<TranscriptLibraryEntry> visibleEntries)
    {
        RefreshLibraryContextFilters(allEntries);
        RefreshLibraryMeetingTypeFilters(allEntries);
        RefreshLibraryTagFilters(visibleEntries);
    }

    private void RefreshLibraryContextFilters(IReadOnlyList<TranscriptLibraryEntry> entries)
    {
        var catalog = LibraryContextCatalogService.MergeWithEntries(entries);
        var contexts = catalog.Contexts;
        var selectedContext = GetSelectedLibraryContextFilter();
        var subcontexts = catalog.Subcontexts
            .Where(x => string.IsNullOrWhiteSpace(selectedContext)
                || entries.Any(e => string.Equals(e.ContextName, selectedContext, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.SubcontextName, x, StringComparison.OrdinalIgnoreCase))
                || string.IsNullOrWhiteSpace(selectedContext))
            .ToList();

        _suppressLibraryContextFilterChanged = true;
        try
        {
            RebindFilterCombo(_libraryContextFilterComboBox, "Alle organisaties", contexts, _appSettings.Ui.LibrarySelectedContextFilter);
            RebindFilterCombo(_librarySubcontextFilterComboBox, "Alle projecten / dossiers", subcontexts, _appSettings.Ui.LibrarySelectedSubcontextFilter);
        }
        finally
        {
            _suppressLibraryContextFilterChanged = false;
        }
    }

    private void RefreshLibraryMeetingTypeFilters(IReadOnlyList<TranscriptLibraryEntry> entries)
    {
        var meetingTypes = LibraryContextCatalogService.MergeWithEntries(entries).MeetingTypes;
        _suppressLibraryContextFilterChanged = true;
        try
        {
            RebindFilterCombo(_libraryMeetingTypeFilterComboBox, "Alle overlegtypen", meetingTypes, _appSettings.Ui.LibrarySelectedMeetingTypeFilter);
        }
        finally
        {
            _suppressLibraryContextFilterChanged = false;
        }
    }

    private void RefreshLibraryTagFilters(IReadOnlyList<TranscriptLibraryEntry> entries)
    {
        var tags = LibraryContextCatalogService.MergeWithEntries(entries).Tags;
        var selected = _appSettings.Ui.LibrarySelectedTagFilter;
        _suppressLibraryTagFilterChanged = true;
        try
        {
            RebindFilterCombo(_libraryTagFilterComboBox, "Alle onderwerpen", tags, selected);
        }
        finally
        {
            _suppressLibraryTagFilterChanged = false;
        }
    }

    private void RebindFilterCombo(ComboBox comboBox, string allLabel, IReadOnlyList<string> values, string selectedValue)
    {
        var localizedAllLabel = UiText.T(allLabel);
        comboBox.BeginUpdate();
        try
        {
            comboBox.Items.Clear();
            comboBox.Items.Add(localizedAllLabel);
            foreach (var value in values)
            {
                comboBox.Items.Add(value);
            }

            if (!string.IsNullOrWhiteSpace(selectedValue) && comboBox.Items.Contains(selectedValue))
            {
                comboBox.SelectedItem = selectedValue;
            }
            else
            {
                comboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            comboBox.EndUpdate();
        }
    }

    private bool IsLibraryAllLabelSelected(ComboBox comboBox, string canonicalLabel)
    {
        if (comboBox.SelectedItem is not string value)
        {
            return true;
        }

        var localizedLabel = UiText.T(canonicalLabel);
        return string.Equals(value, canonicalLabel, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, localizedLabel, StringComparison.OrdinalIgnoreCase);
    }

    private string GetSelectedLibraryContextFilter()
        => _libraryContextFilterComboBox.SelectedItem is string value && !IsLibraryAllLabelSelected(_libraryContextFilterComboBox, "Alle organisaties") ? value : string.Empty;

    private string GetSelectedLibrarySubcontextFilter()
        => _librarySubcontextFilterComboBox.SelectedItem is string value && !IsLibraryAllLabelSelected(_librarySubcontextFilterComboBox, "Alle projecten / dossiers") ? value : string.Empty;

    private string GetSelectedLibraryMeetingTypeFilter()
        => _libraryMeetingTypeFilterComboBox.SelectedItem is string value && !IsLibraryAllLabelSelected(_libraryMeetingTypeFilterComboBox, "Alle overlegtypen") ? value : string.Empty;

    private string GetSelectedLibraryTagFilter()
        => _libraryTagFilterComboBox.SelectedItem is string value && !IsLibraryAllLabelSelected(_libraryTagFilterComboBox, "Alle onderwerpen") ? value : string.Empty;

    private void ShowLibraryTagsDialog()
    {
        var allEntries = TranscriptLibraryService.Scan(_appSettings, null, null, null, null, null, 500);
        var catalog = LibraryContextCatalogService.MergeWithEntries(allEntries);

        using var dialog = new Form
        {
            Text = $"{Branding.DisplayName} · Indeling beheren",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(760, 560),
            MinimumSize = new Size(700, 520),
            BackColor = Color.White,
            Font = Font
        };

        var info = new Label
        {
            Text = "Beheer hier de bibliotheekbrede indeling. Gebruik Organisatie voor je eigen organisatie, een opdrachtgever of een andere hoofdcontext, Project / dossier voor nadere afbakening en Onderwerpen voor trefwoorden. Toewijzing aan een specifieke sessie gebeurt via rechtermuisknop op een sessie in de lijst.",
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            Margin = new Padding(0, 0, 0, 10)
        };

        var contextsEditor = CreateCatalogEditor("Nieuwe organisatie", "Bijvoorbeeld: Gemeente Voorbeeld", catalog.Contexts, "Organisaties");
        var subcontextsEditor = CreateCatalogEditor("Nieuw project / dossier", "Bijvoorbeeld: NIS2 implementatie", catalog.Subcontexts, "Projecten / dossiers");
        var meetingTypesEditor = CreateCatalogEditor("Nieuw overlegtype", "Bijvoorbeeld: Teamvergadering", catalog.MeetingTypes, "Overlegtypen");
        var tagsEditor = CreateCatalogEditor("Nieuw onderwerp", "Bijvoorbeeld: MT, NIS2, incident", catalog.Tags, "Onderwerpen");

        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 0, 0)
        };
        tabControl.TabPages.Add(CreateCatalogTabPage("Organisaties", "Gebruik Organisatie als harde scheiding tussen organisaties, opdrachtgevers of andere hoofdcontexten.", contextsEditor.Host));
        tabControl.TabPages.Add(CreateCatalogTabPage("Projecten / dossiers", "Gebruik Project / dossier voor projecten, dossiers of teams binnen een organisatie.", subcontextsEditor.Host));
        tabControl.TabPages.Add(CreateCatalogTabPage("Overlegtypen", "Gebruik Overlegtype als compacte indeling voor bijvoorbeeld teamvergadering, voortgangsoverleg of evaluatie.", meetingTypesEditor.Host));
        tabControl.TabPages.Add(CreateCatalogTabPage("Onderwerpen", "Gebruik Onderwerpen als aanvullende verrijking voor thema’s, typen overleg of classificaties.", tagsEditor.Host));

        var okButton = new Button { Text = "Opslaan", Width = 120, Height = 34, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Annuleren", Width = 120, Height = 34, DialogResult = DialogResult.Cancel };
        StyleButton(okButton, AccentColor, AccentHoverColor, Color.White);
        StyleButton(cancelButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(info, 0, 0);
        root.Controls.Add(tabControl, 0, 1);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Top, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);
        root.Controls.Add(buttons, 0, 2);
        dialog.Controls.Add(root);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        catalog = new LibraryContextCatalog
        {
            Contexts = GetListBoxItems(contextsEditor.ListBox),
            Subcontexts = GetListBoxItems(subcontextsEditor.ListBox),
            MeetingTypes = GetListBoxItems(meetingTypesEditor.ListBox),
            Tags = GetListBoxItems(tagsEditor.ListBox)
        };
        LibraryContextCatalogService.Save(catalog);
        BindLibrarySessionMetadataControls(GetSelectedLibraryEntry());
        _ = RunLibraryUiActionAsync("Bibliotheek vernieuwen", ct => RefreshLibraryAsync(ct));
    }

    private static TabPage CreateCatalogTabPage(string title, string description, Control content)
    {
        var page = new TabPage(title) { Padding = new Padding(12) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = description,
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);
        layout.Controls.Add(content, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private (Control Host, ListBox ListBox) CreateCatalogEditor(string inputLabel, string placeholder, IReadOnlyList<string> values, string listLabel)
    {
        var listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            SelectionMode = SelectionMode.One,
            HorizontalScrollbar = true
        };
        foreach (var value in values)
        {
            listBox.Items.Add(value);
        }
        SortListBox(listBox);

        var inputBox = new TextBox { Dock = DockStyle.Fill, PlaceholderText = placeholder };
        var addButton = new Button { Text = "Toevoegen", Width = 110, Height = 32 };
        var removeButton = new Button { Text = "Verwijder geselecteerd", Width = 180, Height = 32 };
        StyleButton(addButton, AccentColor, AccentHoverColor, Color.White);
        StyleButton(removeButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));

        var feedbackLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 104, 120),
            Margin = new Padding(0, 6, 0, 0),
            Text = listBox.Items.Count == 0 ? "Nog geen items toegevoegd." : $"{listBox.Items.Count} item(s) beschikbaar."
        };

        void RefreshFeedback(string? selectedValue = null)
        {
            feedbackLabel.Text = listBox.Items.Count == 0 ? "Nog geen items toegevoegd." : $"{listBox.Items.Count} item(s) beschikbaar.";
            if (!string.IsNullOrWhiteSpace(selectedValue))
            {
                for (var i = 0; i < listBox.Items.Count; i++)
                {
                    if (string.Equals(listBox.Items[i]?.ToString(), selectedValue, StringComparison.OrdinalIgnoreCase))
                    {
                        listBox.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        addButton.Click += (_, _) =>
        {
            var value = inputBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!listBox.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), value, StringComparison.OrdinalIgnoreCase)))
            {
                listBox.Items.Add(value);
            }

            SortListBox(listBox);
            RefreshFeedback(value);
            inputBox.Clear();
            inputBox.Focus();
        };
        removeButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is null)
            {
                return;
            }

            var removeIndex = listBox.SelectedIndex;
            listBox.Items.Remove(listBox.SelectedItem);
            if (listBox.Items.Count > 0)
            {
                listBox.SelectedIndex = Math.Min(removeIndex, listBox.Items.Count - 1);
            }
            RefreshFeedback();
        };

        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var inputLabelControl = new Label { Text = inputLabel, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
        host.Controls.Add(inputLabelControl, 0, 0);

        var addRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        addRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        addRow.Controls.Add(inputBox, 0, 0);
        addRow.Controls.Add(addButton, 1, 0);
        host.Controls.Add(addRow, 0, 1);

        var listGroup = new GroupBox { Text = listLabel, Dock = DockStyle.Fill, Padding = new Padding(10, 8, 10, 10) };
        var listLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        listLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        listLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        listLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        listLayout.Controls.Add(listBox, 0, 0);
        var removeRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 8, 0, 0) };
        removeRow.Controls.Add(removeButton);
        listLayout.Controls.Add(removeRow, 0, 1);
        listLayout.Controls.Add(feedbackLabel, 0, 2);
        listGroup.Controls.Add(listLayout);
        host.Controls.Add(listGroup, 0, 2);

        host.Controls.Add(new Panel { Height = 1, Dock = DockStyle.Bottom }, 0, 3);
        return (host, listBox);
    }

    private static void SortListBox(ListBox listBox)
    {
        var values = listBox.Items.Cast<object>()
            .Select(x => x?.ToString() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        listBox.Items.Clear();
        listBox.Items.AddRange(values);
    }

    private string BuildLibraryRuntimeStatusText(TranscriptLibraryEntry entry)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.Status))
        {
            parts.Add($"sessie: {entry.Status}");
        }

        if (!string.IsNullOrWhiteSpace(entry.TranscriptionStatus))
        {
            parts.Add($"transcriptie: {entry.TranscriptionStatus}");
        }

        var appVersion = AppRuntime.NormalizeVersionTag(entry.AppVersion);
        var workerVersion = AppRuntime.NormalizeVersionTag(entry.WorkerVersion);
        if (!string.IsNullOrWhiteSpace(appVersion) && !string.IsNullOrWhiteSpace(workerVersion))
        {
            parts.Add(string.Equals(appVersion, workerVersion, StringComparison.OrdinalIgnoreCase)
                ? $"build: {appVersion}"
                : $"build mismatch: {appVersion} / worker {workerVersion}");
        }
        else if (!string.IsNullOrWhiteSpace(appVersion))
        {
            parts.Add($"build: {appVersion}");
        }
        else if (!string.IsNullOrWhiteSpace(workerVersion))
        {
            parts.Add($"worker: {workerVersion}");
        }

        if (entry.ReadyWrittenAt.HasValue)
        {
            parts.Add($"ready: {entry.ReadyWrittenAt.Value.LocalDateTime:dd-MM HH:mm}");
        }

        if (entry.TranscriptionStartedAt.HasValue || entry.TranscriptionFinishedAt.HasValue)
        {
            var inconsistentTimeline = false;
            if (entry.TranscriptionStartedAt.HasValue && entry.TranscriptionFinishedAt.HasValue)
            {
                inconsistentTimeline = entry.TranscriptionFinishedAt < entry.TranscriptionStartedAt;
            }

            if (!inconsistentTimeline && entry.ReadyWrittenAt.HasValue && entry.TranscriptionStartedAt.HasValue)
            {
                inconsistentTimeline = entry.TranscriptionStartedAt < entry.ReadyWrittenAt;
            }

            if (!inconsistentTimeline && entry.TranscriptionStartedAt.HasValue && entry.TranscriptionFinishedAt.HasValue)
            {
                parts.Add($"tijdlijn: {entry.TranscriptionStartedAt.Value.LocalDateTime:dd-MM HH:mm} → {entry.TranscriptionFinishedAt.Value.LocalDateTime:dd-MM HH:mm}");
                if (!string.IsNullOrWhiteSpace(entry.TranscriptionTimingAuthority))
                {
                    parts.Add($"tijdbron: {entry.TranscriptionTimingAuthority}");
                }
            }
            else
            {
                parts.Add("tijdlijn: inconsistent");
            }
        }


        return parts.Count == 0
            ? UiText.T("Nog geen status")
            : string.Join(" · ", parts);
    }

    private string BuildLibraryArtifactStatusText(TranscriptLibraryEntry entry)
    {
        var parts = new List<string>();
        parts.Add(HasTranscriptFile(entry) ? "transcript aanwezig" : "transcript ontbreekt");
        return string.Join(" · ", parts);
    }

    private static List<string> GetListBoxItems(ListBox listBox)
        => listBox.Items.Cast<object>()
            .Select(x => x?.ToString() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    private static List<string> SplitLines(string text)
        => text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void BindLibrarySessionMetadataControls(TranscriptLibraryEntry? entry)
    {
        var selectedEntries = GetSelectedLibraryEntries();
        if (selectedEntries.Count == 0)
        {
            _librarySessionSelectionSummaryLabel.Text = UiText.T("Nog geen sessie geselecteerd.");
            _librarySessionOrganizationValueLabel.Text = UiText.T("Nog niet toegewezen");
            _librarySessionProjectValueLabel.Text = UiText.T("Nog niet toegewezen");
            _librarySessionMeetingTypeValueLabel.Text = UiText.T("Nog niet toegewezen");
            _librarySessionCalendarSubjectValueLabel.Text = UiText.T("Nog geen agenda-match");
            _librarySessionParticipantsValueLabel.Text = UiText.T("Nog geen deelnemers");
            _librarySessionAgendaStatusValueLabel.Text = UiText.T("Nog geen agenda-status");
            _librarySessionTopicsValueLabel.Text = UiText.T("Geen onderwerpen");
            _librarySessionRuntimeStatusValueLabel.Text = UiText.T("Nog geen status");
            _libraryArtifactStatusLabel.Text = "Nog geen artefactstatus";
            return;
        }

        if (selectedEntries.Count == 1 && entry is not null)
        {
            _librarySessionSelectionSummaryLabel.Text = UiText.F("Geselecteerde sessie: {0} · {1:dd-MM-yyyy HH:mm}", GetLibraryDisplayTitle(entry), entry.CreatedAt.LocalDateTime);
            _librarySessionOrganizationValueLabel.Text = string.IsNullOrWhiteSpace(entry.ContextName) ? UiText.T("Nog niet toegewezen") : entry.ContextName;
            _librarySessionProjectValueLabel.Text = string.IsNullOrWhiteSpace(entry.SubcontextName) ? UiText.T("Nog niet toegewezen") : entry.SubcontextName;
            _librarySessionMeetingTypeValueLabel.Text = string.IsNullOrWhiteSpace(entry.MeetingType) ? UiText.T("Nog niet toegewezen") : entry.MeetingType;
            _librarySessionCalendarSubjectValueLabel.Text = string.IsNullOrWhiteSpace(entry.CalendarSubject) ? UiText.T("Nog geen agenda-match") : entry.CalendarSubject;
            _librarySessionParticipantsValueLabel.Text = entry.Participants.Count == 0 ? UiText.T("Nog geen deelnemers") : string.Join(", ", entry.Participants);
            _librarySessionTopicsValueLabel.Text = entry.Tags.Count == 0 ? UiText.T("Geen onderwerpen") : string.Join(", ", entry.Tags);
            _librarySessionRuntimeStatusValueLabel.Text = BuildLibraryRuntimeStatusText(entry);
            _libraryArtifactStatusLabel.Text = BuildLibraryArtifactStatusText(entry);
            return;
        }

        _librarySessionSelectionSummaryLabel.Text = UiText.F("{0} sessies geselecteerd. Toewijzingen via rechtermuisknop worden op alle geselecteerde sessies toegepast.", selectedEntries.Count);

        var organizationValues = selectedEntries
            .Select(x => string.IsNullOrWhiteSpace(x.ContextName) ? string.Empty : x.ContextName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var projectValues = selectedEntries
            .Select(x => string.IsNullOrWhiteSpace(x.SubcontextName) ? string.Empty : x.SubcontextName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var meetingTypeValues = selectedEntries
            .Select(x => string.IsNullOrWhiteSpace(x.MeetingType) ? string.Empty : x.MeetingType.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var commonTopics = selectedEntries
            .Select(x => x.Tags ?? new List<string>())
            .Aggregate(
                (IEnumerable<string>)selectedEntries.First().Tags,
                (current, tags) => current.Intersect(tags, StringComparer.OrdinalIgnoreCase).ToList())
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _librarySessionOrganizationValueLabel.Text = organizationValues.Count == 1
            ? (string.IsNullOrWhiteSpace(organizationValues[0]) ? UiText.T("Nog niet toegewezen") : organizationValues[0])
            : UiText.T("Meerdere waarden");
        _librarySessionProjectValueLabel.Text = projectValues.Count == 1
            ? (string.IsNullOrWhiteSpace(projectValues[0]) ? UiText.T("Nog niet toegewezen") : projectValues[0])
            : UiText.T("Meerdere waarden");
        _librarySessionMeetingTypeValueLabel.Text = meetingTypeValues.Count == 1
            ? (string.IsNullOrWhiteSpace(meetingTypeValues[0]) ? UiText.T("Nog niet toegewezen") : meetingTypeValues[0])
            : UiText.T("Meerdere waarden");
        _librarySessionCalendarSubjectValueLabel.Text = UiText.T("Meerdere sessies geselecteerd");
        _librarySessionParticipantsValueLabel.Text = UiText.T("Meerdere sessies geselecteerd");
        _librarySessionAgendaStatusValueLabel.Text = UiText.T("Meerdere sessies geselecteerd");
        _librarySessionTopicsValueLabel.Text = commonTopics.Count > 0
            ? UiText.F("Gemeenschappelijk: {0}", string.Join(", ", commonTopics))
            : UiText.T("Geen gemeenschappelijke onderwerpen");
        _librarySessionRuntimeStatusValueLabel.Text = UiText.T("Meerdere sessies geselecteerd");
        _libraryArtifactStatusLabel.Text = "Artefactstatus niet beschikbaar bij meerdere geselecteerde sessies";
    }

    private List<TranscriptLibraryEntry> GetVisibleLibraryEntries()
        => _libraryListView.Items.Cast<ListViewItem>()
            .Select(item => item.Tag as TranscriptLibraryEntry)
            .Where(entry => entry is not null)
            .Cast<TranscriptLibraryEntry>()
            .ToList();

    private void RefreshLibraryUiLanguageState()
    {
        var allEntries = TranscriptLibraryService.Scan(_appSettings, null, null, null, null, null, 500);
        var visibleEntries = GetVisibleLibraryEntries();

        RefreshLibraryContextFilters(allEntries);
        RefreshLibraryMeetingTypeFilters(allEntries);
        RefreshLibraryTagFilters(visibleEntries);
        BindLibrarySessionMetadataControls(GetSelectedLibraryEntry());

        if (GetSelectedLibraryEntry() is null)
        {
            _libraryTranscriptPreviewTextBox.Text = UiText.T("Selecteer een sessie om transcriptdetails te bekijken.");
            _libraryImportStatusTextBox.Text = "Selecteer een geimporteerde opname om recorded_at en imported_at te bekijken.";
        }
    }

    private void SaveLibrarySessionMetadata(TranscriptLibraryEntry entry, LibrarySessionMetadata updated, string statusText)
    {
        SaveLibrarySessionMetadataBatch(new[] { entry }, _ => updated, statusText);
    }

    private void SaveLibrarySessionMetadataBatch(IReadOnlyList<TranscriptLibraryEntry> entries, Func<TranscriptLibraryEntry, LibrarySessionMetadata> createMetadata, string statusText)
    {
        if (entries is null || entries.Count == 0)
        {
            return;
        }

        var catalogEntries = new List<TranscriptLibraryEntry>();
        foreach (var entry in entries)
        {
            var existing = LibrarySessionMetadataService.Load(entry.DirectoryPath);
            var updated = createMetadata(entry);
            existing.ContextId = updated.ContextId;
            existing.ContextName = updated.ContextName;
            existing.SubcontextId = updated.SubcontextId;
            existing.SubcontextName = updated.SubcontextName;
            existing.MeetingType = updated.MeetingType;
            existing.Tags = updated.Tags;
            LibrarySessionMetadataService.Save(entry.DirectoryPath, existing);
            catalogEntries.Add(new TranscriptLibraryEntry
            {
                ContextName = existing.ContextName,
                SubcontextName = existing.SubcontextName,
                MeetingType = existing.MeetingType,
                Tags = existing.Tags
            });
        }

        var catalog = LibraryContextCatalogService.MergeWithEntries(catalogEntries);
        LibraryContextCatalogService.Save(catalog);
        var detail = entries.Count == 1 ? entries[0].MeetingId : $"{entries.Count} sessies";
        SetStatus(statusText, detail);
        _ = RunLibraryUiActionAsync("Bibliotheek vernieuwen", ct => RefreshLibraryAsync(ct));
    }

    private void AssignSelectedLibraryOrganization(string? organization)
    {
        var entries = GetSelectedLibraryEntries();
        if (entries.Count == 0)
        {
            ShowOwnedMessage(this, "Selecteer eerst één of meer sessies in de bibliotheek.", "Geen sessie geselecteerd", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var normalizedOrganization = organization?.Trim() ?? string.Empty;
        SaveLibrarySessionMetadataBatch(entries, entry => new LibrarySessionMetadata
        {
            ContextName = normalizedOrganization,
            ContextId = normalizedOrganization,
            SubcontextName = entry.SubcontextName,
            SubcontextId = entry.SubcontextName,
            MeetingType = entry.MeetingType,
            Tags = entry.Tags.ToList()
        }, entries.Count == 1 ? "Organisatie toegewezen." : "Organisatie toegewezen aan geselecteerde sessies.");
    }

    private void AssignSelectedLibraryProject(string? project)
    {
        var entries = GetSelectedLibraryEntries();
        if (entries.Count == 0)
        {
            ShowOwnedMessage(this, "Selecteer eerst één of meer sessies in de bibliotheek.", "Geen sessie geselecteerd", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var normalizedProject = project?.Trim() ?? string.Empty;
        SaveLibrarySessionMetadataBatch(entries, entry => new LibrarySessionMetadata
        {
            ContextName = entry.ContextName,
            ContextId = entry.ContextName,
            SubcontextName = normalizedProject,
            SubcontextId = normalizedProject,
            MeetingType = entry.MeetingType,
            Tags = entry.Tags.ToList()
        }, entries.Count == 1 ? "Project / dossier toegewezen." : "Project / dossier toegewezen aan geselecteerde sessies.");
    }

    private void AssignSelectedLibraryMeetingType(string? meetingType)
    {
        var entries = GetSelectedLibraryEntries();
        if (entries.Count == 0)
        {
            ShowOwnedMessage(this, "Selecteer eerst één of meer sessies in de bibliotheek.", "Geen sessie geselecteerd", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var normalizedMeetingType = meetingType?.Trim() ?? string.Empty;
        SaveLibrarySessionMetadataBatch(entries, entry => new LibrarySessionMetadata
        {
            ContextName = entry.ContextName,
            ContextId = entry.ContextName,
            SubcontextName = entry.SubcontextName,
            SubcontextId = entry.SubcontextName,
            MeetingType = normalizedMeetingType,
            Tags = entry.Tags.ToList()
        }, entries.Count == 1 ? "Overlegtype toegewezen." : "Overlegtype toegewezen aan geselecteerde sessies.");
    }

    private void ShowAssignMeetingTypeDialog()
    {
        var entries = GetSelectedLibraryEntries();
        var entry = entries.FirstOrDefault();
        if (entry is null)
        {
            ShowOwnedMessage(this, "Selecteer eerst één of meer sessies in de bibliotheek.", "Geen sessie geselecteerd", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var catalog = LibraryContextCatalogService.MergeWithEntries(TranscriptLibraryService.Scan(_appSettings, null, null, null, null, null, 500));
        using var dialog = new Form
        {
            Text = $"{Branding.DisplayName} · Overlegtype toewijzen",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(460, 220),
            MinimumSize = new Size(420, 200),
            BackColor = Color.White,
            Font = Font
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16),
            BackColor = Color.White
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = entries.Count == 1
                ? "Kies of vul een overlegtype in voor deze sessie."
                : $"Kies of vul een overlegtype in voor {entries.Count} sessies tegelijk.",
            AutoSize = true,
            MaximumSize = new Size(380, 0),
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);

        var combo = new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDown,
            Margin = new Padding(0, 0, 0, 10)
        };
        combo.Items.AddRange(catalog.MeetingTypes.Cast<object>().ToArray());
        combo.Text = entries.Count == 1 ? entry.MeetingType : string.Empty;
        root.Controls.Add(combo, 0, 1);

        root.Controls.Add(new Label
        {
            Text = "Voorbeelden: Teamvergadering, Evaluatie, Voortgangsoverleg, 1-op-1.",
            AutoSize = true,
            ForeColor = Color.FromArgb(97, 107, 121),
            MaximumSize = new Size(380, 0)
        }, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 12, 0, 0)
        };
        var saveButton = new Button { Text = "Opslaan", Width = 110, Height = 34 };
        var clearButton = new Button { Text = "Leegmaken", Width = 110, Height = 34 };
        var cancelButton = new Button { Text = "Annuleren", Width = 110, Height = 34 };
        StyleButton(saveButton, AccentColor, AccentHoverColor, Color.White);
        StyleButton(clearButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(cancelButton, Color.FromArgb(245, 247, 250), Color.FromArgb(234, 239, 244), Color.FromArgb(67, 78, 92));
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(clearButton);
        buttons.Controls.Add(cancelButton);
        root.Controls.Add(buttons, 0, 3);
        dialog.Controls.Add(root);

        saveButton.Click += (_, _) => dialog.DialogResult = DialogResult.OK;
        clearButton.Click += (_, _) => { combo.Text = string.Empty; dialog.DialogResult = DialogResult.OK; };
        cancelButton.Click += (_, _) => dialog.DialogResult = DialogResult.Cancel;

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        AssignSelectedLibraryMeetingType(combo.Text);
    }

    private void ShowAssignTopicsDialog()
    {
        var entries = GetSelectedLibraryEntries();
        var entry = entries.FirstOrDefault();
        if (entry is null)
        {
            ShowOwnedMessage(this, "Selecteer eerst één of meer sessies in de bibliotheek.", "Geen sessie geselecteerd", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var catalog = LibraryContextCatalogService.MergeWithEntries(TranscriptLibraryService.Scan(_appSettings, null, null, null, null, null, 500));
        using var dialog = new Form
        {
            Text = $"{Branding.DisplayName} · Onderwerpen toewijzen",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(520, 520),
            MinimumSize = new Size(480, 460),
            BackColor = Color.White,
            Font = Font
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(16),
            BackColor = Color.White
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = entries.Count == 1
                ? "Selecteer bestaande onderwerpen voor deze sessie. Voeg optioneel onderaan één of meer nieuwe onderwerpen toe, gescheiden door komma’s."
                : $"Selecteer bestaande onderwerpen voor {entries.Count} sessies tegelijk. Aangevinkte onderwerpen worden op alle geselecteerde sessies gezet. Voeg optioneel onderaan één of meer nieuwe onderwerpen toe, gescheiden door komma’s.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);

        var checklist = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false
        };
        foreach (var tag in catalog.Tags)
        {
            var index = checklist.Items.Add(tag);
            var check = entries.Count == 1
                ? entry.Tags.Any(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase))
                : entries.All(selected => selected.Tags.Any(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase)));
            if (check)
            {
                checklist.SetItemChecked(index, true);
            }
        }
        root.Controls.Add(checklist, 0, 1);

        root.Controls.Add(new Label { Text = "Nieuwe onderwerpen", AutoSize = true, Margin = new Padding(0, 10, 0, 4) }, 0, 2);
        var newTopicsBox = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Bijvoorbeeld: audit, planning, risicoanalyse" };
        root.Controls.Add(newTopicsBox, 0, 3);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 14, 0, 0)
        };
        var saveButton = new Button { Text = "Opslaan", Width = 110, Height = 34 };
        var cancelButton = new Button { Text = "Annuleren", Width = 110, Height = 34 };
        StyleButton(saveButton, AccentColor, AccentHoverColor, Color.White);
        StyleButton(cancelButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);
        root.Controls.Add(buttons, 0, 4);

        saveButton.Click += (_, _) => dialog.DialogResult = DialogResult.OK;
        cancelButton.Click += (_, _) => dialog.DialogResult = DialogResult.Cancel;
        dialog.Controls.Add(root);

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var selectedTags = checklist.CheckedItems.Cast<object>()
            .Select(x => x?.ToString() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        selectedTags.AddRange(newTopicsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        var updatedTags = selectedTags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SaveLibrarySessionMetadataBatch(entries, selected => new LibrarySessionMetadata
        {
            ContextName = selected.ContextName,
            ContextId = selected.ContextName,
            SubcontextName = selected.SubcontextName,
            SubcontextId = selected.SubcontextName,
            MeetingType = selected.MeetingType,
            Tags = updatedTags
        }, entries.Count == 1 ? "Onderwerpen bijgewerkt." : "Onderwerpen bijgewerkt voor geselecteerde sessies.");
    }

    private void LibraryContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var entries = GetSelectedLibraryEntries();
        var entry = entries.FirstOrDefault();
        var hasSelection = entry is not null;
        _libraryContextAssignOrganizationItem.Enabled = hasSelection;
        _libraryContextAssignProjectItem.Enabled = hasSelection;
        _libraryContextAssignMeetingTypeItem.Enabled = hasSelection;
        _libraryContextAssignTopicsItem.Enabled = hasSelection;
        if (!hasSelection)
        {
            return;
        }

        var hasSingleSelection = entries.Count == 1;
        var catalog = LibraryContextCatalogService.MergeWithEntries(TranscriptLibraryService.Scan(_appSettings, null, null, null, null, null, 500));

        _libraryContextAssignOrganizationItem.DropDownItems.Clear();
        var organizationsAreSame = entries.Select(x => string.IsNullOrWhiteSpace(x.ContextName) ? string.Empty : x.ContextName.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
        var clearOrg = new ToolStripMenuItem("Geen organisatie") { Checked = organizationsAreSame && entries.All(x => string.IsNullOrWhiteSpace(x.ContextName)) };
        clearOrg.Click += (_, _) => AssignSelectedLibraryOrganization(string.Empty);
        _libraryContextAssignOrganizationItem.DropDownItems.Add(clearOrg);
        _libraryContextAssignOrganizationItem.DropDownItems.Add(new ToolStripSeparator());
        foreach (var organization in catalog.Contexts)
        {
            var isChecked = organizationsAreSame && entries.All(x => string.Equals(x.ContextName, organization, StringComparison.OrdinalIgnoreCase));
            var item = new ToolStripMenuItem(organization) { Checked = isChecked };
            item.Click += (_, _) => AssignSelectedLibraryOrganization(organization);
            _libraryContextAssignOrganizationItem.DropDownItems.Add(item);
        }
        if (_libraryContextAssignOrganizationItem.DropDownItems.Count == 2)
        {
            _libraryContextAssignOrganizationItem.DropDownItems.Add(new ToolStripMenuItem("Nog geen organisaties. Gebruik eerst Beheer indeling.") { Enabled = false });
        }
        _libraryContextAssignOrganizationItem.Text = hasSingleSelection ? "Toewijzen aan organisatie" : $"Toewijzen aan organisatie ({entries.Count})";

        _libraryContextAssignProjectItem.DropDownItems.Clear();
        var projectsAreSame = entries.Select(x => string.IsNullOrWhiteSpace(x.SubcontextName) ? string.Empty : x.SubcontextName.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
        var clearProject = new ToolStripMenuItem("Geen project / dossier") { Checked = projectsAreSame && entries.All(x => string.IsNullOrWhiteSpace(x.SubcontextName)) };
        clearProject.Click += (_, _) => AssignSelectedLibraryProject(string.Empty);
        _libraryContextAssignProjectItem.DropDownItems.Add(clearProject);
        _libraryContextAssignProjectItem.DropDownItems.Add(new ToolStripSeparator());
        foreach (var project in catalog.Subcontexts)
        {
            var isChecked = projectsAreSame && entries.All(x => string.Equals(x.SubcontextName, project, StringComparison.OrdinalIgnoreCase));
            var item = new ToolStripMenuItem(project) { Checked = isChecked };
            item.Click += (_, _) => AssignSelectedLibraryProject(project);
            _libraryContextAssignProjectItem.DropDownItems.Add(item);
        }
        if (_libraryContextAssignProjectItem.DropDownItems.Count == 2)
        {
            _libraryContextAssignProjectItem.DropDownItems.Add(new ToolStripMenuItem("Nog geen projecten / dossiers. Gebruik eerst Beheer indeling.") { Enabled = false });
        }
        _libraryContextAssignProjectItem.Text = hasSingleSelection ? "Toewijzen aan project / dossier" : $"Toewijzen aan project / dossier ({entries.Count})";

        _libraryContextAssignMeetingTypeItem.DropDownItems.Clear();
        var meetingTypesAreSame = entries.Select(x => string.IsNullOrWhiteSpace(x.MeetingType) ? string.Empty : x.MeetingType.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
        var clearMeetingType = new ToolStripMenuItem("Geen overlegtype") { Checked = meetingTypesAreSame && entries.All(x => string.IsNullOrWhiteSpace(x.MeetingType)) };
        clearMeetingType.Click += (_, _) => AssignSelectedLibraryMeetingType(string.Empty);
        _libraryContextAssignMeetingTypeItem.DropDownItems.Add(clearMeetingType);
        _libraryContextAssignMeetingTypeItem.DropDownItems.Add(new ToolStripSeparator());
        foreach (var meetingType in catalog.MeetingTypes)
        {
            var isChecked = meetingTypesAreSame && entries.All(x => string.Equals(x.MeetingType, meetingType, StringComparison.OrdinalIgnoreCase));
            var item = new ToolStripMenuItem(meetingType) { Checked = isChecked };
            item.Click += (_, _) => AssignSelectedLibraryMeetingType(meetingType);
            _libraryContextAssignMeetingTypeItem.DropDownItems.Add(item);
        }
        if (_libraryContextAssignMeetingTypeItem.DropDownItems.Count == 2)
        {
            _libraryContextAssignMeetingTypeItem.DropDownItems.Add(new ToolStripMenuItem("Nog geen overlegtypen. Gebruik eerst Beheer indeling.") { Enabled = false });
        }
        _libraryContextAssignMeetingTypeItem.Text = hasSingleSelection ? "Toewijzen aan overlegtype" : $"Toewijzen aan overlegtype ({entries.Count})";
        _libraryContextAssignTopicsItem.Text = hasSingleSelection ? "Onderwerpen toewijzen…" : $"Onderwerpen toewijzen… ({entries.Count})";
    }

    private void EditSelectedLibrarySessionTitle()
    {
        var entry = GetSelectedLibraryEntry();
        if (entry is null)
        {
            ShowOwnedMessage(this, "Selecteer eerst een sessie in de bibliotheek.", "Geen sessie geselecteerd", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new BufferedDialogForm
        {
            Text = "Titel toevoegen/wijzigen",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            Size = new Size(560, 180),
            BackColor = Color.White,
            Font = Font
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Handmatige titel voor deze opname/vergadering:", AutoSize = true }, 0, 0);
        var titleBox = new TextBox { Dock = DockStyle.Fill, Text = string.IsNullOrWhiteSpace(entry.DisplayTitle) ? GetLibraryDisplayTitle(entry) : entry.DisplayTitle };
        layout.Controls.Add(titleBox, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var okButton = new Button { Text = "Opslaan", DialogResult = DialogResult.OK, Width = 110, Height = 32 };
        var clearButton = new Button { Text = "Wissen", Width = 110, Height = 32 };
        var cancelButton = new Button { Text = "Annuleren", DialogResult = DialogResult.Cancel, Width = 110, Height = 32 };
        StyleButton(okButton, AccentColor, AccentHoverColor, Color.White);
        StyleButton(clearButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(cancelButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        clearButton.Click += (_, _) => titleBox.Text = string.Empty;
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(clearButton);
        layout.Controls.Add(buttons, 0, 2);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var metadata = LibrarySessionMetadataService.Load(entry.DirectoryPath);
        metadata.DisplayTitle = titleBox.Text.Trim();
        LibrarySessionMetadataService.Save(entry.DirectoryPath, metadata);
        _ = RunLibraryUiActionAsync("Bibliotheek vernieuwen", ct => RefreshLibraryAsync(ct));
    }

    private void EditSelectedImportedRecordingRecordedAt()
    {
        var entry = GetSelectedLibraryEntry();
        if (entry is null)
        {
            ShowOwnedMessage(this, "Selecteer eerst een sessie in de bibliotheek.", "Geen sessie geselecteerd", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!string.Equals(entry.SourceType, "import", StringComparison.OrdinalIgnoreCase))
        {
            ShowOwnedMessage(this, "Opnametijd wijzigen is alleen bedoeld voor geïmporteerde externe opnames.", "Geen importopname", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var current = entry.RecordedAt ?? entry.CreatedAt;
        using var dialog = new BufferedDialogForm
        {
            Text = "Opnametijd wijzigen",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            Size = new Size(560, 230),
            BackColor = Color.White,
            Font = Font
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "Zet hier de werkelijke starttijd van de opname.",
            AutoSize = true,
            MaximumSize = new Size(500, 0)
        }, 0, 0);
        var picker = new DateTimePicker
        {
            Dock = DockStyle.Fill,
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "dd-MM-yyyy HH:mm",
            ShowUpDown = true,
            Value = current.LocalDateTime
        };
        layout.Controls.Add(picker, 0, 1);
        var clearCheckBox = new CheckBox
        {
            Text = "Opnametijd wissen en terugzetten naar handmatige review",
            AutoSize = true
        };
        layout.Controls.Add(clearCheckBox, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var okButton = new Button { Text = "Opslaan", DialogResult = DialogResult.OK, Width = 110, Height = 32 };
        var cancelButton = new Button { Text = "Annuleren", DialogResult = DialogResult.Cancel, Width = 110, Height = 32 };
        StyleButton(okButton, AccentColor, AccentHoverColor, Color.White);
        StyleButton(cancelButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);
        layout.Controls.Add(buttons, 0, 3);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var manifestPath = Path.Combine(entry.DirectoryPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            ShowOwnedMessage(this, "Manifest ontbreekt; opnametijd kan niet worden aangepast.", "Opnametijd wijzigen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var manifest = JsonSerializer.Deserialize<SessionManifest>(File.ReadAllText(manifestPath));
        if (manifest is null)
        {
            ShowOwnedMessage(this, "Manifest kan niet worden gelezen; opnametijd kan niet worden aangepast.", "Opnametijd wijzigen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (clearCheckBox.Checked)
        {
            manifest.RecordedAt = null;
            manifest.RecordedAtSource = "manual_cleared";
            manifest.RecordedAtConfidence = "unknown";
            manifest.RecordedAtConfidenceReason = "recorded_at handmatig gewist door gebruiker."; // geen vertaling
            manifest.ManualRecordedAtConfirmed = false;
            manifest.ManualRecordedAtConfirmedAt = null;
            manifest.AgendaMatchStatus = "manual_review_required";
            manifest.AgendaMatchConfidence = "unknown";
            manifest.AgendaMatchBasis = "manual_cleared";
            manifest.AgendaMatchRecordedAtUsed = null;
            manifest.AgendaMatchTimeDeltaMinutes = null;
            manifest.AgendaMatchReason = "recorded_at handmatig gewist; agenda-match vereist handmatige review."; // geen vertaling
        }
        else
        {
            manifest.RecordedAt = new DateTimeOffset(picker.Value);
            manifest.RecordedAtSource = "manual_user_corrected";
            manifest.RecordedAtConfidence = "high";
            manifest.RecordedAtConfidenceReason = "recorded_at handmatig gecorrigeerd en bevestigd door gebruiker."; // geen vertaling
            manifest.ManualRecordedAtConfirmed = true;
            manifest.ManualRecordedAtConfirmedAt = DateTimeOffset.Now;
            manifest.AgendaMatchStatus = "match_candidate";
            manifest.AgendaMatchConfidence = "high";
            manifest.AgendaMatchBasis = "manual_user_corrected_recorded_at";
            manifest.AgendaMatchRecordedAtUsed = manifest.RecordedAt;
            manifest.AgendaMatchTimeDeltaMinutes = null;
            manifest.AgendaMatchReason = "handmatig gecorrigeerde opnametijd beschikbaar voor nieuwe agenda-match."; // geen vertaling
        }
        manifest.AgendaMatchCandidates = new List<AgendaMatchCandidateSnapshot>();
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        var auditAttemptAt = DateTimeOffset.UtcNow;
        var auditWritten = AppendCaptureTimeAudit(entry.DirectoryPath, new
        {
            timestamp_utc = auditAttemptAt,
            action = clearCheckBox.Checked ? "manual_recorded_at_cleared" : "manual_recorded_at_corrected",
            previous_recorded_at = entry.RecordedAt,
            new_recorded_at = manifest.RecordedAt,
            recorded_at_source = manifest.RecordedAtSource,
            recorded_at_confidence = manifest.RecordedAtConfidence,
            user_confirmed = true,
            external_writes_performed = false
        });
        manifest.CaptureTimeAuditStatus = auditWritten ? "written" : "failed";
        manifest.CaptureTimeAuditLastAttemptAt = auditAttemptAt;
        manifest.CaptureTimeAuditError = auditWritten ? null : "capture_time_audit_write_failed";
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        var metadata = LibrarySessionMetadataService.Load(entry.DirectoryPath);
        metadata.ImportInboxStatus = manifest.AgendaMatchStatus ?? "manual_review_required";
        metadata.RecordedAtConfidenceNote = clearCheckBox.Checked
            ? "recorded_at handmatig gewist; agenda-match vereist handmatige review." // geen vertaling
            : "recorded_at handmatig gecorrigeerd door gebruiker; gebruik deze tijd voor nieuwe agenda-match."; // geen vertaling
        LibrarySessionMetadataService.Save(entry.DirectoryPath, metadata);

        _ = RunLibraryUiActionAsync("Bibliotheek vernieuwen", ct => RefreshLibraryAsync(ct));
    }

    private static bool AppendCaptureTimeAudit(string sessionDirectory, object auditEntry)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sessionDirectory) || !Directory.Exists(sessionDirectory))
            {
                return false;
            }

            var path = Path.Combine(sessionDirectory, "capture_time_corrections.jsonl");
            var json = JsonSerializer.Serialize(auditEntry, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            SessionFiles.AppendAllText(path, json + Environment.NewLine);
            return true;
        }
        catch (Exception ex)
        {
            TryAppendCaptureTimeAuditFailure(sessionDirectory, ex);
            return false;
        }
    }

    private static void TryAppendCaptureTimeAuditFailure(string sessionDirectory, Exception exception)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sessionDirectory) || !Directory.Exists(sessionDirectory))
            {
                return;
            }

            var line = JsonSerializer.Serialize(new
            {
                timestamp_utc = DateTimeOffset.UtcNow,
                component = "capture_time_audit",
                status = "failed",
                error = "capture_time_audit_write_failed",
                exception_type = exception.GetType().Name
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            File.AppendAllText(Path.Combine(sessionDirectory, "session.log"), line + Environment.NewLine);
        }
        catch
        {
            // Best effort: de gebruikerscorrectie mag niet blokkeren op ondersteunende failure logging.
        }
    }

    private static string? FirstExistingPath(params string?[] candidates)
        => candidates.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && File.Exists(x));

    private sealed class LibraryDetailSnapshot
    {
        public string TranscriptPreview { get; init; } = string.Empty;
        public string ImportStatusPreview { get; init; } = string.Empty;
    }
}