using System.Globalization;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace DonkiePane.Tasks.Security;

/// <summary>
/// Een zelfondertekend certificaat vastpinnen op zijn SHA-256-vingerafdruk, in plaats van de
/// certificaatcontrole uit te zetten. Met een ingevulde vingerafdruk wordt alleen het certificaat
/// met precies die afdruk geaccepteerd; zonder vingerafdruk geldt de gewone controle van Windows.
/// </summary>
public static class CertPin
{
    /// <summary>"AA:BB:…" (of met spaties, streepjes, kleine letters) naar 64 hoofdletter-hexcijfers. Leeg = geen pin (<c>null</c>).</summary>
    /// <exception cref="FormatException">Er staat iets in dat geen SHA-256-vingerafdruk is.</exception>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var hex = new string(text.Where(c => c is not (':' or ' ' or '-' or '\t' or '\r' or '\n')).ToArray()).ToUpperInvariant();
        if (hex.Length != 64 || !hex.All(Uri.IsHexDigit))
            throw new FormatException("De vingerafdruk moet een SHA-256-afdruk zijn: 64 hexcijfers, bijvoorbeeld AA:BB:CC:…");
        return hex;
    }

    /// <summary>De SHA-256-vingerafdruk van het (DER-)certificaat, als 64 hoofdletter-hexcijfers.</summary>
    public static string Fingerprint(X509Certificate certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));

    /// <summary>Is dit certificaat toegestaan? Past in <c>RemoteCertificateValidationCallback</c>.</summary>
    /// <param name="pin">Genormaliseerde vingerafdruk (zie <see cref="Normalize"/>) of <c>null</c>.</param>
    public static bool Validate(X509Certificate? certificate, SslPolicyErrors errors, string? pin)
    {
        if (pin is null) return errors == SslPolicyErrors.None;
        if (certificate is null) return false;

        // Gelijke lengte is gegarandeerd (64); vergelijk in constante tijd.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Fingerprint(certificate)),
            Encoding.ASCII.GetBytes(pin));
    }

    /// <summary>Voor in een foutmelding: "AA:BB:…".</summary>
    public static string Format(string hex) =>
        string.Join(':', Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2).ToUpper(CultureInfo.InvariantCulture)));
}

/// <summary>De HTTP-verbinding die bronnen gebruiken: met een pin alleen dat ene certificaat, zonder pin de gewone controle.</summary>
public static class PinnedHttp
{
    /// <summary>Groter dan dit is geen antwoord met taken; zo kan een bron het geheugen niet vullen.</summary>
    public const long MaxResponseBytes = 16 * 1024 * 1024;

    public static SocketsHttpHandler CreateHandler(string? pin) => new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(10),
        // Geen doorverwijzingen volgen: het token hoort bij dit adres, en een loginpagina elders is geen taak.
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        SslOptions = new SslClientAuthenticationOptions
        {
            // Met een pin: alleen dat ene certificaat. Zonder pin: de gewone controle van Windows (nooit "alles goed").
            RemoteCertificateValidationCallback = (_, certificate, _, errors) => CertPin.Validate(certificate, errors, pin),
        },
    };
}
