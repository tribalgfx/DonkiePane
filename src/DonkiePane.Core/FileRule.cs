namespace DonkiePane.Core;

/// <summary>Hoe een opgerold pane even opengaat (het blijft opgerold; je kijkt er alleen in).</summary>
public enum RollUpOpen
{
    /// <summary>Alleen met de knop of een dubbelklik op de titelbalk (dan rolt het echt uit).</summary>
    Button,

    /// <summary>Gaat open zodra de muis erboven komt en rolt weer op als de muis weggaat.</summary>
    Hover,

    /// <summary>Gaat open met één klik op de titelbalk en rolt weer op als de muis weggaat.</summary>
    Click,
}

/// <summary>Licht of donker.</summary>
public enum AppTheme
{
    /// <summary>Zoals de apps in Windows (Instellingen → Persoonlijke instellingen → Kleuren).</summary>
    System,
    Dark,
    Light,
}

/// <summary>Waar een regel naar kijkt.</summary>
public enum RuleKind
{
    /// <summary>De extensie is een van de opgegeven extensies (bijv. "pdf, docx").</summary>
    Extension,

    /// <summary>De naam bevat de tekst (hoofdletters tellen niet).</summary>
    NameContains,

    /// <summary>De naam is precies de tekst, met extensie (hoofdletters tellen niet).</summary>
    NameIs,

    /// <summary>Het is een map.</summary>
    Folder,
}

/// <summary>Een regel: een nieuw bureaubladitem dat past, gaat naar <see cref="TargetPane"/> in plaats van naar de Inbox.</summary>
public sealed class FileRule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public RuleKind Kind { get; set; } = RuleKind.Extension;
    public string Value { get; set; } = "";
    public Guid TargetPane { get; set; }

    /// <summary>Past deze regel bij een item met deze bestandsnaam?</summary>
    public bool Matches(string fileName, bool isFolder)
    {
        var value = (Value ?? "").Trim();
        return Kind switch
        {
            RuleKind.Folder => isFolder,
            RuleKind.Extension => !isFolder && Extensions(value).Contains(Path.GetExtension(fileName).TrimStart('.'), StringComparer.OrdinalIgnoreCase),
            RuleKind.NameContains => value.Length > 0 && fileName.Contains(value, StringComparison.CurrentCultureIgnoreCase),
            RuleKind.NameIs => value.Length > 0 && string.Equals(fileName, value, StringComparison.CurrentCultureIgnoreCase),
            _ => false,
        };
    }

    /// <summary>"pdf, .docx;xlsx" → pdf, docx, xlsx.</summary>
    public static IReadOnlyList<string> Extensions(string value) =>
        value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.TrimStart('.', '*'))
            .Where(e => e.Length > 0)
            .ToList();
}

public static class FileRules
{
    /// <summary>
    /// Het pane waar dit bureaubladitem volgens de regels heen gaat, of <c>null</c>. Alleen bestaande iconenpanes tellen;
    /// systeemiconen (Prullenbak, Deze pc) vallen nooit onder een regel.
    /// </summary>
    public static Pane? Target(Layout layout, string itemId, Func<string, bool>? isFolder)
    {
        if (layout.Rules is not { Count: > 0 } rules) return null;
        var slash = itemId.IndexOf('/');
        if (slash <= 0 || itemId[..slash] == DesktopItemId.Shell) return null;
        var fileName = itemId[(slash + 1)..];
        var folder = isFolder?.Invoke(itemId) ?? false;

        foreach (var rule in rules)
        {
            if (rule is null || !rule.Matches(fileName, folder)) continue;
            if (layout.Panes.FirstOrDefault(p => p.Id == rule.TargetPane && p.Kind == PaneKind.Icons) is { } pane) return pane;
        }
        return null;
    }
}
