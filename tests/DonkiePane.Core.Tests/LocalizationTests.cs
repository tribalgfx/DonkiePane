using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DonkiePane.Core;

namespace DonkiePane.Core.Tests;

/// <summary>
/// Bewaakt de vertalingen: elke Nederlandse tekst in de code (Loc.T/Loc.F en {l:T '...'} in XAML) heeft een Engelse
/// vertaling, er staan geen vergeten teksten in het vertaalbestand, de {0}-plekken kloppen, en schermen hebben geen
/// vaste (onvertaalde) tekst meer. De test leest de broncode van de repo.
/// </summary>
public sealed partial class LocalizationTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "DonkiePane.slnx"))) return dir.FullName;
        throw new DirectoryNotFoundException("De map van de repo is niet gevonden.");
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root, "src"), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [GeneratedRegex("""Loc\.[TF]\(\s*"((?:[^"\\]|\\.)*)"(\s*\+)?""")]
    private static partial Regex CsKey();

    [GeneratedRegex("""\{l:T '((?:[^'\\]|\\.)*)'\}""")]
    private static partial Regex XamlKey();

    [GeneratedRegex("""\{(\d+)(?:[:,][^}]*)?\}""")]
    private static partial Regex Placeholder();

    private static string UnescapeCs(string s) => Regex.Replace(s, """\\(["\\nrt])""",
        m => m.Groups[1].Value switch { "n" => "\n", "r" => "\r", "t" => "\t", var c => c });

    private static string DecodeXml(string s) => XElement.Parse($"<x a=\"{s}\" />").Attribute("a")!.Value;

    /// <summary>Alle teksten die in de code vertaald worden.</summary>
    private static HashSet<string> UsedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in SourceFiles("*.cs"))
            foreach (Match m in CsKey().Matches(File.ReadAllText(file)))
                keys.Add(UnescapeCs(m.Groups[1].Value));
        foreach (var file in SourceFiles("*.xaml"))
            foreach (Match m in XamlKey().Matches(File.ReadAllText(file)))
                keys.Add(DecodeXml(Regex.Replace(m.Groups[1].Value, """\\(.)""", "$1")));
        return keys;
    }

    [Fact]
    public void Every_text_in_the_code_has_an_english_translation()
    {
        var english = Loc.Load(Loc.English);
        var missing = UsedKeys().Where(k => !english.TryGetValue(k, out var t) || string.IsNullOrWhiteSpace(t)).Order().ToList();

        Assert.True(missing.Count == 0, "Zonder Engelse vertaling (zet ze in src/DonkiePane.Core/Localization/en.json):\n" + string.Join("\n", missing));
    }

    [Fact]
    public void The_translation_file_has_no_forgotten_texts()
    {
        // Een tekst telt als gebruikt als hij vertaald wordt, of als letterlijke tekst in de code staat die later
        // vertaald wordt (zoals de namen van de thema's: Loc.T(theme.Name)).
        var used = UsedKeys();
        var allCode = string.Join("\n", SourceFiles("*.cs").Select(File.ReadAllText));
        var unused = Loc.Load(Loc.English).Keys
            .Where(k => !used.Contains(k) && !allCode.Contains('"' + k.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + '"'))
            .Order().ToList();

        Assert.True(unused.Count == 0, "Niet meer gebruikt (weghalen uit en.json):\n" + string.Join("\n", unused));
    }

    [Fact]
    public void Placeholders_match_between_dutch_and_english()
    {
        static string Places(string s) => string.Join(",", Placeholder().Matches(s).Select(m => m.Groups[1].Value).Distinct().Order());
        var wrong = Loc.Load(Loc.English).Where(p => Places(p.Key) != Places(p.Value)).Select(p => p.Key).ToList();

        Assert.True(wrong.Count == 0, "{0}-plekken verschillen:\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void Translated_texts_are_single_literals()
    {
        // Loc.T("a" + b) of Loc.T($"...") kan niet vertaald worden: de sleutel is dan niet de tekst in en.json.
        var bad = new List<string>();
        foreach (var file in SourceFiles("*.cs"))
        {
            var text = File.ReadAllText(file);
            bad.AddRange(CsKey().Matches(text).Where(m => m.Groups[2].Success).Select(m => $"{Path.GetFileName(file)}: {m.Value}"));
            bad.AddRange(Regex.Matches(text, """Loc\.[TF]\(\s*\$""").Select(m => $"{Path.GetFileName(file)}: interpolatie"));
        }

        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void Screens_have_no_untranslated_text()
    {
        var literal = new List<string>();
        foreach (var file in SourceFiles("*.xaml"))
        {
            var doc = XDocument.Load(file);
            foreach (var attribute in doc.Descendants().Attributes()
                         .Where(a => a.Name.LocalName is "Text" or "Header" or "Content" or "ToolTip" or "Title" or "Description"))
            {
                var value = attribute.Value;
                if (value.StartsWith('{')) continue; // binding, resource of {l:T ...}
                if (!value.Any(char.IsLetter)) continue; // een icoon uit Segoe Fluent Icons
                literal.Add($"{Path.GetFileName(file)}: {attribute.Name.LocalName}=\"{value}\"");
            }
            foreach (var setter in doc.Descendants().Where(e => e.Name.LocalName == "Setter"))
            {
                if (setter.Attribute("Property")?.Value is not ("ToolTip" or "Text" or "Content" or "Header")) continue;
                if (setter.Attribute("Value")?.Value is { } value && !value.StartsWith('{') && value.Any(char.IsLetter))
                    literal.Add($"{Path.GetFileName(file)}: Setter {value}");
            }
        }

        Assert.True(literal.Count == 0, "Vaste tekst in een scherm (gebruik {l:T '...'}):\n" + string.Join("\n", literal));
    }

    [Theory]
    [InlineData(null, "nl-NL", "nl")]   // Nederlandse Windows
    [InlineData(null, "nl-BE", "nl")]
    [InlineData(null, "en-GB", "en")]
    [InlineData(null, "de-DE", "en")]   // een taal die DonkiePane niet kent: Engels
    [InlineData("", "nl-NL", "nl")]
    [InlineData("en", "nl-NL", "en")]   // zelf gekozen gaat voor
    [InlineData("nl", "fr-FR", "nl")]
    [InlineData("xx", "nl-NL", "nl")]   // onbekende instelling: zoals Windows
    public void The_language_follows_windows_unless_chosen(string? setting, string windows, string expected) =>
        Assert.Equal(expected, Loc.Resolve(setting, CultureInfo.GetCultureInfo(windows)));

    [Fact]
    public void The_english_texts_are_built_into_the_program()
    {
        var english = Loc.Load(Loc.English);

        Assert.Equal("Paste", english["Plakken"]);
        Assert.Equal("New pane", english["Nieuw pane"]);
    }
}
