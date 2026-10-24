using System.Drawing;
using LocalCaptureAgent.Controls;
using LocalCaptureAgent.Models;
using LocalCaptureAgent.Services;

namespace LocalCaptureAgent;

public sealed partial class MainForm
{
    private readonly System.Windows.Forms.Timer _meetingDetectionTimer = new() { Interval = 5000 };
    private readonly Panel _meetingDetectionBannerPanel = new() { Dock = DockStyle.Fill, Visible = false, Height = 44, BackColor = Color.FromArgb(255, 248, 230), Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0, 0, 0, 8) };
    private readonly Label _meetingDetectionBannerLabel = new() { AutoSize = true, MaximumSize = new Size(780, 0), ForeColor = Color.FromArgb(124, 75, 0), Margin = new Padding(0, 6, 12, 0) };
    private readonly Button _meetingDetectionDismissButton = new() { Text = "Negeren", Width = 96, Height = 30 };
    private bool _launchMinimizedRequested;
    private string? _meetingDetectionStableKey;
    private DateTimeOffset? _meetingDetectionStableSince;
    private string? _meetingDetectionDismissedKey;
    private DateTimeOffset _meetingDetectionDismissedUntil = DateTimeOffset.MinValue;
    private MeetingDetectionService.DetectionResult? _pendingMeetingDetection;

    private Control BuildMeetingDetectionBanner()
    {
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = _meetingDetectionBannerPanel.BackColor,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.Controls.Add(_meetingDetectionBannerLabel);
        layout.Controls.Add(_meetingDetectionDismissButton);
        _meetingDetectionBannerPanel.Controls.Add(layout);
        return _meetingDetectionBannerPanel;
    }

    private void ConfigureAssistiveFeatures()
    {
        StyleButton(_meetingDetectionDismissButton, Color.FromArgb(250, 237, 205), Color.FromArgb(246, 227, 181), Color.FromArgb(124, 75, 0));
        _meetingDetectionTimer.Tick += (_, _) => EvaluateMeetingDetection();
        _meetingDetectionDismissButton.Click += (_, _) => DismissMeetingDetection(currentOnly: false);
    }

    private void ApplyInitialWindowLaunchState()
    {
        if (!_launchMinimizedRequested)
        {
            return;
        }

        BeginInvoke(new Action(() =>
        {
            WindowState = FormWindowState.Minimized;
            if (_appSettings.Ui.MinimizeToTray || _appSettings.Ui.StartMinimized)
            {
                HideToTray(showBalloon: false);
            }
        }));
    }

    private void EnsureWindowsStartupRegistration()
    {
        try
        {
            WindowsStartupService.Apply(_appSettings.Ui.StartWithWindows, _appSettings.Ui.StartMinimized);
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Windows startup registration failed", ex);
        }
    }

    private void UpdateMeetingDetectionTimerState()
    {
        if (_appSettings.Ui.MeetingDetection.Enabled)
        {
            _meetingDetectionTimer.Start();
        }
        else
        {
            _meetingDetectionTimer.Stop();
            _pendingMeetingDetection = null;
            _meetingDetectionBannerPanel.Visible = false;
        }
    }

    private void EvaluateMeetingDetection()
    {
        if (!_appSettings.Ui.MeetingDetection.Enabled || _session is not null || _recordingBusy)
        {
            if (_pendingMeetingDetection is not null)
            {
                _pendingMeetingDetection = null;
                _meetingDetectionBannerPanel.Visible = false;
            }
            return;
        }

        var detection = MeetingDetectionService.TryDetect(_appSettings.Ui.MeetingDetection);
        if (detection is null)
        {
            _meetingDetectionStableKey = null;
            _meetingDetectionStableSince = null;
            if (_pendingMeetingDetection is not null)
            {
                _pendingMeetingDetection = null;
                _meetingDetectionBannerPanel.Visible = false;
            }
            return;
        }

        if (_meetingDetectionStableKey != detection.IdentityKey)
        {
            _meetingDetectionStableKey = detection.IdentityKey;
            _meetingDetectionStableSince = DateTimeOffset.UtcNow;
            _pendingMeetingDetection = null;
            _meetingDetectionBannerPanel.Visible = false;
            return;
        }

        if (_meetingDetectionDismissedKey == detection.IdentityKey && DateTimeOffset.UtcNow < _meetingDetectionDismissedUntil)
        {
            return;
        }

        if (!_meetingDetectionStableSince.HasValue || DateTimeOffset.UtcNow - _meetingDetectionStableSince.Value < TimeSpan.FromSeconds(_appSettings.Ui.MeetingDetection.StabilitySeconds))
        {
            return;
        }

        if (_pendingMeetingDetection?.IdentityKey == detection.IdentityKey)
        {
            return;
        }

        _pendingMeetingDetection = detection;
        _meetingDetectionBannerLabel.Text = detection.PromptText;
        _meetingDetectionBannerPanel.Visible = true;
        if (!Visible || WindowState == FormWindowState.Minimized)
        {
            _notifyIcon.ShowBalloonTip(3000, Branding.DisplayName, detection.PromptText, ToolTipIcon.Info);
        }
    }

    private void DismissMeetingDetection(bool currentOnly)
    {
        if (_pendingMeetingDetection is null)
        {
            _meetingDetectionBannerPanel.Visible = false;
            return;
        }

        if (!currentOnly)
        {
            _meetingDetectionDismissedKey = _pendingMeetingDetection.IdentityKey;
            _meetingDetectionDismissedUntil = DateTimeOffset.UtcNow.AddMinutes(_appSettings.Ui.MeetingDetection.PromptCooldownMinutes);
        }

        _pendingMeetingDetection = null;
        _meetingDetectionBannerPanel.Visible = false;
    }
}
