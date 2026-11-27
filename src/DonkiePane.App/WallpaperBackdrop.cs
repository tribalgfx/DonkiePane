using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using DonkiePane.Core;
using DonkiePane.Shell;

namespace DonkiePane.App;

/// <summary>
/// Matglas zonder hulp van Windows: panes staan op de bureaubladlaag, waar Windows zijn eigen vervaagde materialen niet tekent.
/// Matglas is in
/// wezen je bureaubladachtergrond, heel sterk vervaagd; dat maakt DonkiePane hier zelf, één keer per scherm, op een
/// kleine maat (vervagen maakt details toch onzichtbaar). Een pane toont het stuk dat precies onder hem ligt.
/// </summary>
internal static class WallpaperBackdrop
{
    private const int Downscale = 12;    // 1/12 van de schermgrootte is genoeg voor een zo vage achtergrond
    private const double BlurRadius = 7; // op die kleine maat: zo'n 80 pixels op het echte scherm

    private sealed record Sample(PxRect Monitor, BitmapSource Image);

    private static List<Sample>? _samples;

    /// <summary>De achtergrond is veranderd (andere afbeelding, ander scherm): de panes moeten opnieuw tekenen.</summary>
    public static event EventHandler? Changed;

    /// <summary>Vergeet de vervaagde achtergronden; ze worden bij het volgende gebruik opnieuw gemaakt.</summary>
    public static void Invalidate()
    {
        _samples = null;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// De vervaagde achtergrond van het scherm onder dit venster, en welk deel daarvan (relatief, 0..1) onder het venster
    /// ligt. <c>null</c> als er geen achtergrond te lezen is.
    /// </summary>
    public static (BitmapSource Image, Rect Viewbox)? For(PxRect window)
    {
        var samples = _samples ??= Build();
        if (samples.Count == 0) return null;

        // Het scherm met het midden van het venster (of anders het dichtstbijzijnde).
        var cx = window.X + window.Width / 2;
        var cy = window.Y + window.Height / 2;
        var sample = samples.FirstOrDefault(s => cx >= s.Monitor.X && cy >= s.Monitor.Y && cx < s.Monitor.X + s.Monitor.Width && cy < s.Monitor.Y + s.Monitor.Height)
                     ?? samples.MinBy(s => Math.Abs(s.Monitor.X + s.Monitor.Width / 2 - cx) + Math.Abs(s.Monitor.Y + s.Monitor.Height / 2 - cy))!;
        var m = sample.Monitor;
        var viewbox = new Rect(
            (window.X - m.X) / (double)m.Width,
            (window.Y - m.Y) / (double)m.Height,
            window.Width / (double)m.Width,
            window.Height / (double)m.Height);
        return (sample.Image, viewbox);
    }

    private static List<Sample> Build()
    {
        var monitors = DesktopWallpaper.Read();
        if (monitors.Count == 0) return [];

        // Bij "Uitspreiden" loopt één afbeelding over alle schermen samen.
        var union = new Int32Rect(monitors.Min(m => m.X), monitors.Min(m => m.Y), 0, 0);
        union.Width = monitors.Max(m => m.X + m.Width) - union.X;
        union.Height = monitors.Max(m => m.Y + m.Height) - union.Y;

        var images = new Dictionary<string, (BitmapSource Image, int OriginalWidth)?>(StringComparer.OrdinalIgnoreCase);
        var samples = new List<Sample>();
        foreach (var monitor in monitors)
        {
            if (monitor.Width <= 0 || monitor.Height <= 0) continue;
            (BitmapSource Image, int OriginalWidth)? image = null;
            if (monitor.ImagePath is { } path && !images.TryGetValue(path, out image))
                images[path] = image = Load(path, Math.Max(64, union.Width / Downscale));
            try
            {
                samples.Add(new Sample(new PxRect(monitor.X, monitor.Y, monitor.Width, monitor.Height), Render(monitor, image, union)));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
            {
                // Dit scherm wordt effen.
            }
        }
        return samples;
    }

    /// <summary>Leest de afbeelding al verkleind (een achtergrond van 4K hoeft niet volledig in het geheugen).</summary>
    private static (BitmapSource Image, int OriginalWidth)? Load(string path, int maxWidth)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            var originalWidth = frame.PixelWidth;
            if (frame.PixelWidth > maxWidth)
            {
                var scale = maxWidth / (double)frame.PixelWidth;
                frame = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            }
            var bitmap = new WriteableBitmap(new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0));
            bitmap.Freeze();
            return (bitmap, originalWidth);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
                                       or InvalidOperationException or System.Runtime.InteropServices.COMException or FileFormatException)
        {
            return null;
        }
    }

    /// <summary>Tekent de achtergrond van één scherm zoals Windows hem laat zien, op kleine maat, en vervaagt hem.</summary>
    private static BitmapSource Render(MonitorWallpaper monitor, (BitmapSource Image, int OriginalWidth)? image, Int32Rect union)
    {
        var width = Math.Max(16, monitor.Width / Downscale);
        var height = Math.Max(16, monitor.Height / Downscale);
        var background = Color.FromRgb((byte)(monitor.BackgroundRgb >> 16), (byte)(monitor.BackgroundRgb >> 8), (byte)monitor.BackgroundRgb);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
            if (image is { } loaded) DrawWallpaper(dc, loaded.Image, loaded.OriginalWidth, monitor, union, width, height);
        }
        var sharp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        sharp.Render(visual);

        // Vervagen, met de randen iets uitgerekt zodat de vervaging daar niet naar doorzichtig wegvalt.
        var margin = BlurRadius * 2;
        var host = new Grid { Width = width, Height = height, ClipToBounds = true };
        host.Children.Add(new Image
        {
            Source = sharp,
            Stretch = Stretch.Fill,
            Margin = new Thickness(-margin),
            Effect = new BlurEffect { Radius = BlurRadius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
        });
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        var blurred = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        blurred.Render(host);
        blurred.Freeze();
        return blurred;
    }

    private static void DrawWallpaper(DrawingContext dc, BitmapSource image, int originalWidth, MonitorWallpaper monitor, Int32Rect union, int width, int height)
    {
        var scale = 1.0 / Downscale;
        // De afbeelding in schermpixels (de geladen afbeelding kan al verkleind zijn).
        double imageW = image.PixelWidth, imageH = image.PixelHeight;
        var aspect = imageW / imageH;
        double mw = monitor.Width, mh = monitor.Height;

        Rect target;
        switch (monitor.Fit)
        {
            case WallpaperFit.Stretch:
                target = new Rect(0, 0, mw, mh);
                break;
            case WallpaperFit.Fit:
                target = mw / mh > aspect ? Centered(mh * aspect, mh, mw, mh) : Centered(mw, mw / aspect, mw, mh);
                break;
            case WallpaperFit.Center:
            case WallpaperFit.Tile:
                // Op ware grootte (de geladen afbeelding kan verkleind zijn; de oorspronkelijke breedte telt).
                var original = (double)originalWidth;
                var w = original;
                var h = original / aspect;
                if (monitor.Fit == WallpaperFit.Tile)
                {
                    var brush = new ImageBrush(image)
                    {
                        TileMode = TileMode.Tile,
                        Viewport = new Rect(0, 0, w * scale, h * scale),
                        ViewportUnits = BrushMappingMode.Absolute,
                        Stretch = Stretch.Fill,
                    };
                    dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
                    return;
                }
                target = Centered(w, h, mw, mh);
                break;
            case WallpaperFit.Span:
                // Eén afbeelding over het geheel van alle schermen (als "Opvullen"), en daarvan het stuk van dit scherm.
                var all = Fill(union.Width, union.Height, aspect);
                target = new Rect(all.X - (monitor.X - union.X), all.Y - (monitor.Y - union.Y), all.Width, all.Height);
                break;
            default: // Fill
                target = Fill(mw, mh, aspect);
                break;
        }
        dc.DrawImage(image, new Rect(target.X * scale, target.Y * scale, target.Width * scale, target.Height * scale));
    }

    private static Rect Centered(double w, double h, double areaW, double areaH) => new((areaW - w) / 2, (areaH - h) / 2, w, h);

    /// <summary>Zo groot dat het hele vlak bedekt is, in het midden (wat buiten valt, wordt afgesneden).</summary>
    private static Rect Fill(double areaW, double areaH, double aspect) =>
        areaW / areaH > aspect ? Centered(areaW, areaW / aspect, areaW, areaH) : Centered(areaH * aspect, areaH, areaW, areaH);
}
