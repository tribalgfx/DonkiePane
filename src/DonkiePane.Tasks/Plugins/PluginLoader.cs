using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;

namespace DonkiePane.Tasks.Plugins;

public sealed record LoadedPlugin(ITaskSourcePlugin Plugin, string AssemblyPath);

public sealed record PluginLoadError(string Path, string Message);

/// <summary>Een plug-in uit een map van de gebruiker die (nog) niet is toegestaan, met de afdruk van zijn bestanden.</summary>
public sealed record PendingPlugin(string Path, string Hash);

public sealed class PluginLoadResult
{
    public List<LoadedPlugin> Plugins { get; } = [];
    public List<PluginLoadError> Errors { get; } = [];

    /// <summary>Gevonden in een map van de gebruiker, maar niet geladen: eerst toestaan.</summary>
    public List<PendingPlugin> Pending { get; } = [];
}

/// <summary>
/// Laadt plug-ins uit mappen die de gebruiker zelf beheert; nooit iets van internet.
///
/// Opbouw: <c>&lt;map&gt;\&lt;naam&gt;\DonkiePane.Plugins.&lt;Naam&gt;.dll</c>. Elke plug-in krijgt een eigen
/// laadcontext, zodat de afhankelijkheden van de ene niet met die van de andere botsen. Een kapotte
/// of ontbrekende plug-in wordt als fout gemeld; de app gaat gewoon door.
///
/// <b>Veiligheid:</b> een plug-in is gewone code en draait met de rechten van de gebruiker. Zet er
/// alleen in wat je vertrouwt. Daarom wordt er niets uit een andere bron geladen dan deze mappen, en een plug-in uit
/// een map van de gebruiker pas als die hem heeft toegestaan (zie <see cref="Load(IEnumerable{string}, string?, Func{string, bool})"/>).
/// </summary>
public static class PluginLoader
{
    private const string FilePrefix = "DonkiePane.Plugins.";

    /// <summary>Laadt alles uit deze mappen (bijv. de map naast het programma, die de installer vult).</summary>
    public static PluginLoadResult Load(IEnumerable<string> folders) => Load(folders, null, _ => false);

    /// <summary>
    /// Laadt alles uit <paramref name="trustedFolders"/>, en uit <paramref name="userFolder"/> alleen de plug-ins
    /// waarvan <paramref name="isApproved"/> de afdruk (<see cref="Fingerprint"/>) kent. De rest komt in
    /// <see cref="PluginLoadResult.Pending"/>. Elk programma dat als jou draait, kan in die map schrijven; zo wordt
    /// een nieuwe of veranderde plug-in daar nooit ongemerkt geladen.
    /// </summary>
    public static PluginLoadResult Load(IEnumerable<string> trustedFolders, string? userFolder, Func<string, bool> isApproved)
    {
        var result = new PluginLoadResult();
        foreach (var folder in trustedFolders)
            foreach (var dll in FindDlls(folder, result)) LoadAssembly(dll, result);

        if (userFolder is null) return result;
        foreach (var dll in FindDlls(userFolder, result))
        {
            string hash;
            try
            {
                hash = Fingerprint(dll);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Errors.Add(new PluginLoadError(dll, ex.Message));
                continue;
            }
            if (isApproved(hash)) LoadAssembly(dll, result);
            else result.Pending.Add(new PendingPlugin(dll, hash));
        }
        return result;
    }

    /// <summary>
    /// De afdruk van een plug-in: SHA-256 over alle DLL's in zijn map (naam en inhoud). Verandert er één, ook een
    /// afhankelijkheid, dan is het een andere plug-in die opnieuw toestemming nodig heeft.
    /// </summary>
    public static string Fingerprint(string dll)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(dll))!;
        using var all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(folder, "*.dll").Order(StringComparer.OrdinalIgnoreCase))
        {
            all.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(file).ToLowerInvariant() + "\0"));
            using var stream = File.OpenRead(file);
            all.AppendData(SHA256.HashData(stream));
        }
        return Convert.ToHexString(all.GetHashAndReset());
    }

    private static List<string> FindDlls(string folder, PluginLoadResult result)
    {
        try
        {
            if (!Directory.Exists(folder)) return [];
            // Vaste volgorde: welke plug-in voorgaat als twee hetzelfde soort bron leveren, hangt niet van het bestandssysteem af.
            return Directory.EnumerateDirectories(folder).Order(StringComparer.OrdinalIgnoreCase)
                .SelectMany(dir => Directory.EnumerateFiles(dir, FilePrefix + "*.dll").Order(StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            result.Errors.Add(new PluginLoadError(folder, ex.Message)); // een onleesbare map: de rest gaat gewoon door
            return [];
        }
    }

    private static void LoadAssembly(string dll, PluginLoadResult result)
    {
        try
        {
            var context = new PluginLoadContext(dll);
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(dll));

            var found = 0;
            foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(ITaskSourcePlugin).IsAssignableFrom(t)))
            {
                if (type.GetConstructor(Type.EmptyTypes) is null)
                {
                    result.Errors.Add(new PluginLoadError(dll, Loc.F("{0} heeft geen publieke constructor zonder parameters.", type.Name)));
                    continue;
                }

                var plugin = (ITaskSourcePlugin)Activator.CreateInstance(type)!;
                // Alles wat de app later van de plug-in leest, nu één keer: een fout daarin komt dan hier en niet in het instellingenvenster.
                _ = (plugin.DisplayName, plugin.Description, plugin.Fields.Count, plugin.Actions.Count);
                if (string.IsNullOrWhiteSpace(plugin.TypeId))
                {
                    result.Errors.Add(new PluginLoadError(dll, Loc.F("{0} heeft geen TypeId.", type.Name)));
                    continue;
                }
                if (result.Plugins.Any(p => p.Plugin.TypeId == plugin.TypeId))
                {
                    result.Errors.Add(new PluginLoadError(dll, Loc.F("Het soort bron '{0}' bestaat al; deze is overgeslagen.", plugin.TypeId)));
                    continue;
                }
                result.Plugins.Add(new LoadedPlugin(plugin, dll));
                found++;
            }

            if (found == 0 && result.Errors.All(e => e.Path != dll))
                result.Errors.Add(new PluginLoadError(dll, Loc.T("Geen plug-in gevonden in dit bestand.")));
        }
#pragma warning disable CA1031 // een plug-in is code van buiten: wat hij ook gooit (ook uit een getter of statische constructor), de app start
        catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            result.Errors.Add(new PluginLoadError(dll, (ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message));
        }
    }

    /// <summary>
    /// De gedeelde onderdelen (<c>DonkiePane.Core</c> en <c>DonkiePane.Tasks</c>) komen altijd van de app zelf. Met
    /// een eigen kopie in de plug-inmap zou <c>ITaskSourcePlugin</c> een ander type zijn en de koppeling mislukken.
    /// </summary>
    private sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(pluginPath), isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(Path.GetFullPath(pluginPath));

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is "DonkiePane.Core" or "DonkiePane.Tasks") return null; // naar de standaardcontext

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
