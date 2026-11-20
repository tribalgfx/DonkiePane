using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using static DonkiePane.Shell.ShellNative;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace DonkiePane.Shell;

/// <summary>
/// Het "neerzetten" van Verkenner voor een map of item: bestanden op een map zetten, op de Prullenbak, op een
/// programma (dat ze dan opent), of in de bureaubladmap. De shell bepaalt zelf wat er gebeurt (verplaatsen,
/// kopiëren, snelkoppeling; Shift/Ctrl/Alt en rechts slepen werken zoals altijd).
/// </summary>
public sealed class ShellDropTarget : IDisposable
{
    private readonly IDropTarget _target;
    private bool _entered;
    private bool _disposed;

    private ShellDropTarget(IDropTarget target) => _target = target;

    /// <summary><c>null</c> als het item niets kan aannemen (bijv. een tekstbestand).</summary>
    public static ShellDropTarget? For(string path)
    {
        try
        {
            using var item = new ItemsInFolder([path]);
            return item.GetUIObject<IDropTarget>(ShellContextMenu.OwnerHandle, IID_IDropTarget) is { } target
                ? new ShellDropTarget(target)
                : null;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or IOException or InvalidCastException)
        {
            return null;
        }
    }

    public DragDropEffects DragEnter(ComIDataObject data, DragDropKeyStates keys, DragDropEffects allowed)
    {
        var effect = (uint)allowed;
        _entered = _target.DragEnter(data, (uint)keys, Cursor(), ref effect) >= 0;
        return _entered ? (DragDropEffects)effect : DragDropEffects.None;
    }

    public DragDropEffects DragOver(DragDropKeyStates keys, DragDropEffects allowed)
    {
        if (!_entered) return DragDropEffects.None;
        var effect = (uint)allowed;
        return _target.DragOver((uint)keys, Cursor(), ref effect) >= 0 ? (DragDropEffects)effect : DragDropEffects.None;
    }

    public void DragLeave()
    {
        if (!_entered) return;
        _entered = false;
        _target.DragLeave();
    }

    public DragDropEffects Drop(ComIDataObject data, DragDropKeyStates keys, DragDropEffects allowed)
    {
        if (!_entered) return DragDropEffects.None;
        _entered = false;
        var effect = (uint)allowed;
        return _target.Drop(data, (uint)keys, Cursor(), ref effect) >= 0 ? (DragDropEffects)effect : DragDropEffects.None;
    }

    /// <summary>
    /// Zet de items neer zonder te slepen, zoals Plakken in Verkenner: het doel kiest binnen <paramref name="effect"/>
    /// (bijv. alleen verplaatsen bij knippen). Geeft terug wat het doel heeft gedaan.
    /// </summary>
    public DragDropEffects SimulateDrop(ComIDataObject data, DragDropEffects effect)
    {
        var entered = DragEnter(data, DragDropKeyStates.None, effect);
        if (entered == DragDropEffects.None)
        {
            DragLeave();
            return DragDropEffects.None;
        }
        return Drop(data, DragDropKeyStates.None, entered);
    }

    private static POINT Cursor()
    {
        GetCursorPos(out var pt);
        return pt;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DragLeave();
        Marshal.ReleaseComObject(_target);
    }
}

/// <summary>
/// Laat het sleepplaatje van Verkenner (iconen met een aantal) ook boven de panes zien. Zonder dit verdwijnt het
/// plaatje zodra je met de muis boven een pane komt.
/// </summary>
public static class DragImages
{
    private static IDropTargetHelper? _helper;

    private static IDropTargetHelper? Helper => _helper ??= Create<IDropTargetHelper>(CLSID_DragDropHelper);

    public static void Enter(IntPtr hwnd, ComIDataObject? data, DragDropEffects effect) => Try(h =>
    {
        if (data is null) return;
        GetCursorPos(out var pt);
        h.DragEnter(hwnd, data, ref pt, (uint)effect);
    });

    public static void Over(DragDropEffects effect) => Try(h =>
    {
        GetCursorPos(out var pt);
        h.DragOver(ref pt, (uint)effect);
    });

    public static void Leave() => Try(h => h.DragLeave());

    public static void Drop(ComIDataObject? data, DragDropEffects effect) => Try(h =>
    {
        if (data is null) { h.DragLeave(); return; }
        GetCursorPos(out var pt);
        h.Drop(data, ref pt, (uint)effect);
    });

    private static void Try(Action<IDropTargetHelper> action)
    {
        if (Helper is not { } helper) return;
        try { action(helper); }
        catch (COMException) { }
    }
}
