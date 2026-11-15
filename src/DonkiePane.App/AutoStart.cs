using Microsoft.Win32;

namespace DonkiePane.App;

/// <summary>
/// Starten met Windows via de Run-sleutel van de huidige gebruiker. Dat is
/// dezelfde plek die Taakbeheer → Opstarten toont, dus je kunt het daar ook
/// zelf uitzetten. Er zijn geen beheerdersrechten voor nodig.
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DonkiePane";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var path = Environment.ProcessPath ?? throw new InvalidOperationException("Onbekend programmapad.");
            key.SetValue(ValueName, $"\"{path}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Het pad waarmee Windows DonkiePane nu start (of <c>null</c>).</summary>
    public static string? RegisteredPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return (key?.GetValue(ValueName) as string)?.Trim('"');
    }
}
