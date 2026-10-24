using System.Drawing;
using System.Windows.Forms;

namespace LocalCaptureAgent.Controls;

internal sealed class InsetPanel : Panel
{
    public Color BorderColor { get; set; } = Color.FromArgb(225, 231, 238);

    public InsetPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        ResizeRedraw = true;
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
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(pen, rect);
    }
}
