using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LocalCaptureAgent.Controls;

internal enum PrimaryActionVisualState
{
    Start,
    Stop,
    Busy
}

internal sealed class PrimaryActionButton : Button
{
    private bool _hovered;
    private bool _pressed;
    private PrimaryActionVisualState _visualState = PrimaryActionVisualState.Start;

    public PrimaryActionVisualState VisualState
    {
        get => _visualState;
        set
        {
            if (_visualState == value)
            {
                return;
            }

            _visualState = value;
            Invalidate();
        }
    }

    public PrimaryActionButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint |
                 ControlStyles.SupportsTransparentBackColor, true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = new Font("Segoe UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
        ForeColor = Color.White;
        BackColor = Color.White;
        Cursor = Cursors.Hand;
        Size = new Size(212, 46);
        Margin = new Padding(0);
        Text = "Opname starten";
    }

    protected override void OnMouseEnter(System.EventArgs e)
    {
        _hovered = true;
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(System.EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        base.OnMouseLeave(e);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        base.OnMouseDown(mevent);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        base.OnMouseUp(mevent);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = ClientRectangle;
        if (rect.Width <= 1 || rect.Height <= 1)
        {
            return;
        }

        rect.Inflate(-1, -1);
        var radius = Math.Max(10, rect.Height / 3);

        var (baseColor, hoverColor, borderColor, indicatorColor) = GetPalette();
        var disabledFill = ControlPaint.Light(baseColor, 0.15f);
        var fillColor = !Enabled ? disabledFill : (_pressed ? ControlPaint.Dark(baseColor, 0.08f) : (_hovered ? hoverColor : baseColor));
        var textColor = Enabled ? Color.White : Color.FromArgb(244, 247, 251);

        using (var path = CreateRoundedRectanglePath(rect, radius))
        using (var fill = new SolidBrush(fillColor))
        using (var border = new Pen(borderColor, 1.0f))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        DrawIndicator(e.Graphics, rect, indicatorColor, textColor);
        DrawCaption(e.Graphics, rect, textColor);

        if (Focused && ShowFocusCues)
        {
            var focus = Rectangle.Inflate(rect, -4, -4);
            ControlPaint.DrawFocusRectangle(e.Graphics, focus, textColor, fillColor);
        }
    }

    protected override void OnResize(System.EventArgs e)
    {
        base.OnResize(e);
        ApplyRoundedRegion();
    }

    private void ApplyRoundedRegion()
    {
        var rect = ClientRectangle;
        if (rect.Width <= 1 || rect.Height <= 1)
        {
            return;
        }

        rect.Inflate(-1, -1);
        var radius = Math.Max(10, rect.Height / 3);
        using var path = CreateRoundedRectanglePath(rect, radius);
        Region?.Dispose();
        Region = new Region(path);
    }

    private void DrawCaption(Graphics graphics, Rectangle rect, Color textColor)
    {
        var textRect = Rectangle.Inflate(rect, -16, 0);
        textRect.X += 18;
        using var brush = new SolidBrush(textColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(Text, Font, brush, textRect, format);
    }

    private void DrawIndicator(Graphics graphics, Rectangle rect, Color indicatorColor, Color contrastColor)
    {
        var size = Math.Max(10, rect.Height / 5);
        var centerX = rect.X + 24 + (size / 2);
        var centerY = rect.Top + rect.Height / 2;

        switch (VisualState)
        {
            case PrimaryActionVisualState.Start:
                using (var brush = new SolidBrush(indicatorColor))
                {
                    graphics.FillEllipse(brush, centerX - size / 2, centerY - size / 2, size, size);
                }
                break;
            case PrimaryActionVisualState.Stop:
                using (var brush = new SolidBrush(contrastColor))
                {
                    graphics.FillRectangle(brush, centerX - size / 2, centerY - size / 2, size, size);
                }
                break;
            case PrimaryActionVisualState.Busy:
                using (var pen = new Pen(contrastColor, 1.8f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    var busySize = size + 2;
                    graphics.DrawArc(pen, centerX - busySize / 2, centerY - busySize / 2, busySize, busySize, 25, 290);
                }
                break;
        }
    }

    private (Color baseColor, Color hoverColor, Color borderColor, Color indicatorColor) GetPalette()
    {
        return VisualState switch
        {
            PrimaryActionVisualState.Stop => (Color.FromArgb(181, 35, 35), Color.FromArgb(158, 29, 29), Color.FromArgb(125, 23, 23), Color.White),
            PrimaryActionVisualState.Busy => (Color.FromArgb(170, 29, 33), Color.FromArgb(152, 24, 29), Color.FromArgb(120, 20, 24), Color.FromArgb(255, 246, 246)),
            // Opnemen is rood met een witte stip (zoals overal in de schil).
            _ => (Color.FromArgb(196, 43, 28), Color.FromArgb(170, 36, 23), Color.FromArgb(143, 29, 18), Color.White)
        };
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
