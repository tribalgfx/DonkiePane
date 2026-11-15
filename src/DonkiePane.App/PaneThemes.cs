using System.Windows;
using System.Windows.Media;

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

/// <summary>Kleuren en patroon van een pane. Alle kleuren zijn "#RRGGBB".</summary>
public sealed record PaneTheme(
    string Id,
    string Name,
    string Base,        // donkere achtergrond
    string Title,       // titelbalk en vinkjes
    string Accent,      // ster (belangrijk)
    PatternKind Pattern,
    string PatternColor);

public static class PaneThemes
{
    public const string DefaultId = "klassiek-rood";

    public static readonly IReadOnlyList<PaneTheme> All =
    [
        new("klassiek-rood", "Klassiek rood", "#201A1A", "#B3262D", "#F2C14E", PatternKind.Plaid, "#D8454D"),
        new("blauw", "Blauw", "#161B24", "#2F5D9E", "#F2C14E", PatternKind.Plaid, "#5B8FD6"),
        new("groen", "Groen", "#151D18", "#3E7B57", "#F2C14E", PatternKind.Gingham, "#5FB080"),
        new("mosterd", "Mosterd", "#211C12", "#A8741A", "#FFE08A", PatternKind.Stripes, "#D9A441"),
        new("antraciet", "Antraciet", "#1B1C1E", "#4A4F57", "#F2C14E", PatternKind.None, "#FFFFFF"),
    ];

    public static PaneTheme Get(string? id) =>
        All.FirstOrDefault(t => t.Id == id) ?? All.First(t => t.Id == DefaultId);

    /// <summary>Zet de kleuren van <paramref name="theme"/> als resources, zodat alle onderdelen van een pane ze (dynamisch) overnemen.</summary>
    public static void Apply(ResourceDictionary resources, PaneTheme theme)
    {
        resources["PaneBackground"] = CreateBackground(theme);
        resources["PaneTitleBackground"] = Solid(theme.Title, alpha: 0xCC);
        resources["PaneCheck"] = Solid(theme.Title, alpha: 0xFF);
        resources["PaneAccent"] = Solid(theme.Accent, alpha: 0xFF);
        resources["PaneBorder"] = Solid("#FFFFFF", alpha: 0x55);
    }

    private static SolidColorBrush Solid(string hex, byte alpha)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        color.A = alpha;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Brush CreateBackground(PaneTheme theme)
    {
        var basis = Solid(theme.Base, alpha: 0xB8);
        if (theme.Pattern == PatternKind.None) return basis;

        // Doorzichtige banen in de patroonkleur; waar ze kruisen wordt het
        // vanzelf donkerder, net als bij geweven stof.
        var band = Solid(theme.PatternColor, alpha: 0x2A);
        var thin = Solid(theme.PatternColor, alpha: 0x1E);

        var tile = new DrawingGroup();
        double size;
        switch (theme.Pattern)
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
