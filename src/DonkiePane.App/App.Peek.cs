using System.Windows;
using System.Windows.Input;
using DonkiePane.Core;
using DonkiePane.Shell;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace DonkiePane.App;

/// <summary>
/// Een sneltoets haalt alle panes even boven je vensters, met het scherm erachter gedimd. Nog een keer de sneltoets,
/// Esc, ernaast klikken of naar een ander programma gaan (bijv. door iets te openen) zet ze terug op het bureaublad.
/// </summary>
public partial class App
{
    private const int PeekHotkeyId = 1;
    private const int EscapeHotkeyId = 2;
    private const int VK_ESCAPE = 0x1B;

    private GlobalHotkeys? _hotkeys;
    private bool _peeking;
    private readonly List<PeekOverlayWindow> _peekOverlays = [];

    private void StartPeekHotkey()
    {
        Deactivated += (_, _) =>
        {
            // Naar een ander programma (iets geopend vanuit een pane, of ergens anders geklikt): terug op het bureaublad.
            if (_peeking) Dispatcher.InvokeAsync(EndPeek);
        };
        try
        {
            _hotkeys = new GlobalHotkeys();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return; // zonder sneltoets werkt de rest gewoon (en via het icoon bij de klok)
        }
        _hotkeys.Pressed += (_, id) =>
        {
            if (id == PeekHotkeyId) TogglePeek();
            else if (id == EscapeHotkeyId) EndPeek();
        };
        ApplyPeekHotkey(_layout.PeekHotkey);
    }

    /// <summary>
    /// Stelt de sneltoets in (leeg = geen). Geeft een foutmelding als het niet kan (ongeldig, of al in gebruik door
    /// Windows of een ander programma), anders <c>null</c>. De keuze wordt altijd bewaard.
    /// </summary>
    private string? ApplyPeekHotkey(string? text)
    {
        _layout.PeekHotkey = string.IsNullOrWhiteSpace(text) ? "" : text;
        SaveLayout();
        if (_hotkeys is null) return Loc.T("Sneltoetsen zijn op deze pc niet beschikbaar.");
        _hotkeys.Unregister(PeekHotkeyId);
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (Hotkey.Parse(text) is not { } hotkey || !Enum.TryParse<Key>(hotkey.Key, ignoreCase: true, out var key))
            return Loc.T("Deze sneltoets kan niet. Gebruik Ctrl, Alt of Win met een gewone toets.");
        return _hotkeys.Register(PeekHotkeyId, (int)hotkey.Modifiers, KeyInterop.VirtualKeyFromKey(key))
            ? null
            : Loc.F("{0} is al in gebruik door Windows of een ander programma. Kies een andere.", hotkey.ToString());
    }

    private void TogglePeek()
    {
        if (_peeking) EndPeek();
        else BeginPeek();
    }

    private void BeginPeek()
    {
        if (_peeking) return;
        _peeking = true;

        // Een dun donker vlak over elk scherm, met de panes daarboven.
        var screens = Forms.Screen.AllScreens;
        while (_peekOverlays.Count < screens.Length)
        {
            var overlay = new PeekOverlayWindow();
            overlay.Dismissed += (_, _) => EndPeek();
            _peekOverlays.Add(overlay);
        }
        for (var i = 0; i < _peekOverlays.Count; i++)
        {
            if (i < screens.Length) _peekOverlays[i].ShowOn(screens[i].Bounds);
            else _peekOverlays[i].Hide();
        }

        var windows = _windows.Where(w => !w.IsClosed).ToList();
        foreach (var window in windows) window.Show(); // ook als ze verborgen waren
        DesktopLayer.BeginPeek(windows);
        if (_peekOverlays.Count > 0) DesktopLayer.Activate(_peekOverlays[0]); // zodat Esc en wegklikken werken
        _hotkeys?.Register(EscapeHotkeyId, 0, VK_ESCAPE);
    }

    private void EndPeek()
    {
        if (!_peeking) return;
        _peeking = false;
        _hotkeys?.Unregister(EscapeHotkeyId);
        var windows = _windows.Where(w => !w.IsClosed).ToList();
        DesktopLayer.EndPeek(windows);
        foreach (var overlay in _peekOverlays) overlay.Hide();
        if (_panesHidden) foreach (var window in windows) window.Hide();
    }

    private void StopPeek()
    {
        EndPeek();
        _hotkeys?.Dispose();
        _hotkeys = null;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnBackdropDisplayChanged;
    }

    // ---------- Achtergrond en licht/donker van Windows volgen ----------

    private void WatchLookChanges()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnBackdropDisplayChanged;
    }

    // Komen binnen op een andere thread.
    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) => Dispatcher.InvokeAsync(() =>
    {
        if (e.Category == UserPreferenceCategory.Desktop) WallpaperBackdrop.Invalidate(); // andere achtergrond
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            RefreshAllLooks(); // licht/donker of accentkleur van Windows
    });

    private void OnBackdropDisplayChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(WallpaperBackdrop.Invalidate);
}
