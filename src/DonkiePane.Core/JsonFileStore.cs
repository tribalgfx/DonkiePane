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

    /// <summary>Een virusscanner of indexeerder houdt een bestand soms even vast; zo vaak proberen we het opnieuw.</summary>
    private const int Attempts = 4;

    public string FilePath { get; } = filePath;

    protected static string AppDataPath(string fileName) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DonkiePane",
            fileName);

    /// <summary>
    /// Het opgeslagen object; een nieuw, leeg object als er nog niets is. Is het hoofdbestand beschadigd, dan de
    /// back-up. Een bestand dat in gebruik of niet toegankelijk blijft, geeft een <see cref="IOException"/> of
    /// <see cref="UnauthorizedAccessException"/>: dan een leeg object teruggeven zou bij de volgende keer opslaan
    /// alles overschrijven.
    /// </summary>
    public T Load()
    {
        var corrupt = false;
        foreach (var path in new[] { FilePath, FilePath + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var value = JsonSerializer.Deserialize<T>(Retry(() => File.ReadAllText(path)), Options);
                if (value is not null) return value;
                corrupt = true;
            }
            catch (JsonException)
            {
                corrupt = true; // beschadigd bestand: probeer de back-up
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && path == FilePath)
            {
                // Het hoofdbestand is er wel, maar in gebruik of niet toegankelijk. De back-up is ouder, en leeg
                // beginnen zou de volgende keer opslaan alles overschrijven: dan liever een fout.
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Een onleesbare back-up telt als geen back-up.
            }
        }

        if (corrupt) KeepCorruptCopy();
        return new T();
    }

    public void Save(T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var json = JsonSerializer.Serialize(value, Options);
        // Een eigen tijdelijk bestand per keer: twee schrijvers op hetzelfde bestand (bijv. een oude en een nieuwe
        // wachtrij vlak na een herstart van een bron) zitten elkaar dan niet in de weg.
        var tempPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            Retry(() =>
            {
                File.WriteAllText(tempPath, json);
                if (File.Exists(FilePath))
                    File.Replace(tempPath, FilePath, FilePath + ".bak");
                else
                    File.Move(tempPath, FilePath);
                return true;
            });
        }
        finally
        {
            try { File.Delete(tempPath); } catch (IOException) { } catch (UnauthorizedAccessException) { } // alleen na een fout
        }
    }

    /// <summary>
    /// Alles was onleesbaar en we beginnen leeg: bewaar het beschadigde bestand apart (<c>.corrupt</c>), zodat de
    /// volgende keer opslaan het niet ongemerkt wegschrijft. Lukt dat niet, dan gaat het laden gewoon door.
    /// </summary>
    private void KeepCorruptCopy()
    {
        try
        {
            if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".corrupt", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static TResult Retry<TResult>(Func<TResult> action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (IOException) when (attempt < Attempts)
            {
                Thread.Sleep(100 * attempt);
            }
        }
    }
}
