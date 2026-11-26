using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DonkiePane.Core;
using DonkiePane.Shell;

namespace DonkiePane.App;

/// <summary>
/// Inhoud van een mappane: de bestanden van een map naar keuze, met een balk om terug te gaan. Een map openen gaat erin
/// kijken; typen zoekt in deze map en de mappen eronder. Alles wat met de iconen kan, is hetzelfde als in een
/// iconenpane (de shell voert het uit), alleen gaan nieuwe en hierheen gesleepte bestanden naar deze map.
/// </summary>
public sealed class FolderPaneView : UserControl
{
    private const int IconPixelSize = 96;
    private const int MaxItems = 1000;          // meer iconen maakt het pane traag en niet overzichtelijker
    private const int MaxSearchResults = 300;

    private readonly Pane _pane;
    private readonly IconsView _icons = new() { IsFolderView = true };
    private readonly TextBlock _location = new();
    private readonly Button _upButton;
    private readonly Button _homeButton;
    private readonly DispatcherTimer _reloadTimer;
    private readonly Dictionary<string, ItemView> _cache = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _search;

    /// <summary>De map waar je nu in kijkt (de map van het pane, of een map daaronder).</summary>
    public string CurrentFolder { get; private set; }

    /// <summary>De map van het pane.</summary>
    public string RootFolder => _pane.FolderPath ?? "";

    /// <summary>Wat er nu te zien is (voor tests).</summary>
    internal IconsView Icons => _icons;

    public FolderPaneView(Pane pane)
    {
        _pane = pane;
        CurrentFolder = RootFolder;

        _upButton = BarButton("", Loc.T("Map omhoog (Backspace)"), (_, _) => GoUp());
        _homeButton = BarButton("", Loc.T("Terug naar de map van het pane"), (_, _) => Navigate(RootFolder));
        var explorerButton = BarButton("", Loc.T("Openen in Verkenner"), (_, _) => OpenInExplorer());

        _location.VerticalAlignment = VerticalAlignment.Center;
        _location.TextTrimming = TextTrimming.CharacterEllipsis;
        _location.FontSize = 12;
        _location.Margin = new Thickness(4, 0, 4, 0);
        _location.SetResourceReference(TextBlock.ForegroundProperty, "PaneMutedText");

        var bar = new DockPanel { Margin = new Thickness(6, 4, 6, 0), LastChildFill = true };
        DockPanel.SetDock(_upButton, Dock.Left);
        DockPanel.SetDock(_homeButton, Dock.Left);
        DockPanel.SetDock(explorerButton, Dock.Right);
        bar.Children.Add(_upButton);
        bar.Children.Add(_homeButton);
        bar.Children.Add(explorerButton);
        bar.Children.Add(_location);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_icons);
        Content = root;

        _icons.FolderOpened += (_, path) => Navigate(path);
        _icons.FilterChanged += (_, text) => OnFilterChanged(text);
        PreviewKeyDown += OnPreviewKeyDown;

        _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            _cache.Clear(); // een gewijzigd bestand kan een ander icoon hebben
            if (_icons.FilterText.Length == 0) Reload();
        };

        Loaded += (_, _) => Navigate(CurrentFolder, force: true);
        Unloaded += (_, _) => StopWatching();
    }

    private static Button BarButton(string glyph, string tip, RoutedEventHandler click)
    {
        var button = new Button { Content = glyph, ToolTip = tip, FontSize = 11 };
        button.SetResourceReference(StyleProperty, "GlyphButton");
        button.SetResourceReference(ForegroundProperty, "PaneMutedText");
        button.Click += click;
        return button;
    }

    /// <summary>Het pane kijkt voortaan naar een andere map.</summary>
    public void ChangeRoot() => Navigate(RootFolder, force: true);

    /// <summary>Kijk in deze map. Een map buiten die van het pane kan niet (dan de map van het pane).</summary>
    public void Navigate(string folder, bool force = false)
    {
        if (!IsInsideRoot(folder)) folder = RootFolder;
        if (!force && string.Equals(folder, CurrentFolder, StringComparison.OrdinalIgnoreCase) && _watcher is not null) return;
        CurrentFolder = folder;
        _icons.TargetFolder = folder;
        _icons.CloseFilter(focusIcons: false);
        _cache.Clear();
        Watch(folder);
        Reload();
    }

    private bool IsInsideRoot(string folder)
    {
        if (RootFolder.Length == 0) return false;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RootFolder));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ||
               full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private void GoUp()
    {
        if (string.Equals(Path.TrimEndingDirectorySeparator(CurrentFolder), Path.TrimEndingDirectorySeparator(RootFolder), StringComparison.OrdinalIgnoreCase)) return;
        var current = CurrentFolder;
        Navigate(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(CurrentFolder)) ?? RootFolder);
        // Het mapje waar je uit kwam, is geselecteerd (zoals in Verkenner).
        _icons.Select([current]);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back && e.OriginalSource is not TextBox)
        {
            GoUp();
            e.Handled = true;
        }
        else if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            GoUp();
            e.Handled = true;
        }
    }

    private void OpenInExplorer()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{CurrentFolder}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(ex.Message, "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- Inlezen ----------

    private void Reload()
    {
        var atRoot = string.Equals(Path.TrimEndingDirectorySeparator(CurrentFolder), Path.TrimEndingDirectorySeparator(RootFolder), StringComparison.OrdinalIgnoreCase);
        _upButton.Visibility = _homeButton.Visibility = atRoot ? Visibility.Collapsed : Visibility.Visible;
        // Kort: de map van het pane met de map erboven, en daaronder waar je nu bent ("Documenten › Projecten › Website").
        var rootName = Path.GetFileName(Path.TrimEndingDirectorySeparator(RootFolder));
        var parentName = Path.GetFileName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(RootFolder)) ?? "");
        var path = new List<string>();
        if (parentName.Length > 0) path.Add(parentName);
        path.Add(rootName.Length > 0 ? rootName : RootFolder);
        if (!atRoot) path.AddRange(Path.GetRelativePath(RootFolder, CurrentFolder).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        _location.Text = string.Join(" › ", path);
        _location.ToolTip = CurrentFolder;

        List<string> paths;
        try
        {
            paths = Directory.EnumerateFileSystemEntries(CurrentFolder, "*", new EnumerationOptions
            {
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                IgnoreInaccessible = true,
            }).Take(MaxItems).ToList();
            _icons.SetEmptyHint(Loc.T("Deze map is leeg"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            paths = [];
            _icons.SetEmptyHint(RootFolder.Length == 0 ? Loc.T("Kies een map via het menu van dit pane") : Loc.F("Map niet gevonden: {0}", CurrentFolder));
        }

        _icons.SetView(_pane.IconSize, sorted: true);
        _icons.SetItems(Sorted(paths.Select(View).ToList()));
    }

    private ItemView View(string path)
    {
        if (_cache.TryGetValue(path, out var cached)) return cached;
        var view = new ItemView(path, path, ShellItemInfo.GetDisplayName(path), ShellItemInfo.GetIcon(path, IconPixelSize));
        _cache[path] = view;
        return view;
    }

    /// <summary>Gesorteerd zoals het pane wil; "eigen volgorde" heeft in een map geen betekenis, dan op naam.</summary>
    private List<ItemView> Sorted(List<ItemView> items)
    {
        var order = _pane.SortBy == ItemSortOrder.Manual ? ItemSortOrder.Name : _pane.SortBy;
        var byId = items.GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var sortable = byId.Values.Select(i =>
        {
            var isFolder = Directory.Exists(i.Path);
            DateTime modified;
            try { modified = isFolder ? Directory.GetLastWriteTimeUtc(i.Path) : File.GetLastWriteTimeUtc(i.Path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { modified = DateTime.MinValue; }
            return new SortableItem(i.Id, i.DisplayName, isFolder, isFolder ? "" : Path.GetExtension(i.Path).TrimStart('.'), modified);
        });
        return ItemSort.Order(sortable, order).Select(id => byId[id]).ToList();
    }

    /// <summary>Weergave of sortering van het pane is veranderd.</summary>
    public void Refresh()
    {
        if (_icons.FilterText.Length == 0) Reload();
        else _icons.SetView(_pane.IconSize, sorted: true);
    }

    // ---------- Zoeken in deze map en de mappen eronder ----------

    private void OnFilterChanged(string text)
    {
        _search?.Cancel();
        if (text.Length < 2)
        {
            if (text.Length == 0) Reload(); // filter weg: gewoon de map
            return;                        // één letter: alleen filteren wat er al staat
        }

        var cts = _search = new CancellationTokenSource();
        var folder = CurrentFolder;
        Task.Run(() => Search(folder, text, cts.Token), cts.Token).ContinueWith(t =>
        {
            if (cts.IsCancellationRequested || t.Status != TaskStatus.RanToCompletion) return;
            Dispatcher.InvokeAsync(() =>
            {
                if (cts.IsCancellationRequested || _icons.FilterText != text) return;
                _icons.SetItems(Sorted(t.Result.Select(View).ToList()));
            });
        }, TaskScheduler.Default);
    }

    /// <summary>Bestanden en mappen waarvan de naam de tekst bevat, in deze map en daaronder (verborgen en ontoegankelijk overgeslagen).</summary>
    internal static List<string> Search(string folder, string text, CancellationToken cancel)
    {
        var results = new List<string>();
        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(folder, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 8,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            }))
            {
                cancel.ThrowIfCancellationRequested();
                if (!Path.GetFileName(path).Contains(text, StringComparison.CurrentCultureIgnoreCase)) continue;
                results.Add(path);
                if (results.Count >= MaxSearchResults) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            // Wat er gevonden is, telt.
        }
        return results;
    }

    // ---------- Volgen ----------

    private void Watch(string folder)
    {
        StopWatching();
        try
        {
            if (!Directory.Exists(folder)) return;
            _watcher = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Attributes,
                IncludeSubdirectories = false,
            };
            FileSystemEventHandler changed = (_, _) => Dispatcher.InvokeAsync(ScheduleReload);
            _watcher.Created += changed;
            _watcher.Deleted += changed;
            _watcher.Changed += changed;
            _watcher.Renamed += (_, _) => Dispatcher.InvokeAsync(ScheduleReload);
            _watcher.Error += (_, _) => Dispatcher.InvokeAsync(() => { Watch(CurrentFolder); ScheduleReload(); });
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private void ScheduleReload()
    {
        _reloadTimer.Stop();
        _reloadTimer.Start();
    }

    private void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        _search?.Cancel();
    }
}
