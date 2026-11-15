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
    private readonly object _lock = new();
    private readonly string _path;

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DonkiePane", "secrets");

    public DpapiSecretStore(string connectionId, string? folder = null)
    {
        // Het id komt uit een configuratiebestand; alleen veilige tekens, zodat het nooit een pad kan zijn.
        if (!SafeId().IsMatch(connectionId)) throw new ArgumentException("Ongeldig id voor een verbinding.", nameof(connectionId));
        _path = Path.Combine(folder ?? DefaultFolder, connectionId + ".dat");
    }

    public string? Get(string key)
    {
        lock (_lock) return Read().GetValueOrDefault(key);
    }

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

    private Dictionary<string, string> Read()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? [];
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or JsonException)
        {
            // Ander Windows-account, andere pc of een beschadigd bestand: dan zijn de geheimen weg en vul je ze opnieuw in.
            return [];
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
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, protectedBytes);
        File.Move(temp, _path, overwrite: true);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex SafeId();
}
