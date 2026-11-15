namespace DonkiePane.Tasks;

/// <summary>
/// Bron van taken. Nu alleen <see cref="LocalTaskProvider"/>; een koppeling met
/// Microsoft To Do wordt een tweede implementatie, zonder dat de panes veranderen.
///
/// Methodes geven kopieën terug: wijzig een taak en geef hem terug via
/// <see cref="UpdateAsync"/>.
/// </summary>
public interface ITaskProvider
{
    /// <summary>Een lijst is gewijzigd (door DonkiePane zelf of, later, door synchronisatie).</summary>
    event EventHandler<string>? ListChanged;

    Task<IReadOnlyList<TaskItem>> GetTasksAsync(string listId, CancellationToken ct = default);

    Task AddAsync(string listId, TaskItem task, CancellationToken ct = default);

    Task UpdateAsync(IEnumerable<TaskItem> tasks, CancellationToken ct = default);

    Task DeleteAsync(string taskId, CancellationToken ct = default);

    /// <summary>Verwijdert alle taken van een lijst (bij het verwijderen van een taakpane).</summary>
    Task DeleteListAsync(string listId, CancellationToken ct = default);
}
