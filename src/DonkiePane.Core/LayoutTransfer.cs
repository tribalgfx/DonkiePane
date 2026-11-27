using System.Text.Json;
using System.Text.Json.Serialization;

namespace DonkiePane.Core;

/// <summary>
/// De indeling als los bestand: bewaren, of overzetten naar een andere pc. Het bestand bevat de panes (plek, grootte,
/// uiterlijk, welke iconen erin staan, welke takenlijst ze tonen) en de instellingen voor het bureaublad en het raster.
/// Geen taken, geen bronnen en geen tokens: die staan in eigen bestanden en blijven op deze pc.
/// </summary>
public static class LayoutTransfer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        MaxDepth = 32, // een indeling is nooit dieper dan een paar niveaus; diep geneste onzin wordt geweigerd
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>De indeling als tekst, zonder wat alleen voor deze pc geldt (meldingen, welkomstscherm, taal, updates).</summary>
    public static string Serialize(Layout layout)
    {
        var copy = JsonSerializer.Deserialize<Layout>(JsonSerializer.Serialize(layout, Options), Options)!;
        copy.WelcomeShown = true;
        copy.NotifiedUpdateVersion = null;
        copy.Language = null;
        copy.DueRemindersDate = null;
        copy.DueRemindedTaskIds = [];
        return JsonSerializer.Serialize(copy, Options);
    }

    /// <summary>
    /// Leest een geëxporteerde indeling. Geeft een <see cref="InvalidDataException"/> met een leesbare reden als het geen
    /// indeling van DonkiePane is (ander bestand, beschadigd, geen panes).
    /// </summary>
    /// <summary>Groter is geen indeling: een bestand van een paar duizend panes is nog geen megabyte.</summary>
    public const long MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Een map op deze pc met een volledig pad (<c>C:\…</c>). Geen netwerkpad (<c>\\server\…</c>, <c>//server/…</c>),
    /// geen apparaatpad (<c>\\?\…</c>, <c>\\.\…</c>) en geen relatief pad.
    /// </summary>
    public static bool IsLocalFolder(string? path) =>
        path is { Length: >= 3 } && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/'
        && Path.IsPathFullyQualified(path) && path.IndexOfAny(['"', '\0']) < 0;

    /// <summary>Leest een geëxporteerd bestand; een veel te groot bestand wordt niet eens gelezen.</summary>
    /// <exception cref="InvalidDataException">Te groot, of geen indeling van DonkiePane.</exception>
    public static Layout ReadFile(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes)
            throw new InvalidDataException(Loc.T("Dit bestand is te groot voor een indeling van DonkiePane."));
        return Parse(File.ReadAllText(path));
    }

    public static Layout Parse(string json)
    {
        Layout? layout;
        try
        {
            layout = JsonSerializer.Deserialize<Layout>(json, Options);
        }
        catch (JsonException)
        {
            throw new InvalidDataException(Loc.T("Dit bestand is geen indeling van DonkiePane, of het is beschadigd."));
        }
        if (layout?.Panes is not { Count: > 0 } panes || panes.Any(p => p is null))
            throw new InvalidDataException(Loc.T("In dit bestand staan geen panes."));
        return layout;
    }

    /// <summary>
    /// Vervangt de panes en de bureaublad- en rasterinstellingen van <paramref name="target"/> door die uit het bestand.
    /// Taal, updates, het welkomstscherm en de meldingen van vandaag blijven zoals ze op deze pc zijn.
    /// </summary>
    public static void ApplyTo(Layout target, Layout imported)
    {
        target.Panes = imported.Panes;
        target.HideDesktopIcons = imported.HideDesktopIcons;
        target.HideOnDesktopDoubleClick = imported.HideOnDesktopDoubleClick;
        target.SnapToGrid = imported.SnapToGrid;
        target.GridSize = imported.GridSize;
        target.SyncSeconds = imported.SyncSeconds;
        target.RemindDueTasks = imported.RemindDueTasks;
        target.RollUpOpen = imported.RollUpOpen;
        target.Appearance = imported.Appearance;
        target.CornerRadius = imported.CornerRadius;
        target.Rules = imported.Rules ?? [];

        // Een mappane uit een bestand van iemand anders mag niet zomaar naar een netwerkmap kijken: dan logt Windows
        // daar zonder te vragen in met jouw account. Alleen mappen op deze pc; een netwerkmap kies je daarna zelf.
        foreach (var pane in target.Panes.Where(p => p is not null && !IsLocalFolder(p.FolderPath))) pane.FolderPath = null;
        target.DimUnusedDays = Math.Max(0, imported.DimUnusedDays);
    }
}
