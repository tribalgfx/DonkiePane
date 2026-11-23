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
    }
}
