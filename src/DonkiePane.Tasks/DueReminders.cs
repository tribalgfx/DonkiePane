namespace DonkiePane.Tasks;

/// <summary>Welke taken een melding krijgen omdat hun deadline vandaag is.</summary>
public static class DueReminders
{
    /// <summary>Open taken met als deadline precies <paramref name="today"/>, in de volgorde van de lijst.</summary>
    public static IReadOnlyList<TaskItem> DueToday(IEnumerable<TaskItem> tasks, DateOnly today) =>
        TaskOrdering.Ordered(tasks.ToList()).Where(t => !t.IsCompleted && t.DueDate == today).ToList();

    /// <summary>
    /// De tekst onder de melding: de titels onder elkaar, hooguit <paramref name="max"/>; wat er niet meer bij past,
    /// telt <paramref name="more"/> (bijv. "en {0} meer").
    /// </summary>
    public static string Body(IReadOnlyList<string> titles, string more, int max = 4)
    {
        var shown = titles.Take(max).Select(t => "• " + t);
        var text = string.Join("\n", shown);
        return titles.Count > max ? text + "\n" + string.Format(more, titles.Count - max) : text;
    }
}
