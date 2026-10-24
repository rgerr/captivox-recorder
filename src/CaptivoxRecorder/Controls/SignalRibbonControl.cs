using System.Drawing.Drawing2D;

namespace LocalCaptureAgent.Controls;

internal sealed class SignalRibbonControl : Control
{
    private readonly Queue<float> _micHistory = new();
    private readonly Queue<float> _systemHistory = new();
    private int _maxSamples = 144;
    private bool _isRecording;

    public SignalRibbonControl()
    {
        DoubleBuffered = true;
        Height = 56;
        Margin = new Padding(0);
        BackColor = Color.White;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
    }

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (_isRecording == value)
            {
                return;
            }

            _isRecording = value;
            Invalidate();
        }
    }

    public void AddSample(float micValue, float systemValue)
    {
        Enqueue(_micHistory, micValue);
        Enqueue(_systemHistory, systemValue);
        TrimHistory();
        Invalidate();
    }

    public void ResetHistory()
    {
        _micHistory.Clear();
        _systemHistory.Clear();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width <= 0)
        {
            return;
        }

        var suggested = Math.Clamp(Width / 6, 72, 220);
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
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var backgroundBrush = new SolidBrush(Color.FromArgb(250, 251, 253));
        using var borderPen = new Pen(Color.FromArgb(228, 232, 238));
        using var centerPen = new Pen(Color.FromArgb(226, 230, 236));
        using var micBrush = new SolidBrush(Color.FromArgb(_isRecording ? 160 : 138, 210, 216, 232));
        using var systemBrush = new SolidBrush(Color.FromArgb(_isRecording ? 170 : 145, 123, 197, 201));
        using var activeIndicator = new SolidBrush(_isRecording ? Color.FromArgb(221, 79, 62) : Color.FromArgb(178, 187, 198));

        FillRoundedRect(e.Graphics, rect, 14, backgroundBrush, borderPen);

        var contentRect = Rectangle.Inflate(rect, -12, -10);
        var centerY = contentRect.Top + (contentRect.Height / 2f);
        e.Graphics.DrawLine(centerPen, contentRect.Left, centerY, contentRect.Right, centerY);

        DrawRibbon(e.Graphics, contentRect, centerY, _micHistory.ToArray(), micBrush, true);
        DrawRibbon(e.Graphics, contentRect, centerY, _systemHistory.ToArray(), systemBrush, false);

        var dotRect = new RectangleF(contentRect.Left, rect.Top + 8, 8, 8);
        e.Graphics.FillEllipse(activeIndicator, dotRect);
    }

    private static void DrawRibbon(Graphics graphics, Rectangle contentRect, float centerY, float[] values, Brush brush, bool upper)
    {
        if (values.Length == 0)
        {
            return;
        }

        var step = Math.Max(3.2f, contentRect.Width / Math.Max(1f, values.Length - 1));
        var usableHeight = Math.Max(8f, contentRect.Height * 0.38f);

        for (var i = 0; i < values.Length; i++)
        {
            var x = contentRect.Left + (i * step);
            if (x > contentRect.Right)
            {
                break;
            }

            var amplitude = Math.Max(2.0f, values[i] * usableHeight);
            var width = 2.0f;
            var height = Math.Max(2.0f, amplitude);
            var y = upper ? centerY - height : centerY;
            graphics.FillEllipse(brush, x, y, width, height);
        }
    }

    private static void FillRoundedRect(Graphics graphics, Rectangle rect, int radius, Brush fill, Pen border)
    {
        using var path = CreateRoundedRectanglePath(rect, radius);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);
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

    private static void Enqueue(Queue<float> queue, float value)
    {
        queue.Enqueue(Math.Clamp(value, 0f, 1f));
    }

    private void TrimHistory()
    {
        while (_micHistory.Count > _maxSamples)
        {
            _micHistory.Dequeue();
        }

        while (_systemHistory.Count > _maxSamples)
        {
            _systemHistory.Dequeue();
        }
    }
}
