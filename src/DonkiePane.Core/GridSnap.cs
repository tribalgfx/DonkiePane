namespace DonkiePane.Core;

/// <summary>
/// Het ruitjesraster: panes klikken met hun hoek en afmetingen vast op een
/// raster van <c>cell</c> eenheden (WPF-eenheden), met de oorsprong op 0,0.
/// </summary>
public static class GridSnap
{
    public const double DefaultCell = 32;

    /// <summary>Dichtstbijzijnde rasterlijn.</summary>
    public static double Position(double value, double cell) =>
        cell <= 0 ? value : Math.Round(value / cell, MidpointRounding.AwayFromZero) * cell;

    /// <summary>
    /// Dichtstbijzijnde veelvoud van de cel, niet kleiner dan <paramref name="min"/>
    /// (naar boven afgerond op een hele cel, zodat een pane nooit kleiner wordt dan kan).
    /// </summary>
    public static double Size(double value, double cell, double min)
    {
        if (cell <= 0) return Math.Max(value, min);
        var snapped = Math.Round(value / cell, MidpointRounding.AwayFromZero) * cell;
        if (snapped < min) snapped = Math.Ceiling(min / cell) * cell;
        return snapped;
    }

    /// <summary>
    /// Verschuiving van het raster ten opzichte van een venster dat op
    /// <paramref name="origin"/> staat: hoe ver de eerste rasterlijn rechts
    /// (of onder) van de venster-oorsprong ligt, om het raster te tekenen.
    /// </summary>
    public static double Offset(double origin, double cell)
    {
        if (cell <= 0) return 0;
        var offset = -origin % cell;
        return offset < 0 ? offset + cell : offset;
    }
}
