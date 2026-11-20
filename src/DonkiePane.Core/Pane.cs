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

    /// <summary>
    /// De Inbox vangt bureaubladitems op die nog in geen enkel pane staan.
    /// Er is er altijd precies één.
    /// </summary>
    public bool IsInbox { get; set; }

    /// <summary>Bureaubladitems in dit pane, in weergavevolgorde (zie <see cref="DesktopItemId"/>). Alleen bij <see cref="PaneKind.Icons"/>.</summary>
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

    public static Pane NewTaskPane() => new()
    {
        Kind = PaneKind.Tasks,
        Title = Loc.T("Taken"),
        Width = 300,
        Height = 360,
        TaskListId = Guid.NewGuid().ToString("N"),
    };
}
