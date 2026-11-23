namespace DonkiePane.Core;

/// <summary>Wat er nodig is om een bureaubladitem te sorteren.</summary>
/// <param name="IsFolder">Een map, of een systeemicoon zoals de Prullenbak: die komen vooraan, zoals in Verkenner.</param>
/// <param name="Extension">De extensie zonder punt (leeg bij een map).</param>
public sealed record SortableItem(string Id, string Name, bool IsFolder, string Extension, DateTime Modified);

/// <summary>De volgorde van de iconen in een pane als het niet op de eigen volgorde staat.</summary>
public static class ItemSort
{
    /// <summary>
    /// De id's in de gekozen volgorde. Bij <see cref="ItemSortOrder.Manual"/> blijft de volgorde zoals hij binnenkomt.
    /// Mappen komen altijd eerst (behalve bij datum), en gelijke items houden hun eigen volgorde.
    /// </summary>
    public static IReadOnlyList<string> Order(IEnumerable<SortableItem> items, ItemSortOrder order)
    {
        var list = items.ToList();
        IEnumerable<SortableItem> sorted = order switch
        {
            ItemSortOrder.Name => list.OrderBy(i => !i.IsFolder).ThenBy(i => i.Name, NaturalComparer.Instance),
            ItemSortOrder.Type => list.OrderBy(i => !i.IsFolder)
                .ThenBy(i => i.Extension, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Name, NaturalComparer.Instance),
            ItemSortOrder.Modified => list.OrderByDescending(i => i.Modified).ThenBy(i => i.Name, NaturalComparer.Instance),
            _ => list,
        };
        return sorted.Select(i => i.Id).ToList();
    }

    /// <summary>
    /// Zoals Verkenner: getallen in een naam tellen als getal ("Foto 2" vóór "Foto 10"), hoofdletters maken niet uit.
    /// </summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
                {
                    var startX = i;
                    var startY = j;
                    while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                    while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                    var numberX = x[startX..i].TrimStart('0');
                    var numberY = y[startY..j].TrimStart('0');
                    // Langer getal (zonder voorloopnullen) is groter; even lang: cijfer voor cijfer.
                    var byNumber = numberX.Length != numberY.Length
                        ? numberX.Length.CompareTo(numberY.Length)
                        : string.CompareOrdinal(numberX, numberY);
                    if (byNumber != 0) return byNumber;
                    continue;
                }

                var startTextX = i;
                var startTextY = j;
                while (i < x.Length && !char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && !char.IsAsciiDigit(y[j])) j++;
                var byText = string.Compare(x[startTextX..i], y[startTextY..j], Loc.Culture, System.Globalization.CompareOptions.IgnoreCase);
                if (byText != 0) return byText;
            }
            return (x.Length - i).CompareTo(y.Length - j);
        }
    }
}
