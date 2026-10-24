using System.Drawing.Drawing2D;

namespace LocalCaptureAgent.Controls;

public sealed class LevelMeterControl : Control
{
    private float _value;
    private float _peakHold;
    private DateTimeOffset _lastPeakUpdate = DateTimeOffset.MinValue;

    public LevelMeterControl()
    {
        DoubleBuffered = true;
        Height = 18;
        Width = 180;
        Margin = new Padding(0, 0, 0, 8);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public float Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, 0f, 1f);
            _value = clamped;
            if (clamped >= _peakHold || (DateTimeOffset.Now - _lastPeakUpdate) > TimeSpan.FromMilliseconds(800))
            {
                _peakHold = clamped;
                _lastPeakUpdate = DateTimeOffset.Now;
            }
            else
            {
                _peakHold = Math.Max(clamped, _peakHold - 0.02f);
            }

            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        rect.Width -= 1;
        rect.Height -= 1;

        using var border = new Pen(Color.FromArgb(209, 215, 223));
        using var background = new SolidBrush(Color.FromArgb(246, 248, 251));
        e.Graphics.FillRectangle(background, rect);
        e.Graphics.DrawRectangle(border, rect);

        var fillWidth = (int)Math.Round(rect.Width * _value);
        if (fillWidth > 0)
        {
            var fillRect = new Rectangle(rect.X + 1, rect.Y + 1, Math.Max(1, fillWidth - 1), Math.Max(1, rect.Height - 1));
            using var brush = new LinearGradientBrush(fillRect, Color.FromArgb(26, 154, 93), Color.FromArgb(235, 156, 0), LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(brush, fillRect);
        }

        var peakX = rect.X + (int)Math.Round(rect.Width * _peakHold);
        peakX = Math.Clamp(peakX, rect.X + 1, rect.Right - 1);
        using var peakPen = new Pen(Color.FromArgb(183, 28, 28), 2);
        e.Graphics.DrawLine(peakPen, peakX, rect.Y + 1, peakX, rect.Bottom - 1);
    }
}
