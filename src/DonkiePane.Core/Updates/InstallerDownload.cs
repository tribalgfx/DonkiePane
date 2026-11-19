using System.Security.Cryptography;

namespace DonkiePane.Core.Updates;

/// <summary>Er kan niet bijgewerkt worden; de tekst is bedoeld voor de gebruiker.</summary>
public sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Downloadt de installer van een release en controleert hem met het SHA-256-controlegetal van GitHub. Pas als dat
/// klopt, staat het bestand onder zijn eigen naam klaar; anders wordt het weggegooid.
/// </summary>
public static class InstallerDownload
{
    /// <summary>Een installer is zo'n 50 MB; iets wat veel groter is, is geen installer van DonkiePane.</summary>
    public const long MaxInstallerBytes = 300L * 1024 * 1024;

    /// <summary>Downloadt naar <paramref name="folder"/> en geeft het pad van de gecontroleerde installer.</summary>
    /// <param name="progress">Krijgt een getal van 0 tot 1 (alleen als de grootte bekend is).</param>
    /// <exception cref="UpdateException">Geen controlegetal, een verkeerd adres of een bestand dat niet klopt.</exception>
    /// <exception cref="HttpRequestException">Geen verbinding of een fout van GitHub.</exception>
    public static async Task<string> DownloadAsync(HttpClient http, ReleaseAsset asset, string folder,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (asset.Sha256 is null)
            throw new UpdateException("Bij deze versie staat geen controlegetal, dus DonkiePane installeert hem niet zelf. Download hem via de releasepagina.");
        if (asset.Download.Scheme != Uri.UriSchemeHttps || !asset.Download.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new UpdateException("De installer staat niet op GitHub, dus DonkiePane downloadt hem niet.");
        if (!ReleaseFeed.IsInstallerName(asset.Name))
            throw new UpdateException("De installer heeft een onverwachte naam, dus DonkiePane downloadt hem niet.");

        Directory.CreateDirectory(folder);
        RemoveEarlierDownloads(folder);

        // Een eigen map met een onvoorspelbare naam per download: twee downloads zitten elkaar niet in de weg, en een
        // ander programma kan niets van tevoren op de plek van de installer klaarzetten.
        var work = Path.Combine(folder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var target = Path.Combine(work, asset.Name);
        var partial = target + ".part";
        var finished = false;
        try
        {
            using var response = await http.GetAsync(asset.Download, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            if (length > MaxInstallerBytes || (length is { } announced && asset.Size > 0 && announced != asset.Size))
                throw new UpdateException("De installer heeft een andere grootte dan GitHub opgeeft, dus DonkiePane installeert hem niet.");
            var limit = asset.Size > 0 ? asset.Size : MaxInstallerBytes;
            var total = length ?? (asset.Size > 0 ? asset.Size : (long?)null);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    received += read;
                    if (received > limit) // nooit meer op schijf zetten dan er hoort te komen
                        throw new UpdateException("De installer is groter dan GitHub opgeeft, dus DonkiePane installeert hem niet.");
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    if (total is > 0) progress?.Report(Math.Min(1.0, (double)received / total.Value));
                }
            }

            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (actual != asset.Sha256)
                throw new UpdateException("De gedownloade installer klopt niet met het controlegetal en is weggegooid. Probeer het later opnieuw.");

            File.Move(partial, target, overwrite: true);
            finished = true;
            return target;
        }
        finally
        {
            TryDelete(partial);
            if (!finished) TryDeleteFolder(work);
        }
    }

    /// <summary>Vlak voor het starten: is de installer nog precies het bestand dat gecontroleerd is?</summary>
    public static bool IsUnchanged(string path, string sha256)
    {
        try
        {
            using var file = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(file)) == sha256;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Installers en mappen van eerdere updates.</summary>
    private static void RemoveEarlierDownloads(string folder)
    {
        foreach (var old in Directory.EnumerateFiles(folder, ReleaseFeed.InstallerPrefix + "*")) TryDelete(old); // oude indeling
        foreach (var old in Directory.EnumerateDirectories(folder)) TryDeleteFolder(old);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteFolder(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
