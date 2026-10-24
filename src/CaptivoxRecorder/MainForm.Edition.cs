using System.Diagnostics;
using System.Drawing;
using LocalCaptureAgent.Controls;
using LocalCaptureAgent.Models;
using LocalCaptureAgent.Services;

namespace LocalCaptureAgent;

public sealed partial class MainForm
{
    private const string PublicProjectUrl = "https://github.com/rgerr/captivox-recorder";

    private readonly Label _editionBadgeLabel = new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
        ForeColor = Color.FromArgb(73, 82, 96),
        Margin = new Padding(0, 6, 0, 0),
        Text = "Publiek"
    };

    private readonly Label _editionDetailLabel = new()
    {
        AutoSize = true,
        MaximumSize = new Size(340, 0),
        Font = new Font("Segoe UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point),
        ForeColor = Color.FromArgb(73, 82, 96),
        Margin = new Padding(0, 2, 0, 0),
        Text = "Publieke productlijn actief. Basisfuncties blijven lokaal beschikbaar."
    };

    private readonly ToolStripStatusLabel _editionStatusStripLabel = new()
    {
        Text = "Publiek",
        AutoSize = false,
        Width = 180,
        TextAlign = ContentAlignment.MiddleRight
    };

    private readonly EditionService _editionService = new();
    private EditionStateSnapshot _editionState = new();

    private void RefreshEditionStateAndUi(bool attemptRevalidation = true, bool persistSettings = false)
    {
        _editionState = _editionService.GetEditionState(attemptRevalidation);
        var settingsChanged = _editionService.ApplyEditionPolicy(_appSettings, _editionState);
        if (persistSettings || settingsChanged)
        {
            SettingsStore.Save(_appSettings);
        }

        ApplyEditionStatusUi();
    }

    private void ApplyEditionStatusUi()
    {
        _editionBadgeLabel.Text = $"Productlijn: {_editionState.DisplayName}";
        _editionBadgeLabel.ForeColor = Color.FromArgb(73, 82, 96);
        _editionDetailLabel.Text = _editionState.Summary;
        _editionStatusStripLabel.Text = _editionState.DisplayName;
        _editionStatusStripLabel.ForeColor = Color.FromArgb(73, 82, 96);
    }

    private bool IsFeatureEnabled(string feature)
        => _editionState.IsFeatureEnabled(feature);

    private void ShowPremiumFeatureRequiredMessage(string featureName)
    {
        var message = $"{featureName} hoort in deze publieke productlijn beschikbaar te zijn.{Environment.NewLine}{Environment.NewLine}Als de functie toch geblokkeerd lijkt, controleer dan de lokale instellingen, prerequisites en sessiestatus.";
        ShowOwnedMessage(this, message, $"{featureName} niet beschikbaar", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private bool ShowLicenseManagementDialog(IWin32Window? owner = null, Action? onStateChanged = null)
    {
        var beforeState = _editionState.State;
        using var dialog = new Form
        {
            Text = $"{Branding.DisplayName} · Publieke productlijn",
            Icon = Branding.GetAppIconOrDefault(),
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(720, 360),
            MinimumSize = new Size(660, 320),
            AutoScaleMode = AutoScaleMode.Dpi,
            BackColor = AppBackground,
            Font = Font,
            ShowInTaskbar = false,
        };

        var currentLabel = new Label { AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Color.FromArgb(22, 28, 36) };
        var detailLabel = new Label { AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Color.FromArgb(73, 82, 96) };
        var supportLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            ForeColor = Color.FromArgb(73, 82, 96),
            Text = "Updates, bekende beperkingen en projectinformatie staan op de projectpagina. Audio, transcripties en sessieartefacten blijven lokaal op deze computer; online koppelingen zijn alleen ondersteunend."
        };

        var projectButton = new Button { Text = "Projectpagina openen", Width = 190, Height = 34 };
        var closeButton = new Button { Text = "Sluiten", Width = 110, Height = 34, DialogResult = DialogResult.OK };
        StyleButton(projectButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));
        StyleButton(closeButton, Color.FromArgb(230, 236, 242), Color.FromArgb(220, 228, 236), Color.FromArgb(39, 46, 56));

        void RefreshDialogState(string? extraMessage = null)
        {
            var snapshot = _editionService.GetEditionState(attemptRevalidation: false);
            _editionState = snapshot;
            ApplyEditionStatusUi();
            onStateChanged?.Invoke();
            currentLabel.Text = $"Huidige productlijn: {snapshot.DisplayName}";
            detailLabel.Text = string.IsNullOrWhiteSpace(extraMessage)
                ? snapshot.Detail
                : $"{snapshot.Detail}{Environment.NewLine}{Environment.NewLine}{extraMessage}";
        }

        projectButton.Click += (_, _) => OpenExternalUrl(PublicProjectUrl);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14), BackColor = AppBackground };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var infoCard = CreateCardPanel(autoSize: true);
        infoCard.Padding = new Padding(14);
        var infoLayout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, BackColor = infoCard.BackColor };
        infoLayout.Controls.Add(new Label { Text = "Publieke productlijn", Font = SectionFont, AutoSize = true, ForeColor = Color.FromArgb(22, 28, 36) }, 0, 0);
        infoLayout.Controls.Add(currentLabel, 0, 1);
        infoLayout.Controls.Add(detailLabel, 0, 2);
        infoLayout.Controls.Add(supportLabel, 0, 3);
        infoCard.Controls.Add(infoLayout);
        root.Controls.Add(infoCard, 0, 0);

        root.Controls.Add(new BufferedPanel { Dock = DockStyle.Fill, BackColor = AppBackground }, 0, 1);
        var bottom = new BufferedFlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0), BackColor = AppBackground };
        bottom.Controls.Add(closeButton);
        bottom.Controls.Add(projectButton);
        root.Controls.Add(bottom, 0, 2);

        dialog.Controls.Add(root);
        dialog.AcceptButton = closeButton;
        RefreshDialogState();

        _ = dialog.ShowDialog(ResolveDialogOwner(owner) ?? this);
        RefreshEditionStateAndUi(attemptRevalidation: false, persistSettings: true);
        return !string.Equals(beforeState, _editionState.State, StringComparison.OrdinalIgnoreCase);
    }

    private void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowOwnedMessage(this, ex.Message, "Link openen mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
