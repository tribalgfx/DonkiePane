using System.Drawing;
using System.Reflection;

namespace DonkiePane.App;

/// <summary>Het DonkiePane-icoon (ingebouwd in het programma, alle formaten), voor het tray-icoon.</summary>
public static class TrayIconFactory
{
    public const string ResourceName = "DonkiePane.ico";

    /// <summary>Het icoon in het formaat dat de tray nodig heeft (afhankelijk van schermschaling).</summary>
    public static Icon CreateAppIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Het icoon ontbreekt in het programma.");
        return new Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }
}
