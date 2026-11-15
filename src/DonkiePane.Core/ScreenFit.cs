namespace DonkiePane.Core;

/// <summary>Een rechthoek in fysieke schermpixels.</summary>
public readonly record struct PxRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public PxRect Intersect(PxRect other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= x || bottom <= y ? default : new PxRect(x, y, right - x, bottom - y);
    }
}

/// <summary>
/// Houdt panes bereikbaar: zodra een scherm wegvalt (laptop uit het dock,
/// andere resolutie), kan een pane buiten beeld staan en niet meer te pakken zijn.
/// Rekent in fysieke pixels omdat monitoren een eigen schaling kunnen hebben.
/// </summary>
public static class ScreenFit
{
    /// <summary>Zoveel van een pane moet minstens op een scherm zichtbaar zijn om hem te kunnen pakken.</summary>
    public const int MinVisibleWidth = 96;
    public const int MinVisibleHeight = 40;

    /// <summary>Of het pane voldoende zichtbaar is op een van de schermen.</summary>
    public static bool IsReachable(PxRect pane, IReadOnlyList<PxRect> screens) =>
        screens.Any(s =>
        {
            var visible = pane.Intersect(s);
            return visible.Width >= Math.Min(MinVisibleWidth, pane.Width)
                && visible.Height >= Math.Min(MinVisibleHeight, pane.Height);
        });

    /// <summary>
    /// <c>null</c> als het pane goed staat; anders de nieuwe plek: op het
    /// dichtstbijzijnde scherm, met dezelfde afmetingen (een pane die groter is
    /// dan het scherm komt linksboven te staan).
    /// </summary>
    public static PxRect? Fit(PxRect pane, IReadOnlyList<PxRect> screens)
    {
        if (screens.Count == 0 || IsReachable(pane, screens)) return null;

        var centerX = pane.X + pane.Width / 2.0;
        var centerY = pane.Y + pane.Height / 2.0;
        var nearest = screens
            .OrderBy(s => Distance(centerX, centerY, s.X + s.Width / 2.0, s.Y + s.Height / 2.0))
            .First();

        return new PxRect(
            Clamp(pane.X, nearest.X, nearest.Right - pane.Width),
            Clamp(pane.Y, nearest.Y, nearest.Bottom - pane.Height),
            pane.Width,
            pane.Height);
    }

    private static int Clamp(int value, int min, int max) => max < min ? min : Math.Clamp(value, min, max);

    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
}
