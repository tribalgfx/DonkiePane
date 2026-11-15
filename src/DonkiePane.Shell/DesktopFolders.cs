using System.IO;
using DonkiePane.Core;

namespace DonkiePane.Shell;

/// <summary>
/// De twee mappen waaruit het Windows-bureaublad bestaat: je eigen
/// bureaublad en het openbare bureaublad (voor alle gebruikers). DonkiePane
/// leest ze alleen; bestanden worden nooit verplaatst of verwijderd.
/// </summary>
public sealed class DesktopFolders : IDisposable
{
    private readonly Dictionary<string, string> _folders;
    private readonly List<FileSystemWatcher> _watchers = [];

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
    public IReadOnlyList<string> EnumerateItems()
    {
        var items = VirtualDesktopItems.EnumerateEnabled()
            .Select(clsid => DesktopItemId.Create(DesktopItemId.Shell, clsid))
            .ToList();

        foreach (var (location, folder) in _folders)
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                if (IsHidden(entry)) continue;
                items.Add(DesktopItemId.Create(location, entry.Name));
            }
        }
        return items;
    }

    public string? ResolvePath(string id)
    {
        var (location, fileName) = DesktopItemId.Parse(id);
        if (location == DesktopItemId.Shell) return VirtualDesktopItems.ParsingName(fileName);
        return _folders.TryGetValue(location, out var folder) ? Path.Combine(folder, fileName) : null;
    }

    public void StartWatching()
    {
        foreach (var (location, folder) in _folders)
        {
            if (!Directory.Exists(folder)) continue;
            var watcher = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes,
                IncludeSubdirectories = false,
            };
            watcher.Created += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            watcher.Deleted += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            watcher.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty); // bijv. verborgen gemaakt
            watcher.Renamed += (_, e) =>
            {
                Renamed?.Invoke(this, (DesktopItemId.Create(location, e.OldName!), DesktopItemId.Create(location, e.Name!)));
                Changed?.Invoke(this, EventArgs.Empty);
            };
            // Bij een overvolle buffer missen we events; dan alles opnieuw inlezen.
            watcher.Error += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    private static bool IsHidden(FileSystemInfo entry) =>
        (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    public void Dispose()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }
}
