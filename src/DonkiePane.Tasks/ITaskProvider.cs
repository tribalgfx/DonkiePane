namespace DonkiePane.Tasks;

/// <summary>
/// Bron van taken: <see cref="LocalTaskProvider"/> (alleen deze pc) of
/// <see cref="GoogleTaskProvider"/> (Google Taken). Elk takenpane kiest zijn eigen bron.
///
/// Methodes geven kopieën terug: wijzig een taak en geef hem terug via
/// <see cref="UpdateAsync"/>. Fouten die de gebruiker moet zien, komen als
/// <see cref="TaskProviderException"/>; de aanroeper toont ze.
/// </summary>
public interface ITaskProvider
{
    TaskProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Een lijst is gewijzigd (door DonkiePane zelf of door synchronisatie).
    /// Kan op een achtergrondthread binnenkomen.
    /// </summary>
    event EventHandler<string>? ListChanged;

    /// <summary>De verbinding met de bron is veranderd (of hersteld). Kan op een achtergrondthread binnenkomen.</summary>
    event EventHandler<TaskProviderStatus>? StatusChanged;

    Task<IReadOnlyList<TaskItem>> GetTasksAsync(string listId, CancellationToken ct = default);

    Task AddAsync(string listId, TaskItem task, CancellationToken ct = default);

    /// <summary>Titel, afgevinkt, deadline en (als de bron dat kent) belangrijk van bestaande taken.</summary>
    Task UpdateAsync(IEnumerable<TaskItem> tasks, CancellationToken ct = default);

    /// <summary>
    /// Verplaatst een open taak: hij komt vóór de taak die nu op <paramref name="newIndex"/>
    /// staat in de lijst van open taken (-1 of te groot = achteraan).
    /// </summary>
    Task MoveAsync(string listId, string taskId, int newIndex, CancellationToken ct = default);

    Task DeleteAsync(string listId, string taskId, CancellationToken ct = default);

    /// <summary>
    /// Het pane van deze lijst is verwijderd. Lokale bronnen verwijderen de taken;
    /// Google Taken laat de lijst bij Google ongemoeid (zie <see cref="TaskProviderCapabilities.PaneOwnsTasks"/>).
    /// </summary>
    Task DeleteListAsync(string listId, CancellationToken ct = default);
}
