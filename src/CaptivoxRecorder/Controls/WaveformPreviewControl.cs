using System.Drawing.Drawing2D;

namespace LocalCaptureAgent.Controls;

public sealed class WaveformPreviewControl : Control
{
    private readonly Queue<float> _samples = new();
    private int _maxSamples = 120;

    public WaveformPreviewControl()
    {
        DoubleBuffered = true;
        Height = 34;
        Width = 180;
        Margin = new Padding(0, 0, 0, 8);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public int MaxSamples
    {
        get => _maxSamples;
        set
        {
            _maxSamples = Math.Clamp(value, 24, 512);
            TrimHistory();
            Invalidate();
        }
    }

    public void AddSample(float value)
    {
        var clamped = Math.Clamp(value, 0f, 1f);
        _samples.Enqueue(clamped);
        TrimHistory();
        Invalidate();
    }

    public void ResetHistory()
    {
        _samples.Clear();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width <= 0)
        {
            return;
        }

        var suggested = Math.Clamp(Width / 3, 32, 240);
        if (suggested != _maxSamples)
        {
            _maxSamples = suggested;
            TrimHistory();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var rect = ClientRectangle;
        if (rect.Width <= 1 || rect.Height <= 1)
        {
            return;
        }

        rect.Width -= 1;
        rect.Height -= 1;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(Color.FromArgb(209, 215, 223));
        using var background = new SolidBrush(Color.FromArgb(246, 248, 251));
        using var wavePen = new Pen(Color.FromArgb(14, 99, 156), 1.6f);
        using var centerPen = new Pen(Color.FromArgb(228, 232, 238), 1f);

        e.Graphics.FillRectangle(background, rect);
        e.Graphics.DrawRectangle(border, rect);

        var centerY = rect.Top + rect.Height / 2f;
        e.Graphics.DrawLine(centerPen, rect.Left + 1, centerY, rect.Right - 1, centerY);

        if (_samples.Count == 0)
        {
            return;
        }

        var values = _samples.ToArray();
        var contentLeft = rect.Left + 2;
        var contentRight = rect.Right - 2;
        var contentWidth = Math.Max(1, contentRight - contentLeft);
        var contentHeight = Math.Max(2, rect.Height - 4);
        var halfHeight = contentHeight / 2f;

        if (values.Length == 1)
        {
            var amplitude = values[0] * halfHeight;
            e.Graphics.DrawLine(wavePen, contentLeft, centerY - amplitude, contentLeft, centerY + amplitude);
            return;
        }

        var points = new PointF[values.Length * 2];
        for (var i = 0; i < values.Length; i++)
        {
            var x = contentLeft + (contentWidth * i / (float)Math.Max(1, values.Length - 1));
            var amplitude = Math.Max(1.0f, values[i] * halfHeight);
            points[i * 2] = new PointF(x, centerY - amplitude);
            points[i * 2 + 1] = new PointF(x, centerY + amplitude);
        }

        for (var i = 0; i < points.Length; i += 2)
        {
            e.Graphics.DrawLine(wavePen, points[i], points[i + 1]);
        }
    }

    private void TrimHistory()
    {
        while (_samples.Count > _maxSamples)
        {
            _samples.Dequeue();
        }
    }
}
