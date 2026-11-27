using System.IO;
using System.Windows.Threading;
using DonkiePane.Core;
using DonkiePane.Shell;

namespace DonkiePane.App;

/// <summary>
/// Iconen die je lang niet hebt geopend, grijs maken (Instellingen → Panes). Houdt bij wanneer je een
/// bureaubladitem via een pane opent, in <c>usage.json</c>.
/// </summary>
public partial class App
{
    private static readonly TimeSpan DimCheckInterval = TimeSpan.FromHours(1);

    private readonly ItemUsageStore _usageStore = new(ItemUsageStore.DefaultPath);
    private ItemUsage _usage = new();
    private bool _usageWritable = true; // niet te lezen (vergrendeld): dan ook niet overschrijven
    private DispatcherTimer? _dimTimer;

    private void LoadUsage()
    {
        try
        {
            _usage = _usageStore.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _usage = new ItemUsage();
            _usageWritable = false;
        }
    }

    private void SaveUsage()
    {
        if (!_usageWritable) return;
        try
        {
            _usageStore.Save(_usage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Niet erg: de volgende keer opnieuw. Hooguit wordt een icoon iets te vroeg of te laat grijs.
        }
    }

    /// <summary>Na het inlezen van het bureaublad: nieuwe items beginnen nu, verdwenen items worden vergeten.</summary>
    private void TrackUsage()
    {
        var ids = _layout.Panes.Where(p => p.Kind == PaneKind.Icons).SelectMany(p => p.Items);
        if (_usage.Track(ids, DateTime.UtcNow)) SaveUsage();
    }

    /// <summary>Elk uur opnieuw kijken: een icoon kan intussen over de grens zijn gegaan.</summary>
    private void StartDimming()
    {
        _dimTimer = new DispatcherTimer { Interval = DimCheckInterval };
        _dimTimer.Tick += (_, _) => UpdateDimming();
        _dimTimer.Start();
    }

    private void OnItemOpened(Pane pane, ItemView item)
    {
        _usage.Opened(item.Id, DateTime.UtcNow);
        SaveUsage();
        UpdateDimming(pane);
    }

    private void UpdateDimming()
    {
        foreach (var pane in _iconViews.Keys.ToList()) UpdateDimming(pane);
    }

    private void UpdateDimming(Pane pane)
    {
        if (!_iconViews.TryGetValue(pane, out var view)) return;
        var days = _layout.DimUnusedDays;
        if (days <= 0)
        {
            view.SetDimmed([]);
            return;
        }

        var now = DateTime.UtcNow;
        view.SetDimmed(pane.Items
            .Select(id => _itemCache.TryGetValue(id, out var item) ? item : null)
            .OfType<ItemView>()
            .Where(item => !VirtualDesktopItems.IsVirtual(item.Path)) // Prullenbak, Deze pc: van Windows zelf, nooit grijs
            .Where(item => _usage.IsUnused(item.Id, LastWrite(item.Path), now, days))
            .Select(item => item.Id));
    }

    private static DateTime? LastWrite(string path)
    {
        try
        {
            if (File.Exists(path)) return File.GetLastWriteTimeUtc(path);
            if (Directory.Exists(path)) return Directory.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
        return null;
    }
}
