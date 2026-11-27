namespace DonkiePane.Core;

public static class TaskSources
{
    public const string Local = "local";
    public const string Google = "google";
}

public enum PaneKind
{
    /// <summary>Bureaubladiconen.</summary>
    Icons,

    /// <summary>Een takenlijst.</summary>
    Tasks,

    /// <summary>De inhoud van een map naar keuze, waarin je kunt rondkijken en zoeken.</summary>
    Folder,
}

/// <summary>Waar de achtergrond van een pane van gemaakt is.</summary>
public enum PaneMaterial
{
    /// <summary>Effen kleur (met de gekozen transparantie).</summary>
    Solid,

    /// <summary>Mica: de achtergrond van het bureaublad, sterk vervaagd, met de kleur van het thema eroverheen.</summary>
    Mica,
}

/// <summary>Hoe een iconenpane zijn iconen laat zien.</summary>
public enum IconSize
{
    /// <summary>Grote iconen met de naam eronder, zoals op het bureaublad.</summary>
    Large,

    /// <summary>Kleinere iconen met de naam eronder: er past meer in een pane.</summary>
    Small,

    /// <summary>Een lijst: klein icoon met de naam ernaast, één item per regel.</summary>
    List,
}

/// <summary>In welke volgorde een iconenpane zijn iconen laat zien.</summary>
public enum ItemSortOrder
{
    /// <summary>De volgorde waarin je ze zelf hebt gesleept.</summary>
    Manual,
    Name,
    Type,

    /// <summary>Laatst gewijzigd eerst.</summary>
    Modified,
}

/// <summary>
/// Eén vak ("ruitje") op het bureaublad. Positie en grootte zijn in
/// apparaat-onafhankelijke pixels (WPF-eenheden).
/// </summary>
public sealed class Pane
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public PaneKind Kind { get; set; } = PaneKind.Icons;
    public string Title { get; set; } = Loc.T("Nieuw pane");
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 220;
    public bool IsRolledUp { get; set; }

    /// <summary>Het scherm (bijv. <c>\\.\DISPLAY2</c>) waar dit pane voor het laatst stond.</summary>
    public string? Monitor { get; set; }

    /// <summary>
    /// Plek in schermpixels van vóór het pane is teruggehaald omdat zijn scherm
    /// wegviel. Komt <see cref="HomeMonitor"/> terug, dan gaat het pane daarheen.
    /// </summary>
    public int? HomeX { get; set; }
    public int? HomeY { get; set; }
    public string? HomeMonitor { get; set; }

    /// <summary>Naam van het thema (zie de themalijst in de app); onbekend = standaard.</summary>
    public string Theme { get; set; } = "";

    /// <summary>Eigen kleur ("#RRGGBB") die het thema vervangt; <c>null</c> = kleur van het thema.</summary>
    public string? CustomColor { get; set; }

    public const double DefaultOpacity = 0.7;
    public const double MinOpacity = 0.2;

    /// <summary>Hoe ondoorzichtig de achtergrond is (1 = massief, 0,2 = erg doorzichtig). De tekst blijft altijd helder.</summary>
    public double Opacity { get; set; } = DefaultOpacity;

    /// <summary><see cref="Opacity"/> binnen de grenzen (een kapot of handmatig aangepast bestand mag niets onleesbaar maken).</summary>
    public double EffectiveOpacity => double.IsNaN(Opacity) ? DefaultOpacity : Math.Clamp(Opacity, MinOpacity, 1.0);

    /// <summary>Toon het ruitpatroon van het thema. Standaard uit: rustig en zakelijk.</summary>
    public bool ShowPattern { get; set; }

    /// <summary>Effen of Mica. Bij Mica bepaalt <see cref="Opacity"/> hoe sterk de themakleur over de vervaagde achtergrond ligt.</summary>
    public PaneMaterial Material { get; set; } = PaneMaterial.Solid;

    /// <summary>
    /// Dit pane is een tabblad in het venster van het pane met dit ID. <c>null</c> = een eigen venster. De plek en
    /// grootte van een tabblad zijn die van dat venster; die van het tabblad zelf worden niet gebruikt.
    /// </summary>
    public Guid? TabHost { get; set; }

    /// <summary>Bij een pane met tabbladen: welk tabblad open staat. <c>null</c> of onbekend = het pane zelf.</summary>
    public Guid? ActiveTab { get; set; }

    /// <summary>De map die een mappane toont. Alleen bij <see cref="PaneKind.Folder"/>.</summary>
    public string? FolderPath { get; set; }

    /// <summary>
    /// De Inbox vangt bureaubladitems op die nog in geen enkel pane staan.
    /// Er is er altijd precies één.
    /// </summary>
    public bool IsInbox { get; set; }

    /// <summary>Grote of kleine iconen, of een lijst. Alleen bij <see cref="PaneKind.Icons"/>.</summary>
    public IconSize IconSize { get; set; } = IconSize.Large;

    /// <summary>
    /// De volgorde op het scherm. Bij <see cref="ItemSortOrder.Manual"/> is dat <see cref="Items"/>; anders gesorteerd,
    /// en blijft de eigen volgorde in <see cref="Items"/> bewaard voor als je terug wilt. Alleen bij <see cref="PaneKind.Icons"/>.
    /// </summary>
    public ItemSortOrder SortBy { get; set; } = ItemSortOrder.Manual;

    /// <summary>
    /// Eigen grootte van de iconen in WPF-eenheden (zie <see cref="IconMetrics"/>); <c>null</c> = de standaard van
    /// <see cref="IconSize"/>. Een andere weergave kiezen zet hem terug op de standaard. Iconen- en mappanes.
    /// </summary>
    public int? IconPixels { get; set; }

    /// <summary>Namen onder de iconen tonen. Uit: alleen iconen, de naam staat in de tooltip. Een lijst toont ze altijd.</summary>
    public bool ShowNames { get; set; } = true;

    /// <summary>Bureaubladitems in dit pane, in de eigen volgorde (zie <see cref="DesktopItemId"/>). Alleen bij <see cref="PaneKind.Icons"/>.</summary>
    public List<string> Items { get; set; } = [];

    /// <summary>De takenlijst die dit pane toont. Alleen bij <see cref="PaneKind.Tasks"/>. Bij Google Taken is dit het ID van de lijst bij Google.</summary>
    public string? TaskListId { get; set; }

    /// <summary>
    /// De verbinding (zie <c>sources.json</c>) waar de taken van dit pane vandaan komen. Leeg = lokale taken.
    /// Alleen bij <see cref="PaneKind.Tasks"/>.
    /// </summary>
    public string? SourceId { get; set; }

    /// <summary>Oud veld uit de tijd dat Google Taken ingebouwd was; alleen nog gelezen door de eenmalige overgang.</summary>
    public string TaskSource { get; set; } = TaskSources.Local;

    public static Pane NewSourcePane(string sourceId, string listId, string listTitle) => new()
    {
        Kind = PaneKind.Tasks,
        Title = listTitle,
        Width = 320,
        Height = 384,
        SourceId = sourceId,
        TaskListId = listId,
    };

    public static Pane NewFolderPane(string folderPath) => new()
    {
        Kind = PaneKind.Folder,
        Title = Path.GetFileName(Path.TrimEndingDirectorySeparator(folderPath)) is { Length: > 0 } name ? name : folderPath,
        Width = 360,
        Height = 300,
        FolderPath = folderPath,
        SortBy = ItemSortOrder.Name,
    };

    public static Pane NewTaskPane() => new()
    {
        Kind = PaneKind.Tasks,
        Title = Loc.T("Taken"),
        Width = 300,
        Height = 360,
        TaskListId = Guid.NewGuid().ToString("N"),
    };
}
