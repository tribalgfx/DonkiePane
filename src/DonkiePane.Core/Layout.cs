namespace DonkiePane.Core;

/// <summary>Alle panes op het bureaublad plus instellingen; dit wordt als geheel opgeslagen.</summary>
public sealed class Layout
{
    public int Version { get; set; } = 1;
    public List<Pane> Panes { get; set; } = [];

    /// <summary>Verberg de eigen iconen van Windows zodat alleen de panes ze tonen.</summary>
    public bool HideDesktopIcons { get; set; } = true;

    /// <summary>Dubbelklikken op een leeg stuk bureaublad verbergt alle panes; nog een keer haalt ze terug.</summary>
    public bool HideOnDesktopDoubleClick { get; set; } = true;

    /// <summary>
    /// <c>false</c> = nog niet getoond: het welkomstscherm komt bij de volgende start. <c>true</c> = getoond.
    /// <c>null</c> = layout van vóór het welkomstscherm: die gebruiker kent DonkiePane al en ziet het niet.
    /// </summary>
    public bool? WelcomeShown { get; set; }

    /// <summary>Panes klikken bij verplaatsen en vergroten vast op het ruitjesraster.</summary>
    public bool SnapToGrid { get; set; } = true;

    /// <summary>Rastergrootte in WPF-eenheden.</summary>
    public double GridSize { get; set; } = GridSnap.DefaultCell;

    /// <summary>Hoe vaak (in seconden) takenbronnen op veranderingen van andere apparaten controleren.</summary>
    public int SyncSeconds { get; set; } = DefaultSyncSeconds;

    /// <summary>Bij het starten en daarna eens per dag op GitHub kijken of er een nieuwe versie is.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>De taal ("nl" of "en"); leeg = de taal van Windows (zie <see cref="Loc.Resolve"/>).</summary>
    public string? Language { get; set; }

    /// <summary>De nieuwste versie waarover al een melding is getoond; die melding komt niet bij elke start terug.</summary>
    public string? NotifiedUpdateVersion { get; set; }

    /// <summary>Een melding bij het icoon bij de klok als een open taak vandaag zijn deadline heeft.</summary>
    public bool RemindDueTasks { get; set; } = true;

    /// <summary>De dag (jjjj-mm-dd) waar <see cref="DueRemindedTaskIds"/> bij hoort; een nieuwe dag begint leeg.</summary>
    public string? DueRemindersDate { get; set; }

    /// <summary>Taken waarover vandaag al een melding is geweest (bron/lijst/taak), zodat die niet bij elke controle terugkomt.</summary>
    public List<string> DueRemindedTaskIds { get; set; } = [];

    /// <summary>Hoe een opgerold pane even opengaat: alleen met de knop, bij aanwijzen met de muis, of met één klik.</summary>
    public RollUpOpen RollUpOpen { get; set; } = RollUpOpen.Button;

    /// <summary>
    /// De sneltoets die alle panes even boven je vensters haalt (bijv. <c>"Ctrl+Alt+Space"</c>, zie
    /// <see cref="Hotkey"/>). Leeg = geen sneltoets.
    /// </summary>
    public string? PeekHotkey { get; set; } = DefaultPeekHotkey;

    public const string DefaultPeekHotkey = "Ctrl+Alt+Space";

    /// <summary>Donkere of lichte panes, of zoals de apps in Windows.</summary>
    public AppTheme Appearance { get; set; } = AppTheme.Dark;

    /// <summary>Hoe rond de hoeken van de panes zijn, in WPF-eenheden (0 = recht).</summary>
    public double CornerRadius { get; set; } = DefaultCornerRadius;

    public const double DefaultCornerRadius = 10;
    public const double MaxCornerRadius = 20;

    /// <summary><see cref="CornerRadius"/> binnen de grenzen.</summary>
    public double EffectiveCornerRadius => double.IsFinite(CornerRadius) ? Math.Clamp(CornerRadius, 0, MaxCornerRadius) : DefaultCornerRadius;

    /// <summary>
    /// Bureaubladiconen die je zo veel dagen niet via een pane hebt geopend en die zo lang niet gewijzigd zijn, worden
    /// grijs (zie <see cref="ItemUsage"/>). 0 = uit.
    /// </summary>
    public int DimUnusedDays { get; set; }

    /// <summary>Regels die nieuwe bureaubladitems meteen in het juiste pane zetten, in volgorde: de eerste die past, telt.</summary>
    public List<FileRule> Rules { get; set; } = [];

    public const int DefaultSyncSeconds = 60;
    public const int MinSyncSeconds = 15;
    public const int MaxSyncSeconds = 300;

    /// <summary><see cref="SyncSeconds"/>, binnen de toegestane grenzen (een kapot of handmatig aangepast bestand mag niets slopen).</summary>
    public TimeSpan SyncInterval => TimeSpan.FromSeconds(Math.Clamp(SyncSeconds, MinSyncSeconds, MaxSyncSeconds));
}
