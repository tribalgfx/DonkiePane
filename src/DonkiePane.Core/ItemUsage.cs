namespace DonkiePane.Core;

/// <summary>
/// Wanneer je een bureaubladitem voor het laatst via een pane hebt geopend, zodat iconen die je lang niet gebruikt
/// grijs kunnen worden (<see cref="Layout.DimUnusedDays"/>). Een item dat DonkiePane voor het eerst ziet, telt vanaf
/// dat moment: zo wordt na het aanzetten niet meteen alles grijs. Windows houdt zelf niet betrouwbaar bij wanneer
/// een bestand geopend is, daarom telt alleen openen via DonkiePane, plus wijzigen van het bestand.
/// </summary>
public sealed class ItemUsage
{
    private Dictionary<string, DateTime> _lastUsed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per item (zie <see cref="DesktopItemId"/>) het laatste gebruik, in UTC.</summary>
    public Dictionary<string, DateTime> LastUsed
    {
        get => _lastUsed;
        set
        {
            // Uit een bestand komt een gewone woordenlijst: hoofdletters mogen niet uitmaken, net als bij de items zelf.
            _lastUsed = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, when) in value ?? []) _lastUsed[id] = when;
        }
    }

    /// <summary>
    /// Houdt precies deze items bij: nieuwe beginnen nu, verdwenen items worden vergeten. Geeft terug of er iets
    /// veranderd is (dan is opslaan nodig).
    /// </summary>
    public bool Track(IEnumerable<string> ids, DateTime nowUtc)
    {
        var current = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var gone in _lastUsed.Keys.Where(id => !current.Contains(id)).ToList())
            changed |= _lastUsed.Remove(gone);
        foreach (var id in current)
            changed |= _lastUsed.TryAdd(id, nowUtc);
        return changed;
    }

    public void Opened(string id, DateTime nowUtc) => _lastUsed[id] = nowUtc;

    // Werkwoorden uit het menu van Verkenner die het item zelf gebruiken (openen, bewerken, afspelen, ...). Knippen,
    // kopiëren, verwijderen, eigenschappen en dergelijke tellen niet.
    private static readonly HashSet<string> UseVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "opennew", "openas", "runas", "runasuser", "edit", "play", "explore", "print", "printto",
    };

    /// <summary>
    /// Telt deze optie uit het menu van Verkenner als het item gebruiken? Een optie zonder werkwoord (bijv. een
    /// programma onder "Openen met") telt mee: dat zijn bijna altijd manieren om het item te openen.
    /// </summary>
    public static bool CountsAsUse(string? verb) => string.IsNullOrEmpty(verb) || UseVerbs.Contains(verb);

    /// <summary>Een item heeft een andere naam gekregen: de geschiedenis gaat mee.</summary>
    public void Rename(string oldId, string newId)
    {
        if (!_lastUsed.Remove(oldId, out var when)) return;
        _lastUsed[newId] = _lastUsed.TryGetValue(newId, out var existing) && existing > when ? existing : when;
    }

    /// <summary>
    /// Is dit item al <paramref name="days"/> dagen niet geopend en niet gewijzigd? Een onbekend item of
    /// <paramref name="days"/> van 0 of minder: nee.
    /// </summary>
    public bool IsUnused(string id, DateTime? modifiedUtc, DateTime nowUtc, int days)
    {
        if (days <= 0 || !_lastUsed.TryGetValue(id, out var last)) return false;
        if (modifiedUtc is { } modified && modified > last) last = modified;
        return nowUtc - last >= TimeSpan.FromDays(days);
    }
}

/// <summary>Het gebruik van bureaubladitems, in <c>%APPDATA%\DonkiePane\usage.json</c>.</summary>
public sealed class ItemUsageStore(string filePath) : JsonFileStore<ItemUsage>(filePath)
{
    public static string DefaultPath => AppDataPath("usage.json");
}
