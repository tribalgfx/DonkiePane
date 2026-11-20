using System.Globalization;
using System.Text.Json;

namespace DonkiePane.Core;

/// <summary>
/// De taal van DonkiePane. De Nederlandse tekst staat in de code en is tegelijk de sleutel: <c>Loc.T("Plakken")</c>
/// geeft in het Engels "Paste". De vertalingen staan per taal in <c>Localization\&lt;taal&gt;.json</c> (Nederlands →
/// vertaling); ontbreekt er een, dan blijft de Nederlandse tekst staan. Een test controleert dat elke tekst in de code
/// een vertaling heeft.
///
/// Regels voor wie teksten toevoegt: één letterlijke tekst per aanroep (geen + of $"..."), en variabele delen als
/// {0}, {1} via <see cref="F"/>.
/// </summary>
public static class Loc
{
    public const string Dutch = "nl";
    public const string English = "en";

    /// <summary>De talen die DonkiePane kent, met hun eigen naam (voor de keuzelijst in Instellingen).</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Languages = [(Dutch, "Nederlands"), (English, "English")];

    private static IReadOnlyDictionary<string, string>? _table; // null = Nederlands

    /// <summary>De taal die nu gebruikt wordt ("nl" of "en").</summary>
    public static string Language { get; private set; } = Dutch;

    /// <summary>Voor datums en getallen: de instellingen van Windows als die bij de taal passen, anders die van de taal.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentCulture;

    /// <summary>De vertaling van een Nederlandse tekst.</summary>
    public static string T(string dutch) => _table is not null && _table.TryGetValue(dutch, out var text) ? text : dutch;

    /// <summary>De vertaling met ingevulde {0}, {1}, ...</summary>
    public static string F(string dutch, params object?[] args) => string.Format(Culture, T(dutch), args);

    /// <summary>
    /// Welke taal een instelling betekent: "nl" of "en", of leeg/onbekend = de taal van Windows (Nederlands bij een
    /// Nederlandse Windows, anders Engels).
    /// </summary>
    public static string Resolve(string? setting, CultureInfo? windowsLanguage = null)
    {
        if (Languages.Any(l => l.Code == setting)) return setting!;
        var windows = (windowsLanguage ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName;
        return Languages.Any(l => l.Code == windows) ? windows : English;
    }

    /// <summary>Kiest de taal voor de hele app. Aanroepen bij het starten, vóór er vensters of menu's zijn.</summary>
    public static void Use(string language)
    {
        Language = Languages.Any(l => l.Code == language) ? language : English;
        _table = Language == Dutch ? null : Load(Language);
        Culture = CultureInfo.CurrentCulture.TwoLetterISOLanguageName == Language
            ? CultureInfo.CurrentCulture
            : CultureInfo.GetCultureInfo(Language);
    }

    /// <summary>De vertalingen van een taal (Nederlands → vertaling), uit het programma zelf.</summary>
    public static IReadOnlyDictionary<string, string> Load(string language)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"DonkiePane.Localization.{language}.json");
        if (stream is null) return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
