using System.Text.Json;
using System.Text.Json.Serialization;

namespace DonkiePane.Core;

/// <summary>
/// Leest en schrijft één object als JSON-bestand. Schrijven gaat via een
/// tijdelijk bestand zodat een crash halverwege het bestaande bestand niet
/// beschadigt; de vorige versie blijft bewaard als <c>.bak</c> en wordt
/// gebruikt als het hoofdbestand onleesbaar is.
/// </summary>
public class JsonFileStore<T>(string filePath) where T : class, new()
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath { get; } = filePath;

    protected static string AppDataPath(string fileName) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DonkiePane",
            fileName);

    public T Load()
    {
        foreach (var path in new[] { FilePath, FilePath + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
                if (value is not null) return value;
            }
            catch (JsonException)
            {
                // Beschadigd bestand: probeer de back-up.
            }
        }
        return new T();
    }

    public void Save(T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(value, Options));

        if (File.Exists(FilePath))
            File.Replace(tempPath, FilePath, FilePath + ".bak");
        else
            File.Move(tempPath, FilePath);
    }
}
