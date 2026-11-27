namespace DonkiePane.Core;

/// <summary>Hoe groot een icoon en zijn tegel zijn, afhankelijk van de weergave en de eigen keuzes van een pane.</summary>
public static class IconMetrics
{
    public const int Min = 16;
    public const int Max = 96;

    /// <summary>De stappen van groter en kleiner maken (Ctrl+scrollwiel of het menu).</summary>
    public static readonly IReadOnlyList<int> Steps = [16, 20, 24, 32, 40, 48, 64, 80, 96];

    /// <summary>De grootte die een weergave uit zichzelf heeft.</summary>
    public static int Default(IconSize mode) => mode switch
    {
        IconSize.Small => 32,
        IconSize.List => 20,
        _ => 48,
    };

    /// <summary>De grootte waarmee het pane zijn iconen toont: de eigen keuze (binnen de grenzen) of de standaard.</summary>
    public static int Pixels(IconSize mode, int? custom) => custom is { } px ? Math.Clamp(px, Min, Max) : Default(mode);

    public static int Pixels(Pane pane) => Pixels(pane.IconSize, pane.IconPixels);

    /// <summary>Een stap groter (<paramref name="steps"/> &gt; 0) of kleiner; aan de rand blijft hij staan.</summary>
    public static int Step(int current, int steps)
    {
        // De dichtstbijzijnde stap als vertrekpunt: een met de hand aangepaste grootte telt zo ook mee.
        var index = 0;
        for (var i = 1; i < Steps.Count; i++)
            if (Math.Abs(Steps[i] - current) < Math.Abs(Steps[index] - current)) index = i;
        // Tussen twee stappen in: groter is de eerstvolgende stap erboven, kleiner de eerste eronder.
        if (steps > 0 && Steps[index] > current) index--;
        else if (steps < 0 && Steps[index] < current) index++;
        return Steps[Math.Clamp(index + steps, 0, Steps.Count - 1)];
    }

    /// <summary>
    /// Hoe breed een tegel is. Met namen minstens zo breed als de weergave gewend is (dan passen namen), en breder bij
    /// grote iconen; zonder namen net om het icoon heen. Een lijst heeft geen vaste breedte (<see cref="double.NaN"/>).
    /// </summary>
    public static double TileWidth(IconSize mode, int pixels, bool showNames)
    {
        if (mode == IconSize.List) return double.NaN;
        if (!showNames) return pixels + 18;
        var minimum = mode == IconSize.Small ? 88 : 100;
        return Math.Max(minimum, pixels + 52);
    }

    /// <summary>Hoeveel fysieke pixels een icoon geladen moet worden om bij 200% schaling nog scherp te zijn.</summary>
    public static int LoadPixels(int pixels) => pixels <= 48 ? 96 : 192;
}
