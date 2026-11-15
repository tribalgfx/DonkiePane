namespace DonkiePane.Core;

/// <summary>
/// Alle wijzigingen aan welke items in welk pane staan. Hoofdregel: elk
/// bureaubladitem staat in precies één pane, en er gaat nooit iets verloren;
/// wat nergens thuishoort belandt in de Inbox.
/// </summary>
public static class LayoutEditor
{
    public static Pane EnsureInbox(Layout layout)
    {
        var inboxes = layout.Panes.Where(p => p.IsInbox).ToList();
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
    /// <returns><c>true</c> als er iets veranderd is.</returns>
    public static bool Sync(Layout layout, IEnumerable<string> currentItems)
    {
        var current = new HashSet<string>(currentItems, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        var inboxCountBefore = layout.Panes.Count(p => p.IsInbox);
        var inbox = EnsureInbox(layout);
        changed |= inboxCountBefore != 1;

        foreach (var pane in layout.Panes)
        {
            var removed = pane.Items.RemoveAll(id => !current.Contains(id) || !seen.Add(id));
            changed |= removed > 0;
        }

        foreach (var id in current.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Contains(id)) continue;
            inbox.Items.Add(id);
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
