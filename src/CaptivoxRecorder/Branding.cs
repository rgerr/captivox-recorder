using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Captivox.Recorder;

internal static class Branding
{
    public const string DisplayName = "Captivox Recorder";
    public const string ProjectUrl = "https://github.com/rgerr/captivox-recorder";
    public const string DiagnosticsPrefix = "CaptivoxRecorder";
    public const string ExecutableBaseName = "CaptivoxRecorder";

    // Eigen map naast een eventuele andere Captivox-installatie; zie AppRuntime.GetAppDataDirectory.
    public const string AppDataFolder = "CaptivoxRecorderFree";

    private const string IconResourceName = "Captivox.Recorder.Assets.captivox-recorder.ico";
    private const string BannerResourceName = "Captivox.Recorder.Assets.captivox-banner.png";
    private const string LogoResourceName = "Captivox.Recorder.Assets.captivox-logo.png";

    private static readonly Lazy<Icon?> AppIconLazy = new(LoadIcon);
    private static readonly Lazy<Image?> BannerImageLazy = new(() => LoadImage(BannerResourceName));
    private static readonly Lazy<Image?> LogoImageLazy = new(() => LoadImage(LogoResourceName));

    public static Icon GetAppIconOrDefault()
        => AppIconLazy.Value is Icon icon ? new Icon(icon, icon.Size) : SystemIcons.Application;

    public static Image? CreateBannerImage()
        => BannerImageLazy.Value is Image image ? (Image)image.Clone() : null;

    public static Image? CreateHeaderBannerImage()
        => BannerImageLazy.Value is Image image ? CreateHeaderBannerStrip(image) : null;

    public static Image? CreateLogoImage()
        => LogoImageLazy.Value is Image image ? (Image)image.Clone() : null;

    public static Image CreateHeaderLogoImage(int size = 128)
        => CreateHeaderLogoGraphic(size);

    private static Icon? LoadIcon()
    {
        try
        {
            var assembly = typeof(Branding).Assembly;
            using var stream = assembly.GetManifestResourceStream(IconResourceName);
            return stream is null ? null : new Icon(stream);
        }
        catch
        {
            return null;
        }
    }



    private static Image CreateHeaderLogoGraphic(int size)
    {
        size = Math.Max(64, size);
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        var tileInset = Math.Max(4, size / 18);
        var tileRect = new Rectangle(tileInset, tileInset, size - (tileInset * 2), size - (tileInset * 2));
        var radius = Math.Max(14, size / 7);
        using (var path = CreateRoundedRectanglePath(tileRect, radius))
        using (var fill = new LinearGradientBrush(tileRect, Color.FromArgb(20, 29, 42), Color.FromArgb(10, 17, 27), LinearGradientMode.Vertical))
        using (var border = new Pen(Color.FromArgb(70, 95, 124), Math.Max(1.35f, size / 56f)))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        var linePenWidth = Math.Max(3f, size / 18f);
        using (var wavePen = new Pen(Color.FromArgb(248, 250, 252), linePenWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        })
        {
            var points = new[]
            {
                new PointF(size * 0.22f, size * 0.55f),
                new PointF(size * 0.34f, size * 0.55f),
                new PointF(size * 0.41f, size * 0.38f),
                new PointF(size * 0.49f, size * 0.70f),
                new PointF(size * 0.59f, size * 0.31f),
                new PointF(size * 0.69f, size * 0.55f),
                new PointF(size * 0.80f, size * 0.55f),
            };
            g.DrawLines(wavePen, points);
        }

        var dotSize = Math.Max(10, size / 8);
        var dotRect = new Rectangle(tileRect.Right - dotSize - Math.Max(8, size / 20), tileRect.Top + Math.Max(8, size / 20), dotSize, dotSize);
        using (var dotBrush = new SolidBrush(Color.FromArgb(255, 77, 94)))
        {
            g.FillEllipse(dotBrush, dotRect);
        }

        return bitmap;
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

    private static Image CreateHeaderBannerStrip(Image source)
    {
        var bounds = FindMeaningfulBannerBounds(source);
        var verticalPadding = Math.Max(12, bounds.Height / 8);
        var top = Math.Max(0, bounds.Top - verticalPadding);
        var bottom = Math.Min(source.Height, bounds.Bottom + verticalPadding);
        var height = Math.Max(1, bottom - top);
        var crop = new Rectangle(0, top, source.Width, height);

        using var cropped = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(cropped))
        {
            g.Clear(Color.FromArgb(5, 8, 15));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.DrawImage(source, new Rectangle(0, 0, cropped.Width, cropped.Height), crop, GraphicsUnit.Pixel);
        }

        return (Image)cropped.Clone();
    }

    private static Rectangle FindMeaningfulBannerBounds(Image source)
    {
        using var bitmap = new Bitmap(source);
        var threshold = 14;
        var top = 0;
        var bottom = bitmap.Height - 1;

        bool RowHasContent(int y)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A < 8)
                {
                    continue;
                }

                var luminance = (pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000;
                if (luminance > threshold || pixel.R > threshold || pixel.G > threshold || pixel.B > threshold)
                {
                    return true;
                }
            }

            return false;
        }

        while (top < bitmap.Height && !RowHasContent(top))
        {
            top++;
        }

        while (bottom > top && !RowHasContent(bottom))
        {
            bottom--;
        }

        if (top >= bottom)
        {
            return new Rectangle(0, 0, source.Width, source.Height);
        }

        return new Rectangle(0, top, source.Width, bottom - top + 1);
    }

    private static Image? LoadImage(string resourceName)
    {
        try
        {
            var assembly = typeof(Branding).Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName);
            return stream is null ? null : Image.FromStream(stream);
        }
        catch
        {
            return null;
        }
    }
}
