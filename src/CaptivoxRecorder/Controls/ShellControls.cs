using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LocalCaptureAgent.Services;

namespace LocalCaptureAgent.Controls;

// Bouwstenen van de nieuwe schil (bovenbalk, menu links, kaarten). Een kleur-, maat- en vormtaal voor het hele venster,
// zodat schermen niet elk hun eigen kleuren en knoppen hebben.
internal static class ShellPalette
{
    public static readonly Color Ground = Color.FromArgb(244, 245, 247);
    public static readonly Color Surface = Color.White;
    public static readonly Color Line = Color.FromArgb(221, 225, 230);
    public static readonly Color LineSoft = Color.FromArgb(238, 240, 243);
    public static readonly Color Text = Color.FromArgb(23, 32, 42);
    public static readonly Color TextSecondary = Color.FromArgb(75, 85, 99);
    public static readonly Color Accent = Color.FromArgb(11, 93, 122);
    public static readonly Color AccentSoft = Color.FromArgb(228, 239, 243);
    public static readonly Color Hover = Color.FromArgb(238, 241, 244);
    public static readonly Color Record = Color.FromArgb(196, 43, 28);
    public static readonly Color RecordSoft = Color.FromArgb(253, 236, 234);
    public static readonly Color Ok = Color.FromArgb(30, 122, 60);
    public static readonly Color Busy = Color.FromArgb(29, 95, 168);
    public static readonly Color Wait = Color.FromArgb(154, 91, 0);
    public static readonly Color WaitSoft = Color.FromArgb(251, 241, 226);
    public static readonly Color Bad = Color.FromArgb(180, 35, 24);
    public static readonly Color BadSoft = Color.FromArgb(251, 230, 228);


    // De eerste ouder met een dekkende kleur (een doorzichtige ouder gaf zwarte hoekjes).
    public static Color EffectiveBackColor(Control? control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (current.BackColor.A == 255) return current.BackColor;
        }

        return Ground;
    }

    public static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1f, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal enum ShellIcon
{
    None,
    Home,
    Calendar,
    Checklist,
    Bell,
    Sliders,
    Mail,
    Clock,
    Check,
    Microphone,
    Speaker
}

// Eenvoudige lijnpictogrammen (24 x 24 raster), geschaald naar het vak.
internal static class ShellIconPainter
{
    public static void Draw(Graphics g, ShellIcon icon, RectangleF box, Color color, float strokeWidth = 1.8f)
    {
        if (icon == ShellIcon.None || box.Width <= 2 || box.Height <= 2)
        {
            return;
        }

        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(box.X, box.Y);
        g.ScaleTransform(box.Width / 24f, box.Height / 24f);
        using var pen = new Pen(color, strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (icon)
        {
            case ShellIcon.Home:
                g.DrawLines(pen, new[] { new PointF(3, 11), new PointF(12, 4), new PointF(21, 11), new PointF(21, 20), new PointF(15, 20), new PointF(15, 14), new PointF(9, 14), new PointF(9, 20), new PointF(3, 20), new PointF(3, 11) });
                break;
            case ShellIcon.Calendar:
                using (var path = ShellPalette.RoundedRectangle(new RectangleF(3, 5, 18, 16), 2))
                {
                    g.DrawPath(pen, path);
                }

                g.DrawLine(pen, 3, 10, 21, 10);
                g.DrawLine(pen, 8, 3, 8, 7);
                g.DrawLine(pen, 16, 3, 16, 7);
                break;
            case ShellIcon.Checklist:
                foreach (var y in new[] { 6f, 12f, 18f })
                {
                    g.DrawLine(pen, 10, y, 20, y);
                    g.DrawLines(pen, new[] { new PointF(3.5f, y), new PointF(5f, y + 1.5f), new PointF(7.5f, y - 1f) });
                }

                break;
            case ShellIcon.Bell:
                g.DrawLines(pen, new[] { new PointF(6, 16), new PointF(6, 11) });
                g.DrawArc(pen, 6, 5, 12, 12, 180, 180);
                g.DrawLines(pen, new[] { new PointF(18, 11), new PointF(18, 16), new PointF(20, 18), new PointF(4, 18), new PointF(6, 16) });
                g.DrawArc(pen, 10, 18, 4, 4, 0, 180);
                break;
            case ShellIcon.Sliders:
                g.DrawLine(pen, 4, 6, 13, 6);
                g.DrawLine(pen, 17, 6, 20, 6);
                g.DrawLine(pen, 4, 12, 7, 12);
                g.DrawLine(pen, 11, 12, 20, 12);
                g.DrawLine(pen, 4, 18, 15, 18);
                g.DrawEllipse(pen, 13, 4, 4, 4);
                g.DrawEllipse(pen, 7, 10, 4, 4);
                g.DrawEllipse(pen, 15, 16, 4, 4);
                break;
            case ShellIcon.Mail:
                using (var path = ShellPalette.RoundedRectangle(new RectangleF(3, 5, 18, 14), 2))
                {
                    g.DrawPath(pen, path);
                }

                g.DrawLines(pen, new[] { new PointF(3, 7), new PointF(12, 13), new PointF(21, 7) });
                break;
            case ShellIcon.Clock:
                g.DrawEllipse(pen, 3, 3, 18, 18);
                g.DrawLines(pen, new[] { new PointF(12, 7), new PointF(12, 12), new PointF(15, 14) });
                break;
            case ShellIcon.Check:
                g.DrawLines(pen, new[] { new PointF(5, 12), new PointF(10, 17), new PointF(19, 7) });
                break;
            case ShellIcon.Microphone:
                using (var path = ShellPalette.RoundedRectangle(new RectangleF(9, 3, 6, 11), 3))
                {
                    g.DrawPath(pen, path);
                }

                g.DrawArc(pen, 5, 4, 14, 14, 0, 180);
                g.DrawLine(pen, 12, 18, 12, 21);
                break;
            case ShellIcon.Speaker:
                g.DrawLines(pen, new[] { new PointF(4, 9), new PointF(8, 9), new PointF(13, 5), new PointF(13, 19), new PointF(8, 15), new PointF(4, 15), new PointF(4, 9) });
                g.DrawArc(pen, 13, 9, 6, 6, -60, 120);
                break;
        }

        g.Restore(state);
    }
}

// Een witte kaart met afgeronde hoeken en een dunne rand.
internal sealed class ShellCard : Panel
{
    public int CornerRadius { get; set; } = 12;

    public Color BorderColor { get; set; } = ShellPalette.Line;

    public ShellCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = ShellPalette.Surface;
        Padding = new Padding(20, 18, 20, 18);
        Margin = new Padding(0, 0, 0, 16);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width <= 2 || Height <= 2)
        {
            return;
        }

        using var path = ShellPalette.RoundedRectangle(new RectangleF(0, 0, Width, Height), LogicalToDeviceUnits(CornerRadius));
        Region?.Dispose();
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = ShellPalette.RoundedRectangle(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), LogicalToDeviceUnits(CornerRadius));
        using var pen = new Pen(BorderColor, 1f);
        e.Graphics.DrawPath(pen, path);
    }
}

// Een menuknop links: pictogram, tekst en eventueel een teller. Een echte knop (Tab en Enter werken).
internal sealed class ShellNavButton : Button
{
    private bool _hovered;
    private bool _selected;
    private int _count;

    public ShellIcon Icon { get; set; }

    public string Key { get; set; } = string.Empty;

    public Color? DotColor { get; set; }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            Invalidate();
        }
    }

    private static readonly Font CountFont = new("Segoe UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);

    // Bij een afgekapte telling: "50+".
    public bool CountCapped { get; set; }

    // Wat de telling betekent, voor de schermlezer ("Overleggen, 3 open").
    public string CountLabel { get; set; } = string.Empty;

    public int Count
    {
        get => _count;
        set
        {
            if (_count == value)
            {
                return;
            }

            _count = value;
            AccessibleName = _count > 0 && CountLabel.Length > 0 ? $"{Text}, {_count}{(CountCapped ? " of meer" : string.Empty)} {CountLabel}" : Text;
            Invalidate();
        }
    }

    public ShellNavButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = ShellPalette.Surface;
        ForeColor = ShellPalette.Text;
        Font = new Font("Segoe UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point);
        Cursor = Cursors.Hand;
        Height = 44;
        Margin = new Padding(0, 0, 0, 4);
        TextAlign = ContentAlignment.MiddleLeft;
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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var back = ShellPalette.EffectiveBackColor(Parent);
        g.Clear(back);
        var rect = new RectangleF(0, 0, Width - 1, Height - 1);
        var fill = _selected ? ShellPalette.AccentSoft : (_hovered ? ShellPalette.Hover : back);
        using (var path = ShellPalette.RoundedRectangle(rect, LogicalToDeviceUnits(8)))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
        }

        var color = _selected ? ShellPalette.Accent : (Enabled ? ShellPalette.Text : ShellPalette.TextSecondary);
        var pad = LogicalToDeviceUnits(12);
        var iconSize = LogicalToDeviceUnits(20);
        float textX = pad;
        if (DotColor is { } dot)
        {
            var d = LogicalToDeviceUnits(10);
            using var dotBrush = new SolidBrush(dot);
            g.FillEllipse(dotBrush, pad + (iconSize - d) / 2f, (Height - d) / 2f, d, d);
            textX = pad + iconSize + LogicalToDeviceUnits(12);
        }
        else if (Icon != ShellIcon.None)
        {
            ShellIconPainter.Draw(g, Icon, new RectangleF(pad, (Height - iconSize) / 2f, iconSize, iconSize), color);
            textX = pad + iconSize + LogicalToDeviceUnits(12);
        }

        var countWidth = 0f;
        if (_count > 0)
        {
            var countText = _count > 99 ? "99+" : _count.ToString(System.Globalization.CultureInfo.InvariantCulture) + (CountCapped ? "+" : string.Empty);
            var countFont = CountFont;
            var size = g.MeasureString(countText, countFont);
            var w = Math.Max(LogicalToDeviceUnits(22), size.Width + LogicalToDeviceUnits(10));
            var h = LogicalToDeviceUnits(20);
            var badge = new RectangleF(Width - pad - w, (Height - h) / 2f, w, h);
            using (var badgePath = ShellPalette.RoundedRectangle(badge, h / 2f))
            using (var badgeBrush = new SolidBrush(ShellPalette.Wait))
            {
                g.FillPath(badgeBrush, badgePath);
            }

            TextRenderer.DrawText(g, countText, countFont, Rectangle.Round(badge), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            countWidth = w + LogicalToDeviceUnits(8);
        }

        using var font = new Font(Font, _selected ? FontStyle.Bold : FontStyle.Regular);
        var textRect = Rectangle.Round(new RectangleF(textX, 0, Math.Max(0, Width - textX - pad - countWidth), Height));
        TextRenderer.DrawText(g, Text, font, textRect, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (Focused && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -3, -3));
        }
    }
}

// Een regel in een lijst waarop dubbelklikken iets opent. Windows kopieert bij een dubbelklik op een gewoon label de
// tekst naar het klembord; dat mag hier niet stil gebeuren, dus de dubbelklik wordt hier zelf afgehandeld.
internal class ShellRowLabel : Label
{
    private const int WmLButtonDblClk = 0x0203;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmLButtonDblClk)
        {
            OnDoubleClick(EventArgs.Empty);
            return;
        }

        base.WndProc(ref m);
    }
}

// Een label als pil: de organisatie of een korte status. Ook hier geen klembord bij een dubbelklik (zie ShellRowLabel).
internal sealed class ShellPill : ShellRowLabel
{
    // Gedeeld: de labels worden bij elke verversing opnieuw gemaakt.
    private static readonly Font PillFont = new("Segoe UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);

    public Color PillColor { get; set; } = ShellPalette.LineSoft;

    public ShellPill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        AutoSize = true;
        AccessibleDescription = "skip-ui-localization";
        Font = PillFont;
        Padding = new Padding(8, 2, 8, 2);
        Margin = new Padding(0, 2, 6, 2);
        UseMnemonic = false;
    }


    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(ShellPalette.EffectiveBackColor(Parent));
        using (var path = ShellPalette.RoundedRectangle(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f))
        using (var brush = new SolidBrush(PillColor))
        {
            g.FillPath(brush, path);
        }

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

// Een gekleurd rondje (status).
internal sealed class ShellDot : Control
{
    public Color DotColor { get; set; } = ShellPalette.Ok;

    public bool Halo { get; set; }

    public ShellDot()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(14, 14);
        Margin = new Padding(0, 4, 8, 0);
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(ShellPalette.EffectiveBackColor(Parent));
        var d = Math.Min(Width, Height) - 1;
        if (Halo)
        {
            using var halo = new SolidBrush(Color.FromArgb(70, DotColor));
            g.FillEllipse(halo, 0, 0, d, d);
        }

        var inner = Halo ? d * 0.62f : d;
        using var brush = new SolidBrush(DotColor);
        g.FillEllipse(brush, (d - inner) / 2f, (d - inner) / 2f, inner, inner);
    }
}

// Een pictogram in een gekleurd vlak (de rijen van "Wacht op jou").
internal sealed class ShellIconTile : Control
{
    public ShellIcon Glyph { get; set; }

    public Color TileColor { get; set; } = ShellPalette.AccentSoft;

    public ShellIconTile()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Size = new Size(40, 40);
        Margin = new Padding(0, 0, 14, 0);
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(ShellPalette.EffectiveBackColor(Parent));
        using (var path = ShellPalette.RoundedRectangle(new RectangleF(0, 0, Width - 1, Height - 1), LogicalToDeviceUnits(10)))
        using (var brush = new SolidBrush(TileColor))
        {
            g.FillPath(brush, path);
        }

        var size = Width * 0.5f;
        ShellIconPainter.Draw(g, Glyph, new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size), ForeColor);
    }
}

// Pagina's zonder tabbladkoppen: het menu links kiest de pagina. Tabbladen blijven bestaan, zodat bestaande code die op een
// tabblad navigeert gewoon blijft werken.
internal sealed class ShellPageHost : TabControl
{
    private const int TcmAdjustRect = 0x1328;

    public ShellPageHost()
    {
        // Zonder koppen is de houder zelf geen stap in de tabvolgorde.
        TabStop = false;
        // Met veel pagina's toont een TabControl op een regel pijltjes om te schuiven, ook als de koppen verborgen zijn.
        Multiline = true;
    }

    // Ctrl+Tab en Ctrl+PageUp/PageDown wisselden onzichtbaar van pagina; de plekken links zijn de enige navigatie.
    protected override void OnKeyDown(KeyEventArgs ke)
    {
        if (ke.Control && ke.KeyCode is Keys.Tab or Keys.PageUp or Keys.PageDown)
        {
            ke.Handled = true;
            return;
        }

        base.OnKeyDown(ke);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == TcmAdjustRect && !DesignMode)
        {
            m.Result = (IntPtr)1;
            return;
        }

        base.WndProc(ref m);
    }
}

// Een lijst met zelf getekende regels (Overleggen, Beoordelen). Een control in plaats van een control per regel:
// snel bij veel regels, pijltjestoetsen werken vanzelf, en een schermlezer leest per regel ToString() van het item voor.
internal sealed class ShellRowList : ListBox
{
    // Tekent een regel: graphics, vlak, item, geselecteerd.
    public Action<Graphics, Rectangle, object, bool>? Painter { get; set; }

    // Regels die niet te kiezen zijn (kopjes): de selectie springt erover heen.
    public Func<object, bool>? IsSelectable { get; set; }

    public ShellRowList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        BackColor = ShellPalette.Surface;
        ItemHeight = 60;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var g = e.Graphics;
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using (var back = new SolidBrush(selected ? ShellPalette.AccentSoft : ShellPalette.Surface))
        {
            g.FillRectangle(back, e.Bounds);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        Painter?.Invoke(g, e.Bounds, Items[e.Index], selected);
        if (Focused && selected)
        {
            using var focus = new Pen(ShellPalette.Accent, 2f);
            g.DrawRectangle(focus, e.Bounds.X + 1, e.Bounds.Y + 1, e.Bounds.Width - 3, e.Bounds.Height - 3);
        }
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        if (SelectedIndex >= 0 && IsSelectable is not null && !IsSelectable(Items[SelectedIndex]))
        {
            // Over een kopje heen, in de richting van de vorige keuze.
            var direction = SelectedIndex >= _lastIndex ? 1 : -1;
            var next = SelectedIndex + direction;
            while (next >= 0 && next < Items.Count && !IsSelectable(Items[next])) next += direction;
            if (next < 0 || next >= Items.Count)
            {
                next = SelectedIndex - direction;
                while (next >= 0 && next < Items.Count && !IsSelectable(Items[next])) next -= direction;
            }

            if (next >= 0 && next < Items.Count && next != SelectedIndex)
            {
                SelectedIndex = next;
                return;
            }
        }

        _lastIndex = SelectedIndex;
        base.OnSelectedIndexChanged(e);
    }

    private int _lastIndex = -1;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }
}

// Een dunne voortgangsbalk (beoordelen: hoeveel onderdelen bekeken).
internal sealed class ShellProgressBar : Control
{
    private double _fraction;

    public double Fraction
    {
        get => _fraction;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(clamped - _fraction) < 0.0001) return;
            _fraction = clamped;
            Invalidate();
        }
    }

    public ShellProgressBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Height = 6;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(ShellPalette.EffectiveBackColor(Parent));
        var radius = Height / 2f;
        using (var track = ShellPalette.RoundedRectangle(new RectangleF(0, 0, Width - 1, Height - 1), radius))
        using (var brush = new SolidBrush(Color.FromArgb(227, 231, 235)))
        {
            g.FillPath(brush, track);
        }

        if (_fraction <= 0) return;
        var width = Math.Max(Height, (float)((Width - 1) * _fraction));
        using var fill = ShellPalette.RoundedRectangle(new RectangleF(0, 0, width, Height - 1), radius);
        using var accent = new SolidBrush(ShellPalette.Accent);
        g.FillPath(accent, fill);
    }
}
