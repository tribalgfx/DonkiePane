using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DonkiePane.App;

/// <summary>
/// Bewaart het Google-inlogtoken versleuteld met Windows DPAPI: alleen jouw Windows-account
/// op deze pc kan het lezen. Het staat in <c>%APPDATA%\DonkiePane\google.token</c>, nooit in git.
/// </summary>
public sealed class DpapiTokenStore(string? path = null)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DonkiePane.Google.RefreshToken");

    private readonly string _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DonkiePane", "google.token");

    public string? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException)
        {
            // Ander Windows-account, andere pc of een beschadigd bestand: dan opnieuw inloggen.
            return null;
        }
    }

    public void Save(string refreshToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(refreshToken), Entropy, DataProtectionScope.CurrentUser);
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, protectedBytes);
        File.Move(temp, _path, overwrite: true);
    }

    public void Delete()
    {
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // Wissen mislukt (bestand in gebruik): dan blijft de koppeling tot de volgende keer bestaan.
        }
    }
}
