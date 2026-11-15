using System.Text.Json;

namespace DonkiePane.Core;

/// <summary>
/// Leest en schrijft de layout als JSON. Schrijven gaat via een tijdelijk
/// bestand zodat een crash halverwege de bestaande layout niet beschadigt;
/// de vorige versie blijft bewaard als <c>.bak</c>.
/// </summary>
public sealed class LayoutStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string FilePath { get; } = filePath;

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DonkiePane",
            "layout.json");

    public Layout Load()
    {
        foreach (var path in new[] { FilePath, FilePath + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var layout = JsonSerializer.Deserialize<Layout>(File.ReadAllText(path), Options);
                if (layout is not null) return layout;
            }
            catch (JsonException)
            {
                // Beschadigd bestand: probeer de back-up.
            }
        }
        return new Layout();
    }

    public void Save(Layout layout)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(layout, Options));

        if (File.Exists(FilePath))
            File.Replace(tempPath, FilePath, FilePath + ".bak");
        else
            File.Move(tempPath, FilePath);
    }
}
