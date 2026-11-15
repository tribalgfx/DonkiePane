using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DonkiePane.Core;
using DonkiePane.Shell;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace DonkiePane.App;

/// <summary>
/// Meerdere monitoren: panes blijven bereikbaar als een scherm wegvalt (laptop
/// uit het dock) en gaan terug naar hun plek zodra dat scherm terugkomt.
/// </summary>
public partial class App
{
    private DispatcherTimer? _displayTimer;

    private void WatchDisplays()
    {
        // Bij het (los)koppelen van een scherm komen er snel meerdere meldingen;
        // even wachten tot de situatie stabiel is.
        _displayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _displayTimer.Tick += (_, _) =>
        {
            _displayTimer.Stop();
            EnsurePanesVisible();
        };
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void StopWatchingDisplays() => SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

    // Komt binnen op een andere thread.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            _displayTimer?.Stop();
            _displayTimer?.Start();
        });

    /// <summary>
    /// Zet panes die buiten beeld staan terug op het dichtstbijzijnde scherm en
    /// onthoudt waar ze stonden. Komt dat scherm terug, dan gaan ze daarheen.
    /// </summary>
    private void EnsurePanesVisible()
    {
        var screens = Forms.Screen.AllScreens;
        var areas = screens.Select(s => ToPx(s.WorkingArea)).ToList();
        var names = screens.Select(s => s.DeviceName).ToHashSet();
        var changed = false;

        foreach (var window in _windows)
        {
            var pane = window.Pane;
            if (DesktopLayer.GetBounds(window) is not { } bounds) continue;

            // 1. Het scherm waar dit pane vandaan kwam is terug.
            if (pane.HomeMonitor is not null && names.Contains(pane.HomeMonitor) &&
                pane.HomeX is { } homeX && pane.HomeY is { } homeY)
            {
                DesktopLayer.SetBounds(window, homeX, homeY, bounds.Width, bounds.Height);
                bounds = new PxRect(homeX, homeY, bounds.Width, bounds.Height);
                ClearHome(pane);
                changed = true;
            }

            // 2. Het pane staat (nog) buiten beeld.
            if (ScreenFit.Fit(bounds, areas) is { } fitted)
            {
                if (pane.HomeX is null && pane.Monitor is not null)
                {
                    pane.HomeX = bounds.X;
                    pane.HomeY = bounds.Y;
                    pane.HomeMonitor = pane.Monitor;
                }
                DesktopLayer.SetBounds(window, fitted.X, fitted.Y, fitted.Width, fitted.Height);
                changed = true;
            }
            else if (MonitorOf(window) is { } name && name != pane.Monitor)
            {
                pane.Monitor = name; // bijhouden voor het geval dit scherm later wegvalt
                changed = true;
            }
        }

        if (changed) SaveLayout();
    }

    /// <summary>Haalt alle panes naar het hoofdscherm (voor het geval iets toch ergens verdwenen is).</summary>
    public void BringAllPanesToPrimaryScreen()
    {
        var area = Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1280, 720);
        var offset = 0;
        foreach (var window in _windows)
        {
            if (DesktopLayer.GetBounds(window) is not { } bounds) continue;
            var fitted = ScreenFit.Fit(new PxRect(area.X + 40 + offset, area.Y + 40 + offset, bounds.Width, bounds.Height), [ToPx(area)])
                         ?? new PxRect(area.X + 40 + offset, area.Y + 40 + offset, bounds.Width, bounds.Height);
            DesktopLayer.SetBounds(window, fitted.X, fitted.Y, fitted.Width, fitted.Height);
            ClearHome(window.Pane);
            window.Pane.Monitor = Forms.Screen.PrimaryScreen?.DeviceName;
            offset = (offset + 32) % 320;
        }
        SaveLayout();
    }

    private static void ClearHome(Pane pane)
    {
        pane.HomeX = null;
        pane.HomeY = null;
        pane.HomeMonitor = null;
    }

    private static string? MonitorOf(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle == IntPtr.Zero ? null : Forms.Screen.FromHandle(handle).DeviceName;
    }

    private static PxRect ToPx(System.Drawing.Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}
