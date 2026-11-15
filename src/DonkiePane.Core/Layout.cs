namespace DonkiePane.Core;

/// <summary>Alle panes op het bureaublad; dit wordt als geheel opgeslagen.</summary>
public sealed class Layout
{
    public int Version { get; set; } = 1;
    public List<Pane> Panes { get; set; } = [];
}
