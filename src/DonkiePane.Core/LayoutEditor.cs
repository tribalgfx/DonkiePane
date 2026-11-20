namespace DonkiePane.Core;

/// <summary>
/// Alle wijzigingen aan welke items in welk pane staan. Hoofdregel: elk
/// bureaubladitem staat in precies één pane, en er gaat nooit iets verloren;
/// wat nergens thuishoort belandt in de Inbox.
/// </summary>
public static class LayoutEditor
{
    /// <summary>Grenzen voor de afmetingen van een pane (de kleinste is die van het venster zelf).</summary>
    public const double MinPaneWidth = 160, MinPaneHeight = 36, MaxPaneSize = 20_000;

    /// <summary>
    /// Maakt een layout uit een beschadigd of met de hand aangepast <c>layout.json</c> bruikbaar: geen lege lijsten
    /// als <c>null</c>, geen afmetingen waar WPF op vastloopt, en alleen een iconenpane kan de Inbox zijn.
    /// </summary>
    /// <returns><c>true</c> als er iets hersteld is.</returns>
    public static bool Repair(Layout layout)
    {
        var repaired = false;
        if (layout.Panes is null) { layout.Panes = []; repaired = true; }
        repaired |= layout.Panes.RemoveAll(p => p is null) > 0;

        foreach (var pane in layout.Panes)
        {
            if (pane.Items is null) { pane.Items = []; repaired = true; }
            repaired |= pane.Items.RemoveAll(string.IsNullOrEmpty) > 0;
            if (pane.Title is null) { pane.Title = ""; repaired = true; }
            if (pane.Theme is null) { pane.Theme = ""; repaired = true; }
            if (pane.TaskSource is null) { pane.TaskSource = TaskSources.Local; repaired = true; }
            if (pane.IsInbox && pane.Kind != PaneKind.Icons) { pane.IsInbox = false; repaired = true; } // toont geen bureaubladitems

            repaired |= Fix(pane.Width, 320, MinPaneWidth, MaxPaneSize, v => pane.Width = v);
            repaired |= Fix(pane.Height, 220, MinPaneHeight, MaxPaneSize, v => pane.Height = v);
            repaired |= Fix(pane.Left, 100, -MaxPaneSize, MaxPaneSize, v => pane.Left = v);
            repaired |= Fix(pane.Top, 100, -MaxPaneSize, MaxPaneSize, v => pane.Top = v);
        }
        return repaired;

        static bool Fix(double value, double fallback, double min, double max, Action<double> set)
        {
            var fixedValue = double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
            if (fixedValue.Equals(value)) return false;
            set(fixedValue);
            return true;
        }
    }

    public static Pane EnsureInbox(Layout layout)
    {
        // Alleen een iconenpane: een takenpane als Inbox zou nieuwe bureaubladitems onzichtbaar maken (en bij het
        // samenvoegen hieronder verdwijnen).
        var inboxes = layout.Panes.Where(p => p.IsInbox && p.Kind == PaneKind.Icons).ToList();
        if (inboxes.Count > 0)
        {
            // Mocht er door een fout meer dan één zijn: samenvoegen.
            foreach (var extra in inboxes.Skip(1))
            {
                inboxes[0].Items.AddRange(extra.Items);
                layout.Panes.Remove(extra);
            }
            return inboxes[0];
        }

        var inbox = new Pane
        {
            Title = "Inbox",
            IsInbox = true,
            Left = 40,
            Top = 40,
            Width = 440,
            Height = 360,
        };
        layout.Panes.Add(inbox);
        return inbox;
    }

    /// <summary>
    /// Brengt de layout in lijn met wat er werkelijk op het bureaublad staat:
    /// verdwenen items eruit, dubbele vermeldingen weg, nieuwe items naar de Inbox.
    /// </summary>
    /// <param name="unavailableLocations">
    /// Bureaubladmappen die nu niet te lezen zijn (bijv. een netwerk- of OneDrive-map die nog niet verbonden is).
    /// Hun items blijven staan waar ze staan: een map die even weg is, mag de indeling niet wissen.
    /// </param>
    /// <param name="newItemsPane">
    /// Waar nieuwe items heen gaan in plaats van de Inbox, bijv. het pane waarin je net iets hebt geplakt of
    /// neergezet. Geen (bestaand) iconenpane = de Inbox.
    /// </param>
    /// <returns><c>true</c> als er iets veranderd is.</returns>
    public static bool Sync(Layout layout, IEnumerable<string> currentItems, IReadOnlyCollection<string>? unavailableLocations = null,
        Guid? newItemsPane = null)
    {
        var current = new HashSet<string>(currentItems, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        var inboxCountBefore = layout.Panes.Count(p => p.IsInbox);
        var inbox = EnsureInbox(layout);
        changed |= inboxCountBefore != 1;

        bool Unavailable(string id) =>
            unavailableLocations is { Count: > 0 } && id.IndexOf('/') is > 0 and var slash && unavailableLocations.Contains(id[..slash]);

        foreach (var pane in layout.Panes)
        {
            var removed = pane.Items.RemoveAll(id => (!current.Contains(id) && !Unavailable(id)) || !seen.Add(id));
            changed |= removed > 0;
        }

        var target = layout.Panes.FirstOrDefault(p => p.Id == newItemsPane && p.Kind == PaneKind.Icons) ?? inbox;
        foreach (var id in current.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Contains(id)) continue;
            target.Items.Add(id);
            changed = true;
        }

        return changed;
    }

    /// <summary>Hernoemd bestand: houdt dezelfde plek in hetzelfde pane.</summary>
    public static bool Rename(Layout layout, string oldId, string newId)
    {
        foreach (var pane in layout.Panes)
        {
            var index = pane.Items.FindIndex(id => string.Equals(id, oldId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) continue;
            pane.Items[index] = newId;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Verplaatst een item naar <paramref name="targetPaneId"/> op positie
    /// <paramref name="index"/> (of achteraan bij -1 of een te grote index).
    /// </summary>
    public static bool MoveItem(Layout layout, string itemId, Guid targetPaneId, int index = -1)
    {
        var target = layout.Panes.FirstOrDefault(p => p.Id == targetPaneId);
        if (target is null || target.Kind != PaneKind.Icons) return false;

        var source = layout.Panes.FirstOrDefault(p => p.Items.Contains(itemId, StringComparer.OrdinalIgnoreCase));
        if (source is null) return false;

        var oldIndex = source.Items.FindIndex(id => string.Equals(id, itemId, StringComparison.OrdinalIgnoreCase));
        source.Items.RemoveAt(oldIndex);

        // Binnen hetzelfde pane schuift alles na de oude plek één op.
        if (ReferenceEquals(source, target) && index > oldIndex) index--;

        if (index < 0 || index > target.Items.Count) index = target.Items.Count;
        target.Items.Insert(index, itemId);
        return true;
    }

    /// <summary>
    /// Verplaatst meerdere items, in deze volgorde en achter elkaar, naar <paramref name="targetPaneId"/>: vóór het
    /// item dat nu op <paramref name="index"/> staat (of achteraan bij -1 of een te grote index).
    /// </summary>
    public static bool MoveItems(Layout layout, IReadOnlyList<string> itemIds, Guid targetPaneId, int index = -1)
    {
        var target = layout.Panes.FirstOrDefault(p => p.Id == targetPaneId);
        if (target is null || target.Kind != PaneKind.Icons) return false;

        // Het item waar ze vóór moeten komen; zelf een van de verplaatste items telt niet (dan het eerstvolgende andere).
        bool Moving(string id) => itemIds.Contains(id, StringComparer.OrdinalIgnoreCase);
        var anchor = index >= 0 ? target.Items.Skip(index).FirstOrDefault(id => !Moving(id)) : null;

        var moved = false;
        foreach (var id in itemIds)
        {
            // Telkens vlak vóór het anker: zo blijft de volgorde van de verplaatste items behouden.
            var position = anchor is null ? -1 : target.Items.FindIndex(i => string.Equals(i, anchor, StringComparison.OrdinalIgnoreCase));
            moved |= MoveItem(layout, id, targetPaneId, position);
        }
        return moved;
    }

    /// <summary>
    /// Verwijdert een pane; de items gaan naar de Inbox. De Inbox zelf kan
    /// niet verwijderd worden.
    /// </summary>
    public static bool DeletePane(Layout layout, Guid paneId)
    {
        var pane = layout.Panes.FirstOrDefault(p => p.Id == paneId);
        if (pane is null || pane.IsInbox) return false;

        layout.Panes.Remove(pane);
        EnsureInbox(layout).Items.AddRange(pane.Items);
        return true;
    }
}
