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
    public string Title { get; set; } = "Nieuw pane";
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

    /// <summary>
    /// De Inbox vangt bureaubladitems op die nog in geen enkel pane staan.
    /// Er is er altijd precies één.
    /// </summary>
    public bool IsInbox { get; set; }

    /// <summary>Bureaubladitems in dit pane, in weergavevolgorde (zie <see cref="DesktopItemId"/>). Alleen bij <see cref="PaneKind.Icons"/>.</summary>
    public List<string> Items { get; set; } = [];

    /// <summary>De takenlijst die dit pane toont. Alleen bij <see cref="PaneKind.Tasks"/>. Bij Google Taken is dit het ID van de lijst bij Google.</summary>
    public string? TaskListId { get; set; }

    /// <summary>Waar de taken vandaan komen: <see cref="TaskSources.Local"/> of <see cref="TaskSources.Google"/>. Alleen bij <see cref="PaneKind.Tasks"/>.</summary>
    public string TaskSource { get; set; } = TaskSources.Local;

    public static Pane NewGoogleTaskPane(string googleListId, string listTitle) => new()
    {
        Kind = PaneKind.Tasks,
        Title = listTitle,
        Width = 320,
        Height = 384,
        TaskSource = TaskSources.Google,
        TaskListId = googleListId,
    };

    public static Pane NewTaskPane() => new()
    {
        Kind = PaneKind.Tasks,
        Title = "Taken",
        Width = 300,
        Height = 360,
        TaskListId = Guid.NewGuid().ToString("N"),
    };
}
