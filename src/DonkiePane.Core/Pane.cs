namespace DonkiePane.Core;

/// <summary>
/// Eén vak ("ruitje") op het bureaublad. Positie en grootte zijn in
/// apparaat-onafhankelijke pixels (WPF-eenheden).
/// </summary>
public sealed class Pane
{
    public Guid Id { get; init; } = Guid.NewGuid();
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

    /// <summary>Bureaubladitems in dit pane, in weergavevolgorde (zie <see cref="DesktopItemId"/>).</summary>
    public List<string> Items { get; set; } = [];
}
