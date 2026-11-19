using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DonkiePane.Tasks.Plugins;

namespace DonkiePane.App;

/// <summary>
/// Geheimen (tokens, secrets) van één verbinding, versleuteld met Windows DPAPI: alleen jouw Windows-account
/// op deze pc kan ze lezen. Eén bestand per verbinding in <c>%APPDATA%\DonkiePane\secrets</c>; ze komen nooit
/// in <c>sources.json</c> en nooit in git.
/// </summary>
public sealed partial class DpapiSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DonkiePane.Secrets.v1");

    // Eén slot per bestand, gedeeld door alle instanties: de host maakt per aanroep een nieuwe, en de poll-thread
    // en het instellingenvenster kunnen tegelijk schrijven.
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Een virusscanner houdt een bestand soms even vast; zo vaak proberen we het opnieuw.</summary>
    private const int Attempts = 4;

    private readonly object _lock;
    private readonly string _path;

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DonkiePane", "secrets");

    public DpapiSecretStore(string connectionId, string? folder = null)
    {
        // Het id komt uit een configuratiebestand; alleen veilige tekens, zodat het nooit een pad kan zijn.
        if (!SafeId().IsMatch(connectionId)) throw new ArgumentException("Ongeldig id voor een verbinding.", nameof(connectionId));
        _path = Path.GetFullPath(Path.Combine(folder ?? DefaultFolder, connectionId + ".dat"));
        _lock = Locks.GetOrAdd(_path, _ => new object());
    }

    public string? Get(string key)
    {
        lock (_lock)
        {
            try
            {
                return Read().GetValueOrDefault(key);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null; // even niet te lezen: de bron meldt dat er iets ontbreekt, en het bestand blijft heel
            }
        }
    }

    /// <summary>
    /// Bewaart één geheim en laat de andere staan. Is het bestand er wel maar even niet te lezen, dan een fout in
    /// plaats van een bestand met alleen dit ene geheim: anders zou bijvoorbeeld het client-secret verdwijnen.
    /// </summary>
    public void Set(string key, string? value)
    {
        lock (_lock)
        {
            var all = Read();
            if (string.IsNullOrEmpty(value)) all.Remove(key); else all[key] = value;
            Write(all);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            try
            {
                File.Delete(_path);
            }
            catch (IOException)
            {
                // Wissen mislukt (bestand in gebruik): de volgende keer opnieuw proberen.
            }
        }
    }

    /// <summary>De geheimen; leeg als er geen bestand is. Een bestand dat in gebruik blijft, geeft een <see cref="IOException"/>.</summary>
    private Dictionary<string, string> Read()
    {
        if (!File.Exists(_path)) return [];
        var bytes = Retry(() => File.ReadAllBytes(_path));
        try
        {
            var plain = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? [];
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            // Ander Windows-account, andere pc of een beschadigd bestand: dan zijn de geheimen weg en vul je ze opnieuw in.
            return [];
        }
    }

    private static T Retry<T>(Func<T> action)
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

    private void Write(Dictionary<string, string> all)
    {
        if (all.Count == 0)
        {
            Clear();
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var protectedBytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(all), Entropy, DataProtectionScope.CurrentUser);
        var temp = $"{_path}.{Guid.NewGuid():N}.tmp"; // eigen naam per schrijfactie
        try
        {
            File.WriteAllBytes(temp, protectedBytes);
            Retry(() => { File.Move(temp, _path, overwrite: true); return true; });
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { } // alleen als het verplaatsen mislukte
        }
    }

    // \z en niet $: $ laat ook een afsluitende regelovergang toe.
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z")]
    private static partial Regex SafeId();
}
