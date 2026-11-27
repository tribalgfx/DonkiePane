using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DonkiePane.App;

/// <summary>Wat een pane van één bureaubladitem laat zien.</summary>
public sealed record ItemView(string Id, string Path, string DisplayName, ImageSource? Icon)
{
    // Buiten het record bewaard: een veld zou meetellen bij het vergelijken van twee items.
    private static readonly ConditionalWeakTable<ImageSource, BitmapSource> GrayCache = [];

    /// <summary>Het icoon in grijstinten, met dezelfde doorzichtige randen; pas gemaakt als het nodig is.</summary>
    public ImageSource? GrayIcon => Icon is BitmapSource bitmap ? GrayCache.GetValue(bitmap, _ => Grayscale(bitmap)) : Icon;

    internal static BitmapSource Grayscale(BitmapSource source)
    {
        // Gray8 en dergelijke hebben geen alfakanaal; daarom zelf de kleur uit elke pixel halen en de alfa laten staan.
        // Bij voorvermenigvuldigde alfa (Pbgra32) blijft dat kloppen: grijs is nooit meer dan de alfa.
        var format = source.Format == PixelFormats.Pbgra32 ? PixelFormats.Pbgra32 : PixelFormats.Bgra32;
        var converted = source.Format == format ? source : new FormatConvertedBitmap(source, format, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var gray = (byte)((pixels[i] * 29 + pixels[i + 1] * 150 + pixels[i + 2] * 77) >> 8);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = gray;
        }
        var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, converted.DpiX, converted.DpiY, format, null, pixels, stride);
        result.Freeze();
        return result;
    }
}
