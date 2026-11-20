using System.Reflection;
using System.Runtime.Loader;

namespace DonkiePane.Tasks.Plugins;

public sealed record LoadedPlugin(ITaskSourcePlugin Plugin, string AssemblyPath);

public sealed record PluginLoadError(string Path, string Message);

public sealed class PluginLoadResult
{
    public List<LoadedPlugin> Plugins { get; } = [];
    public List<PluginLoadError> Errors { get; } = [];
}

/// <summary>
/// Laadt plug-ins uit mappen die de gebruiker zelf beheert; nooit iets van internet.
///
/// Opbouw: <c>&lt;map&gt;\&lt;naam&gt;\DonkiePane.Plugins.&lt;Naam&gt;.dll</c>. Elke plug-in krijgt een eigen
/// laadcontext, zodat de afhankelijkheden van de ene niet met die van de andere botsen. Een kapotte
/// of ontbrekende plug-in wordt als fout gemeld; de app gaat gewoon door.
///
/// <b>Veiligheid:</b> een plug-in is gewone code en draait met de rechten van de gebruiker. Zet er
/// alleen in wat je vertrouwt. Daarom wordt er niets uit een andere bron geladen dan deze mappen.
/// </summary>
public static class PluginLoader
{
    private const string FilePrefix = "DonkiePane.Plugins.";

    public static PluginLoadResult Load(IEnumerable<string> folders)
    {
        var result = new PluginLoadResult();
        foreach (var folder in folders)
        {
            List<string> dlls;
            try
            {
                if (!Directory.Exists(folder)) continue;
                // Vaste volgorde: welke plug-in voorgaat als twee hetzelfde soort bron leveren, hangt niet van het bestandssysteem af.
                dlls = Directory.EnumerateDirectories(folder).Order(StringComparer.OrdinalIgnoreCase)
                    .SelectMany(dir => Directory.EnumerateFiles(dir, FilePrefix + "*.dll").Order(StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                result.Errors.Add(new PluginLoadError(folder, ex.Message)); // een onleesbare map: de rest gaat gewoon door
                continue;
            }
            foreach (var dll in dlls) LoadAssembly(dll, result);
        }
        return result;
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
