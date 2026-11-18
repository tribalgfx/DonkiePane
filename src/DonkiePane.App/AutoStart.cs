using Microsoft.Win32;

namespace DonkiePane.App;

/// <summary>
/// Starten met Windows via de Run-sleutel van de huidige gebruiker. Dat is
/// dezelfde plek die Taakbeheer → Opstarten toont, dus je kunt het daar ook
/// zelf uitzetten. Er zijn geen beheerdersrechten voor nodig.
///
/// Naast de Run-waarde houdt Windows in <c>StartupApproved\Run</c> bij of een
/// opstartprogramma aan of uit staat. Windows 11 slaat een Run-waarde zonder
/// zo'n vermelding bij het inloggen over (zonder melding), dus we schrijven ze
/// altijd samen. Uitzetten in Taakbeheer zet de vermelding op "uit"; dat
/// respecteren we.
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "DonkiePane";

    /// <summary>"Aan" zoals Taakbeheer het schrijft: 0x02 en verder nullen (de rest is het tijdstip van uitzetten).</summary>
    private static readonly byte[] Enabled = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>De registerroot; tests gebruiken een eigen sleutel in plaats van de echte HKCU.</summary>
    internal static RegistryKey Root { get; set; } = Registry.CurrentUser;

    public static bool IsEnabled()
    {
        using var run = Root.OpenSubKey(RunKey);
        if (run?.GetValue(ValueName) is not string) return false;
        return ApprovalOf() is not { } approval || IsApproved(approval);
    }

    public static void SetEnabled(bool enabled, string? exePath = null)
    {
        using var run = Root.CreateSubKey(RunKey);
        using var approved = Root.CreateSubKey(ApprovedKey);
        if (enabled)
        {
            var path = exePath ?? Environment.ProcessPath ?? throw new InvalidOperationException("Onbekend programmapad.");
            run.SetValue(ValueName, $"\"{path}\"");
            approved.SetValue(ValueName, Enabled, RegistryValueKind.Binary);
        }
        else
        {
            run.DeleteValue(ValueName, throwOnMissingValue: false);
            approved.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Oudere versies schreven alleen de Run-waarde, waardoor Windows DonkiePane
    /// toch niet startte. Staat "starten met Windows" aan zonder vermelding in
    /// StartupApproved, dan vullen we die aan. Een in Taakbeheer uitgezette
    /// vermelding blijft uit. Geeft <c>true</c> als er iets is hersteld.
    /// </summary>
    public static bool RepairApproval()
    {
        using (var run = Root.OpenSubKey(RunKey))
            if (run?.GetValue(ValueName) is not string) return false;
        if (ApprovalOf() is not null) return false;

        using var approved = Root.CreateSubKey(ApprovedKey);
        approved.SetValue(ValueName, Enabled, RegistryValueKind.Binary);
        return true;
    }

    /// <summary>Het pad waarmee Windows DonkiePane nu start (of <c>null</c>).</summary>
    public static string? RegisteredPath()
    {
        using var key = Root.OpenSubKey(RunKey);
        return (key?.GetValue(ValueName) as string)?.Trim('"');
    }

    private static byte[]? ApprovalOf()
    {
        using var approved = Root.OpenSubKey(ApprovedKey);
        return approved?.GetValue(ValueName) as byte[];
    }

    /// <summary>Even eerste byte (02, 06) = aan, oneven (03, 07) = uitgezet door de gebruiker.</summary>
    private static bool IsApproved(byte[] approval) => approval.Length == 0 || (approval[0] & 1) == 0;
}
