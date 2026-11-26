namespace DonkiePane.Core;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,     // dezelfde waarden als MOD_ALT, MOD_CONTROL, MOD_SHIFT en MOD_WIN van RegisterHotKey
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// Een sneltoets als tekst, bijvoorbeeld <c>"Ctrl+Alt+Space"</c>. De toets is de naam uit de toetsenlijst van WPF
/// (<c>Space</c>, <c>D</c>, <c>F12</c>, <c>OemTilde</c>); de app zet die om naar een echte toets.
/// </summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, string Key)
{
    private static readonly (string Name, HotkeyModifiers Flag)[] Names =
    [
        ("Ctrl", HotkeyModifiers.Ctrl),
        ("Alt", HotkeyModifiers.Alt),
        ("Shift", HotkeyModifiers.Shift),
        ("Win", HotkeyModifiers.Win),
    ];

    /// <summary>Leest een sneltoets; zonder toets of zonder Ctrl, Alt of Win (alleen Shift is te makkelijk per ongeluk) = <c>null</c>.</summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifiers = HotkeyModifiers.None;
        string? key = null;
        foreach (var part in parts)
        {
            var flag = Names.FirstOrDefault(n => string.Equals(n.Name, part, StringComparison.OrdinalIgnoreCase)).Flag;
            if (flag != HotkeyModifiers.None) modifiers |= flag;
            else if (key is null) key = part;
            else return null; // twee gewone toetsen
        }
        if (key is null || (modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0) return null;
        return new Hotkey(modifiers, key);
    }

    public override string ToString() =>
        string.Join("+", Names.Where(n => Modifiers.HasFlag(n.Flag)).Select(n => n.Name).Append(Key));
}
