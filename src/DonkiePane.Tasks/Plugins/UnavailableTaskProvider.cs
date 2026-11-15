namespace DonkiePane.Tasks.Plugins;

/// <summary>
/// Wat een pane gebruikt als zijn bron niet te gebruiken is (plug-in ontbreekt, verkeerde instellingen):
/// de reden verschijnt in het pane in plaats van dat er iets crasht of leeg blijft zonder uitleg.
/// </summary>
public sealed class UnavailableTaskProvider(string message) : ITaskProvider
{
    public TaskProviderCapabilities Capabilities { get; } = new(Importance: false, PaneOwnsTasks: false, Reorder: false);

    public event EventHandler<string>? ListChanged { add { } remove { } }

    public event EventHandler<TaskProviderStatus>? StatusChanged { add { } remove { } }

    public Task<IReadOnlyList<TaskItem>> GetTasksAsync(string listId, CancellationToken ct = default) => throw Error();

    public Task AddAsync(string listId, TaskItem task, CancellationToken ct = default) => throw Error();

    public Task UpdateAsync(IEnumerable<TaskItem> tasks, CancellationToken ct = default) => throw Error();

    public Task MoveAsync(string listId, string taskId, int newIndex, CancellationToken ct = default) => throw Error();

    public Task DeleteAsync(string listId, string taskId, CancellationToken ct = default) => throw Error();

    /// <summary>Het pane gaat weg; er is niets om op te ruimen en er mag niets mislukken.</summary>
    public Task DeleteListAsync(string listId, CancellationToken ct = default) => Task.CompletedTask;

    private TaskProviderException Error() => new(message);
}
