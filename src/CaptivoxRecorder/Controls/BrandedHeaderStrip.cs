using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;

namespace LocalCaptureAgent.Controls;

internal sealed class BrandedHeaderStrip : Panel
{
    private readonly PictureBox _logoBox;
    private readonly Label _productLabel;
    private readonly Label _suffixLabel;
    private readonly BufferedTableLayoutPanel _layout;
    private readonly BufferedPanel _logoHost;
    private readonly BufferedTableLayoutPanel _textStack;

    public BrandedHeaderStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        Dock = DockStyle.Top;
        Margin = new Padding(0);
        BackColor = Color.Transparent;

        _layout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _logoHost = new BufferedPanel
        {
            Margin = new Padding(0),
            Padding = new Padding(0),
            Anchor = AnchorStyles.Left,
            BackColor = Color.Transparent
        };

        _logoBox = new PictureBox
        {
            Image = Branding.CreateHeaderLogoImage(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0),
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(6, 12, 20),
            TabStop = false
        };
        _logoHost.Controls.Add(_logoBox);

        _textStack = new BufferedTableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Left
        };
        _textStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _textStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _productLabel = new Label
        {
            Text = "Captivox",
            AutoSize = true,
            ForeColor = Color.White,
            Margin = new Padding(0, 0, 0, 2),
            TextAlign = ContentAlignment.BottomLeft
        };
        _suffixLabel = new Label
        {
            Text = "Recorder",
            AutoSize = true,
            ForeColor = Color.White,
            Margin = new Padding(0),
            TextAlign = ContentAlignment.TopLeft
        };

        _textStack.Controls.Add(_productLabel, 0, 0);
        _textStack.Controls.Add(_suffixLabel, 0, 1);
        _layout.Controls.Add(_logoHost, 0, 0);
        _layout.Controls.Add(_textStack, 1, 0);
        Controls.Add(_layout);

        ApplyMetrics();
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        ApplyMetrics();
    }

    private void ApplyMetrics()
    {
        var scale = DeviceDpi > 0 ? DeviceDpi / 96f : 1f;
        var horizontalPad = Math.Max(14, (int)Math.Round(14 * scale));
        var verticalPad = Math.Max(6, (int)Math.Round(6 * scale));
        var headerHeight = Math.Max(68, (int)Math.Round(68 * scale));
        var logoSize = Math.Max(44, (int)Math.Round(44 * scale));
        var logoGap = Math.Max(12, (int)Math.Round(12 * scale));

        Padding = new Padding(horizontalPad, verticalPad, horizontalPad, verticalPad);
        Height = headerHeight;
        MinimumSize = new Size(0, headerHeight);

        _logoHost.Size = new Size(logoSize, logoSize);
        _logoHost.MinimumSize = new Size(logoSize, logoSize);
        _logoHost.Margin = new Padding(0, 0, logoGap, 0);

        _productLabel.Font = new Font("Segoe UI", 14.5F * scale, FontStyle.Bold, GraphicsUnit.Point);
        _productLabel.ForeColor = Color.White;
        _suffixLabel.Font = new Font("Segoe UI", 11.0F * scale, FontStyle.Bold, GraphicsUnit.Point);
        _suffixLabel.ForeColor = Color.FromArgb(236, 241, 248);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            base.OnPaintBackground(e);
            return;
        }

        using var brush = new LinearGradientBrush(rect, Color.FromArgb(6, 12, 20), Color.FromArgb(5, 10, 17), LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(brush, rect);

        var sheenRect = new Rectangle(rect.Width / 2, 0, Math.Max(1, rect.Width / 4), rect.Height);
        using var sheenBrush = new LinearGradientBrush(sheenRect, Color.FromArgb(14, 16, 32, 52), Color.FromArgb(3, 8, 18, 28), LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(sheenBrush, sheenRect);
    }

    protected override void OnResize(System.EventArgs eventargs)
    {
        base.OnResize(eventargs);
    }
}
