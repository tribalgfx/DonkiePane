namespace DonkiePane.Tasks;

/// <summary>Volgorde van open taken.</summary>
public static class TaskOrdering
{
    /// <summary>Open taken in weergavevolgorde.</summary>
    public static IEnumerable<TaskItem> Ordered(IEnumerable<TaskItem> tasks) =>
        tasks.Where(t => !t.IsCompleted).OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedAt);

    /// <summary>Voltooide taken, meest recent afgevinkt bovenaan.</summary>
    public static IEnumerable<TaskItem> Completed(IEnumerable<TaskItem> tasks) =>
        tasks.Where(t => t.IsCompleted).OrderByDescending(t => t.CompletedAt);

    /// <summary>Volgorde-waarde voor een nieuwe taak bovenaan de lijst.</summary>
    public static double TopOrder(IEnumerable<TaskItem> tasks)
    {
        var open = tasks.Where(t => !t.IsCompleted).ToList();
        return open.Count == 0 ? 0 : open.Min(t => t.SortOrder) - 1;
    }

    /// <summary>
    /// Verplaatst een taak naar <paramref name="newIndex"/> in de geordende
    /// lijst <paramref name="ordered"/> (komt vóór de taak die daar stond; -1 =
    /// achteraan) en nummert opnieuw.
    /// </summary>
    /// <returns>De taken waarvan de volgorde veranderd is (om op te slaan).</returns>
    public static IReadOnlyList<TaskItem> Move(IReadOnlyList<TaskItem> ordered, string taskId, int newIndex)
    {
        var list = ordered.ToList();
        var oldIndex = list.FindIndex(t => t.Id == taskId);
        if (oldIndex < 0) return [];

        var task = list[oldIndex];
        list.RemoveAt(oldIndex);
        if (newIndex > oldIndex) newIndex--;
        if (newIndex < 0 || newIndex > list.Count) newIndex = list.Count;
        list.Insert(newIndex, task);

        var changed = new List<TaskItem>();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].SortOrder == i) continue;
            list[i].SortOrder = i;
            changed.Add(list[i]);
        }
        return changed;
    }
}
