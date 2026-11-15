using System.Windows;
using System.Windows.Media;
using DonkiePane.Core;

namespace DonkiePane.App;

public enum PatternKind
{
    None,

    /// <summary>Ruit: brede en smalle banen die elkaar kruisen, zoals een Schotse ruit.</summary>
    Plaid,

    /// <summary>Gingang: gelijke horizontale en verticale banen (de klassieke theedoek).</summary>
    Gingham,

    /// <summary>Verticale strepen.</summary>
    Stripes,
}

/// <summary>Kleuren van een thema. Alle kleuren zijn "#RRGGBB". Het patroon verschijnt alleen als het pane "Ruitpatroon" aan heeft.</summary>
public sealed record PaneTheme(
    string Id,
    string Name,
    string Base,        // donkere achtergrond
    string Title,       // titelbalk en vinkjes
    string Accent,      // ster (belangrijk)
    PatternKind Pattern,
    string PatternColor);

/// <summary>De kleuren die een pane werkelijk krijgt, na thema, eigen kleur en patroonkeuze.</summary>
public sealed record PaneLook(
    string Base,
    string Title,
    string TitleText,
    string Check,
    string Accent,
    PatternKind Pattern,   // None = effen
    string PatternColor,
    double Opacity);

public static class PaneThemes
{
    /// <summary>Rustig en zakelijk is de standaard; de gekleurde thema's blijven beschikbaar.</summary>
    public const string DefaultId = "antraciet";

    public static readonly IReadOnlyList<PaneTheme> All =
    [
        new("antraciet", "Antraciet", "#1B1C1E", "#4A4F57", "#F2C14E", PatternKind.Gingham, "#FFFFFF"),
        new("blauw", "Blauw", "#161B24", "#2F5D9E", "#F2C14E", PatternKind.Plaid, "#5B8FD6"),
        new("groen", "Groen", "#151D18", "#3E7B57", "#F2C14E", PatternKind.Gingham, "#5FB080"),
        new("klassiek-rood", "Rood", "#201A1A", "#B3262D", "#F2C14E", PatternKind.Plaid, "#D8454D"),
        new("mosterd", "Mosterd", "#211C12", "#A8741A", "#FFE08A", PatternKind.Stripes, "#D9A441"),
    ];

    /// <summary>De stappen in het menu: hoeveel procent doorzichtig (0 = massief).</summary>
    public static readonly IReadOnlyList<int> TransparencySteps = [0, 10, 20, 30, 40, 50, 60, 70, 80];

    public static PaneTheme Get(string? id) =>
        All.FirstOrDefault(t => t.Id == id) ?? All.First(t => t.Id == DefaultId);

    /// <summary>Zoek uit welke kleuren dit pane krijgt.</summary>
    public static PaneLook Resolve(Pane pane)
    {
        var theme = Get(pane.Theme);
        var pattern = pane.ShowPattern ? theme.Pattern : PatternKind.None;
        var opacity = pane.EffectiveOpacity;

        if (!TryParseColor(pane.CustomColor, out var custom))
            return new PaneLook(theme.Base, theme.Title, "#F5F0E8", theme.Title, theme.Accent, pattern, theme.PatternColor, opacity);

        // Eigen kleur: de titelbalk krijgt die kleur, de achtergrond een donkere variant ervan.
        var light = Luminance(custom) > 0.62;
        return new PaneLook(
            Base: Hex(Mix(custom, Colors.Black, 0.82)),
            Title: Hex(custom),
            TitleText: light ? "#1B1C1E" : "#F5F0E8",
            Check: Hex(Luminance(custom) < 0.2 ? Mix(custom, Colors.White, 0.5) : custom), // een bijna zwart vinkje valt weg
            Accent: theme.Accent,
            Pattern: pane.ShowPattern ? PatternKind.Gingham : PatternKind.None,
            PatternColor: Hex(Mix(custom, Colors.White, 0.35)),
            Opacity: opacity);
    }

    /// <summary>Zet de kleuren van <paramref name="pane"/> als resources, zodat alle onderdelen ze (dynamisch) overnemen.</summary>
    public static void Apply(ResourceDictionary resources, Pane pane)
    {
        var look = Resolve(pane);
        resources["PaneBackground"] = CreateBackground(look);
        resources["PaneTitleBackground"] = Solid(look.Title, Math.Min(1.0, look.Opacity + 0.08));
        resources["PaneTitleText"] = Solid(look.TitleText, 1);
        resources["PaneCheck"] = Solid(look.Check, 1);
        resources["PaneAccent"] = Solid(look.Accent, 1);
        resources["PaneBorder"] = Solid("#FFFFFF", 0x55 / 255.0);
    }

    // ---------- Kleurhulp ----------

    /// <summary>Leest "#RRGGBB"; alles anders (leeg, kapot) geldt als geen eigen kleur.</summary>
    public static bool TryParseColor(string? hex, out Color color)
    {
        color = default;
        if (hex is not { Length: 7 } || hex[0] != '#') return false;
        if (!int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb)) return false;
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    public static string ToHex(Color color) => Hex(color);

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Hoe licht een kleur oogt, van 0 (zwart) tot 1 (wit).</summary>
    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    /// <summary>Mengt <paramref name="from"/> met <paramref name="to"/>; <paramref name="amount"/> 0 = from, 1 = to.</summary>
    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    private static SolidColorBrush Solid(string hex, double opacity)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        color.A = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // ---------- Achtergrond ----------

    private static Brush CreateBackground(PaneLook look)
    {
        var basis = Solid(look.Base, look.Opacity);
        if (look.Pattern == PatternKind.None) return basis;

        // Doorzichtige banen in de patroonkleur; waar ze kruisen wordt het
        // vanzelf donkerder, net als bij geweven stof. Zacht, zodat het niet druk wordt.
        var band = Solid(look.PatternColor, 0x1C / 255.0);
        var thin = Solid(look.PatternColor, 0x14 / 255.0);

        var tile = new DrawingGroup();
        double size;
        switch (look.Pattern)
        {
            case PatternKind.Plaid:
                size = 64;
                tile.Children.Add(Rect(basis, 0, 0, size, size));
                AddBands(tile, band, 8, 20);   // brede baan
                AddBands(tile, thin, 44, 6);   // smalle baan
                break;
            case PatternKind.Gingham:
                size = 32;
                tile.Children.Add(Rect(basis, 0, 0, size, size));
                AddBands(tile, band, 0, 16);
                break;
            default: // Stripes
                size = 24;
                tile.Children.Add(Rect(basis, 0, 0, size, size));
                tile.Children.Add(Rect(band, 0, 0, 10, size));
                break;
        }

        var brush = new DrawingBrush(tile)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, size, size),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>Een verticale en een horizontale baan op dezelfde afstand.</summary>
    private static void AddBands(DrawingGroup group, Brush brush, double start, double width)
    {
        group.Children.Add(Rect(brush, start, 0, width, 10_000)); // verticaal
        group.Children.Add(Rect(brush, 0, start, 10_000, width)); // horizontaal
    }

    private static GeometryDrawing Rect(Brush brush, double x, double y, double w, double h) =>
        new(brush, null, new RectangleGeometry(new Rect(x, y, w, h)));
}
