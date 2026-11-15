namespace DonkiePane.Core;

/// <summary>Alle panes op het bureaublad plus instellingen; dit wordt als geheel opgeslagen.</summary>
public sealed class Layout
{
    public int Version { get; set; } = 1;
    public List<Pane> Panes { get; set; } = [];

    /// <summary>Verberg de eigen iconen van Windows zodat alleen de panes ze tonen.</summary>
    public bool HideDesktopIcons { get; set; } = true;

    /// <summary>Panes klikken bij verplaatsen en vergroten vast op het ruitjesraster.</summary>
    public bool SnapToGrid { get; set; } = true;

    /// <summary>Rastergrootte in WPF-eenheden.</summary>
    public double GridSize { get; set; } = GridSnap.DefaultCell;

    /// <summary>Hoe vaak (in seconden) takenbronnen op veranderingen van andere apparaten controleren.</summary>
    public int SyncSeconds { get; set; } = DefaultSyncSeconds;

    public const int DefaultSyncSeconds = 60;
    public const int MinSyncSeconds = 15;
    public const int MaxSyncSeconds = 300;

    /// <summary><see cref="SyncSeconds"/>, binnen de toegestane grenzen (een kapot of handmatig aangepast bestand mag niets slopen).</summary>
    public TimeSpan SyncInterval => TimeSpan.FromSeconds(Math.Clamp(SyncSeconds, MinSyncSeconds, MaxSyncSeconds));
}
