using Microsoft.Win32;

namespace DonkiePane.Shell;

/// <summary>
/// Bureaubladiconen die geen bestand zijn, zoals de Prullenbak en Deze pc.
/// Welke zichtbaar zijn stel je in Windows in via Instellingen → Persoonlijke
/// instellingen → Thema's → Instellingen voor bureaubladpictogrammen; DonkiePane
/// volgt die keuze.
/// </summary>
public static class VirtualDesktopItems
{
    private const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";

    /// <summary>Bekende systeemiconen, met of ze standaard zichtbaar zijn.</summary>
    private static readonly (string Clsid, bool ShownByDefault)[] Known =
    [
        ("{645FF040-5081-101B-9F08-00AA002F954E}", true),  // Prullenbak
        ("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", false), // Deze pc
        ("{59031a47-3f72-44a7-89c5-5595fe6b30ee}", false), // Gebruikersbestanden
        ("{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", false), // Netwerk
        ("{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", false), // Configuratiescherm
    ];

    public static IEnumerable<string> EnumerateEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
        foreach (var (clsid, shownByDefault) in Known)
        {
            // 0 = zichtbaar, 1 = verborgen, ontbreekt = standaard.
            var shown = key?.GetValue(clsid) is int value ? value == 0 : shownByDefault;
            if (shown) yield return clsid;
        }
    }

    /// <summary>De naam waarmee de shell het item kent, bijv. <c>::{645FF040-...}</c>.</summary>
    public static string ParsingName(string clsid) => "::" + clsid;

    public static bool IsVirtual(string path) => path.StartsWith("::", StringComparison.Ordinal);
}
