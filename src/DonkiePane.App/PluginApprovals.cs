using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DonkiePane.App;

/// <summary>
/// Welke plug-ins uit <c>%APPDATA%\DonkiePane\plugins</c> je hebt toegestaan, als afdrukken van hun bestanden (zie
/// <see cref="Tasks.Plugins.PluginLoader.Fingerprint"/>), versleuteld met Windows in <c>plugins-allowed.dat</c>.
/// Een programma dat als jou draait kan dit in principe ook aanpassen; het voorkomt dat een plug-in daar ongemerkt
/// meeloopt, maar is geen muur tegen iets dat DonkiePane gericht aanvalt.
/// </summary>
internal sealed class PluginApprovals(string path)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DonkiePane.PluginApprovals.v1");

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DonkiePane", "plugins-allowed.dat");

    private HashSet<string>? _hashes;

    public bool IsApproved(string hash) => Hashes().Contains(hash);

    public void Approve(IEnumerable<string> hashes)
    {
        var all = Hashes();
        foreach (var hash in hashes) all.Add(hash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(all.Order(StringComparer.Ordinal).ToList()), Entropy, DataProtectionScope.CurrentUser);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }

    private HashSet<string> Hashes()
    {
        if (_hashes is not null) return _hashes;
        _hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(path))
            {
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
                foreach (var hash in JsonSerializer.Deserialize<List<string>>(plain) ?? []) _hashes.Add(hash);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            // Niet te lezen (ander account, beschadigd): dan niets toegestaan; je kunt het opnieuw toestaan.
        }
        return _hashes;
    }
}
