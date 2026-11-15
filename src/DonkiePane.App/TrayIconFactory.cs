using System.Drawing;
using System.Runtime.InteropServices;

namespace DonkiePane.App;

/// <summary>Tekent een klein theedoek-ruitje als tray-icoon, zodat er geen .ico nodig is.</summary>
internal static class TrayIconFactory
{
    public static Icon CreatePlaidIcon(int size = 32)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.FromArgb(245, 240, 232));
            var cell = size / 4;
            using var red = new SolidBrush(Color.FromArgb(200, 179, 38, 45));
            for (var i = 0; i < 4; i++)
            {
                if (i % 2 == 0) continue;
                // Horizontale en verticale banen; waar ze kruisen wordt het donkerder.
                g.FillRectangle(red, 0, i * cell, size, cell);
                g.FillRectangle(red, i * cell, 0, cell, size);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
