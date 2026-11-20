using System.IO;
using DonkiePane.Core;

namespace DonkiePane.Shell;

/// <summary>
/// De twee mappen waaruit het Windows-bureaublad bestaat: je eigen
/// bureaublad en het openbare bureaublad (voor alle gebruikers). DonkiePane
/// leest ze alleen; wat je zelf met een item doet (slepen, Delete, plakken) gaat via de shell (zie ShellItems).
/// </summary>
public sealed class DesktopFolders : IDisposable
{
    private readonly Dictionary<string, string> _folders;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = []; // per locatie; alleen onder _watchLock
    private readonly object _watchLock = new();
    private bool _disposed;

    /// <summary>Er is iets toegevoegd, verwijderd of hernoemd. Komt binnen op een achtergrondthread.</summary>
    public event EventHandler? Changed;

    /// <summary>Een item is hernoemd (oud id, nieuw id). Komt binnen op een achtergrondthread.</summary>
    public event EventHandler<(string OldId, string NewId)>? Renamed;

    public DesktopFolders()
    {
        _folders = new()
        {
            [DesktopItemId.User] = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            [DesktopItemId.Public] = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };
    }

    /// <summary>
    /// Alle zichtbare items: de systeemiconen die je in Windows hebt aangezet
    /// (zoals de Prullenbak) en de bestanden in beide mappen, zonder verborgen en
    /// systeembestanden zoals desktop.ini.
    /// </summary>
    /// <param name="unavailable">
    /// Krijgt de locaties (<see cref="DesktopItemId.User"/>, <see cref="DesktopItemId.Public"/>) waarvan de map nu niet
    /// te lezen is. Geef dat door aan <see cref="LayoutEditor.Sync"/>, zodat de items daarvan blijven staan.
    /// </param>
    public IReadOnlyList<string> EnumerateItems(ICollection<string>? unavailable = null)
    {
        var items = VirtualDesktopItems.EnumerateEnabled()
            .Select(clsid => DesktopItemId.Create(DesktopItemId.Shell, clsid))
            .ToList();

        foreach (var (location, folder) in _folders)
        {
            var found = new List<string>();
            try
            {
                if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
                foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                {
                    if (IsHidden(entry)) continue;
                    found.Add(DesktopItemId.Create(location, entry.Name));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Bijv. een omgeleide of OneDrive-map die (nog) niet bereikbaar is: niets van deze map, en de aanroeper
                // weet dat hij die items moet laten staan.
                unavailable?.Add(location);
                continue;
            }
            items.AddRange(found);
        }
        return items;
    }

    public string? ResolvePath(string id)
    {
        var (location, fileName) = DesktopItemId.Parse(id);
        if (location == DesktopItemId.Shell) return VirtualDesktopItems.ParsingName(fileName);
        return _folders.TryGetValue(location, out var folder) ? Path.Combine(folder, fileName) : null;
    }

    /// <summary>
    /// Volgt de mappen die nog geen werkende watcher hebben. Mag vaker worden aangeroepen: een map die eerst niet
    /// bereikbaar was, of waarvan de watcher na een fout is gestopt, wordt dan alsnog gevolgd.
    /// </summary>
    /// <returns><c>true</c> als er een watcher bij is gekomen (dan is opnieuw inlezen verstandig).</returns>
    public bool StartWatching()
    {
        var started = false;
        foreach (var (location, folder) in _folders)
        {
            lock (_watchLock)
            {
                if (_disposed || _watchers.ContainsKey(location)) continue;
            }
            if (!Directory.Exists(folder) || TryCreateWatcher(location, folder) is not { } watcher) continue;

            lock (_watchLock)
            {
                if (_disposed || !_watchers.TryAdd(location, watcher))
                {
                    watcher.Dispose();
                    continue;
                }
            }
            started = true;
        }
        return started;
    }

    private FileSystemWatcher? TryCreateWatcher(string location, string folder)
    {
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes,
                IncludeSubdirectories = false,
            };
            var self = watcher;
            watcher.Created += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            watcher.Deleted += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            watcher.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty); // bijv. verborgen gemaakt
            watcher.Renamed += (_, e) =>
            {
                Renamed?.Invoke(this, (DesktopItemId.Create(location, e.OldName!), DesktopItemId.Create(location, e.Name!)));
                Changed?.Invoke(this, EventArgs.Empty);
            };
            // Bij een overvolle buffer missen we events, en als de map wegvalt stopt de watcher. Dan deze watcher weg
            // (de volgende StartWatching maakt een nieuwe) en alles opnieuw inlezen.
            watcher.Error += (_, _) =>
            {
                StopWatching(location, self);
                Changed?.Invoke(this, EventArgs.Empty);
            };
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            watcher?.Dispose(); // de map viel net weg; een volgende poging pakt hem op
            return null;
        }
    }

    private void StopWatching(string location, FileSystemWatcher watcher)
    {
        lock (_watchLock)
        {
            if (_watchers.TryGetValue(location, out var current) && current == watcher) _watchers.Remove(location);
        }
        watcher.Dispose();
    }

    private static bool IsHidden(FileSystemInfo entry) =>
        (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    public void Dispose()
    {
        List<FileSystemWatcher> watchers;
        lock (_watchLock)
        {
            _disposed = true;
            watchers = [.. _watchers.Values];
            _watchers.Clear();
        }
        foreach (var watcher in watchers) watcher.Dispose();
    }
}
