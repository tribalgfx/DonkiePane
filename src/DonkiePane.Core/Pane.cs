namespace DonkiePane.Core;

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

    /// <summary>
    /// De Inbox vangt bureaubladitems op die nog in geen enkel pane staan.
    /// Er is er altijd precies één.
    /// </summary>
    public bool IsInbox { get; set; }

    /// <summary>Bureaubladitems in dit pane, in weergavevolgorde (zie <see cref="DesktopItemId"/>). Alleen bij <see cref="PaneKind.Icons"/>.</summary>
    public List<string> Items { get; set; } = [];

    /// <summary>De takenlijst die dit pane toont. Alleen bij <see cref="PaneKind.Tasks"/>.</summary>
    public string? TaskListId { get; set; }

    public static Pane NewTaskPane() => new()
    {
        Kind = PaneKind.Tasks,
        Title = "Taken",
        Width = 300,
        Height = 360,
        TaskListId = Guid.NewGuid().ToString("N"),
    };
}
