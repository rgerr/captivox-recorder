using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LocalCaptureAgent.Controls;

internal sealed class BadgeLabel : Label
{
    private bool _hovered;
    private Color _fillColor = Color.FromArgb(227, 239, 250);
    private Color _borderColor = Color.FromArgb(195, 216, 234);
    private Color _textColor = Color.FromArgb(14, 99, 156);

    public BadgeLabel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint |
                 ControlStyles.SupportsTransparentBackColor, true);

        AutoSize = true;
        BackColor = Color.Transparent;
        Font = new Font("Segoe UI", 8.75F, FontStyle.Bold, GraphicsUnit.Point);
        Padding = new Padding(10, 4, 10, 4);
        Margin = new Padding(0, 8, 0, 0);
        ForeColor = _textColor;
        Text = "Free";
    }

    public Color FillColor
    {
        get => _fillColor;
        set
        {
            _fillColor = value;
            Invalidate();
        }
    }

    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            _borderColor = value;
            Invalidate();
        }
    }

    public Color TextColor
    {
        get => _textColor;
        set
        {
            _textColor = value;
            ForeColor = value;
            Invalidate();
        }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
        PerformLayout();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Invalidate();
        PerformLayout();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        base.OnMouseLeave(e);
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var textSize = TextRenderer.MeasureText(Text ?? string.Empty, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        return new Size(textSize.Width + Padding.Horizontal + 8, textSize.Height + Padding.Vertical + 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = ClientRectangle;
        if (rect.Width <= 1 || rect.Height <= 1)
        {
            return;
        }

        rect.Inflate(-1, -1);
        var radius = Math.Max(8, rect.Height / 2);
        var fillColor = Enabled ? (_hovered ? ControlPaint.Light(FillColor, 0.02f) : FillColor) : ControlPaint.Light(FillColor, 0.08f);
        var textColor = Enabled ? TextColor : ControlPaint.Dark(TextColor, 0.12f);

        using (var path = CreateRoundedRectanglePath(rect, radius))
        using (var fillBrush = new SolidBrush(fillColor))
        using (var borderPen = new Pen(BorderColor, 1f))
        {
            e.Graphics.FillPath(fillBrush, path);
            e.Graphics.DrawPath(borderPen, path);
        }

        var textRect = Rectangle.Inflate(rect, -Padding.Left, -Padding.Top);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            textRect,
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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
}
