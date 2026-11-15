using System.Security.Cryptography;
using System.Text;

namespace DonkiePane.Tasks.Google;

/// <summary>
/// PKCE (RFC 7636): beschermt het inloggen bij apps die geen geheim kunnen bewaren.
/// Google schrijft het voor bij desktop-apps.
/// </summary>
public static class Pkce
{
    /// <summary>Een nieuwe verifier (86 tekens) met bijbehorende S256-challenge.</summary>
    public static (string Verifier, string Challenge) Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        return (verifier, ChallengeFor(verifier));
    }

    public static string ChallengeFor(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string RandomState() => Base64Url(RandomNumberGenerator.GetBytes(24));

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
